import 'dart:math';

import 'package:dio/dio.dart';
import 'package:drift/drift.dart' show Value;
import 'package:uuid/uuid.dart';

import 'outbox_database.dart';

/// Result of attempting to drain eligible pending mutations in the outbox.
class OutboxDrainResult {
  const OutboxDrainResult({
    required this.syncedCount,
    required this.retryScheduledCount,
    required this.conflictCount,
    required this.syncedRecordIds,
  });

  final int syncedCount;
  final int retryScheduledCount;
  final int conflictCount;
  final Map<String, String> syncedRecordIds;
}

/// Outcome of replaying a previously queued mutation with its Idempotency-Key.
class SyncReplayOutcome {
  const SyncReplayOutcome({
    required this.statusCode,
    this.recordId,
    this.errorCode,
  });

  final int statusCode;
  final String? recordId;
  final String? errorCode;
}

/// Offline-first Drift outbox + idempotent Dio sync engine (Doc/01B-ENG).
class OfflineSyncEngine {
  OfflineSyncEngine({
    required this.database,
    required this.dio,
    DateTime Function()? nowUtc,
    String Function()? idGenerator,
  })  : _nowUtc = nowUtc ?? (() => DateTime.now().toUtc()),
        _idGenerator = idGenerator ?? const Uuid().v4,
        _monotonicWatch = Stopwatch()..start();

  final AppOutboxDatabase database;
  final Dio dio;
  final DateTime Function() _nowUtc;
  final String Function() _idGenerator;
  final Stopwatch _monotonicWatch;

  /// Persists an offline mutation locally with client-assigned UUIDv4.
  Future<LocalOutboxMutation> enqueueOfflineRecord({
    required String entityType,
    required String entityLocalId,
    required String payloadJson,
    String? parentEntityLocalId,
    String httpMethod = 'POST',
    String endpointPath = '/v1/sync/spike-records',
    String? actorIdentityId,
    String? agentSessionId,
    String? fobIdUsed,
    DateTime? fobTappedAt,
    DateTime? clientRecordedAtOverride,
  }) async {
    final String clientGuid = _idGenerator();
    final DateTime recordedAt = (clientRecordedAtOverride ?? _nowUtc()).toUtc();
    final int monotonicMs = _monotonicWatch.elapsedMilliseconds;

    await database.enqueueMutation(
      LocalOutboxMutationsCompanion.insert(
        clientGuid: clientGuid,
        entityType: entityType,
        entityLocalId: entityLocalId,
        parentEntityLocalId: _nullableValue(parentEntityLocalId),
        httpMethod: httpMethod,
        endpointPath: endpointPath,
        payloadJson: payloadJson,
        clientRecordedAt: recordedAt,
        monotonicElapsedMs: monotonicMs,
        actorIdentityId: _nullableValue(actorIdentityId),
        agentSessionId: _nullableValue(agentSessionId),
        fobIdUsed: _nullableValue(fobIdUsed),
        fobTappedAt: _nullableValue(fobTappedAt?.toUtc()),
      ),
    );

    final LocalOutboxMutation? persisted =
        await database.getByClientGuid(clientGuid);
    if (persisted == null) {
      throw StateError(
        'Enqueued outbox row for $clientGuid could not be read back.',
      );
    }
    return persisted;
  }

  /// Drains eligible pending mutations in strict ascending seq_id FIFO order.
  Future<OutboxDrainResult> drainOutbox({
    bool respectBackoff = true,
    int batchLimit = 25,
  }) async {
    final List<LocalOutboxMutation> pending =
        await database.getPendingBatch(limit: batchLimit);

    int syncedCount = 0;
    int retryScheduledCount = 0;
    int conflictCount = 0;
    final Map<String, String> syncedRecordIds = <String, String>{};

    for (final LocalOutboxMutation row in pending) {
      final DateTime now = _nowUtc();
      if (respectBackoff &&
          row.nextRetryAt != null &&
          now.isBefore(row.nextRetryAt!)) {
        continue;
      }

      try {
        final Response<dynamic> response = await dio.request<dynamic>(
          row.endpointPath,
          data: <String, Object?>{
            'clientGuid': row.clientGuid,
            'entityType': row.entityType,
            'payloadJson': row.payloadJson,
            'clientRecordedAt': row.clientRecordedAt.toUtc().toIso8601String(),
          },
          options: Options(
            method: row.httpMethod,
            headers: <String, String>{
              'Idempotency-Key': row.clientGuid,
            },
            validateStatus: (_) => true,
          ),
        );

        final int statusCode = response.statusCode ?? 0;
        if (statusCode == 200 || statusCode == 201) {
          final Map<String, dynamic> body = _asJsonMap(response.data);
          final Map<String, dynamic> data = _asJsonMap(body['data']);
          final String? recordId = data['recordId'] as String?;
          final String? serverAtRaw = data['serverReceivedAt'] as String?;
          final DateTime serverReceivedAt = serverAtRaw != null
              ? DateTime.parse(serverAtRaw).toUtc()
              : now;

          await database.markSynced(
            seqId: row.seqId,
            serverReceivedAt: serverReceivedAt,
          );
          if (recordId != null) {
            syncedRecordIds[row.clientGuid] = recordId;
          }
          syncedCount++;
          continue;
        }

        if (_isTransientStatus(statusCode)) {
          await _scheduleRetry(row, now, 'HTTP_$statusCode');
          retryScheduledCount++;
          continue;
        }

        final String errorCode =
            _extractErrorCode(response.data) ?? 'HTTP_$statusCode';
        await database.markConflictRejected(
          seqId: row.seqId,
          errorCode: errorCode,
        );
        conflictCount++;
      } on DioException catch (ex) {
        final int? statusCode = ex.response?.statusCode;
        if (statusCode != null && !_isTransientStatus(statusCode)) {
          final String errorCode =
              _extractErrorCode(ex.response?.data) ?? 'HTTP_$statusCode';
          await database.markConflictRejected(
            seqId: row.seqId,
            errorCode: errorCode,
          );
          conflictCount++;
        } else {
          await _scheduleRetry(
            row,
            now,
            ex.type.name.toUpperCase(),
          );
          retryScheduledCount++;
        }
      }
    }

    return OutboxDrainResult(
      syncedCount: syncedCount,
      retryScheduledCount: retryScheduledCount,
      conflictCount: conflictCount,
      syncedRecordIds: syncedRecordIds,
    );
  }

  /// Explicitly replays an outbox item using its original Idempotency-Key.
  Future<SyncReplayOutcome> replayMutation(
    String clientGuid, {
    String? overridePayloadJson,
  }) async {
    final LocalOutboxMutation? row = await database.getByClientGuid(clientGuid);
    if (row == null) {
      throw StateError('No outbox mutation found for $clientGuid.');
    }

    final Response<dynamic> response = await dio.request<dynamic>(
      row.endpointPath,
      data: <String, Object?>{
        'clientGuid': row.clientGuid,
        'entityType': row.entityType,
        'payloadJson': overridePayloadJson ?? row.payloadJson,
        'clientRecordedAt': row.clientRecordedAt.toUtc().toIso8601String(),
      },
      options: Options(
        method: row.httpMethod,
        headers: <String, String>{
          'Idempotency-Key': row.clientGuid,
        },
        validateStatus: (_) => true,
      ),
    );

    final int statusCode = response.statusCode ?? 0;
    final Map<String, dynamic> body = _asJsonMap(response.data);
    final Map<String, dynamic> data = _asJsonMap(body['data']);
    return SyncReplayOutcome(
      statusCode: statusCode,
      recordId: data['recordId'] as String?,
      errorCode: _extractErrorCode(response.data),
    );
  }

  Future<void> _scheduleRetry(
    LocalOutboxMutation row,
    DateTime now,
    String errorCode,
  ) {
    final int nextRetryCount = row.retryCount + 1;
    // Doc/01B-ENG §5.4 bounded exponential backoff: 5s * 2^retry, capped at 1h.
    final int exponent = min(row.retryCount, 10);
    final int delaySeconds = min(3600, 5 * (1 << exponent));
    return database.markRetryableFailure(
      seqId: row.seqId,
      nextRetryCount: nextRetryCount,
      nextRetryAt: now.add(Duration(seconds: delaySeconds)),
      errorCode: errorCode,
    );
  }

  static bool _isTransientStatus(int statusCode) {
    return statusCode == 408 || statusCode == 429 || statusCode >= 500;
  }

  static Map<String, dynamic> _asJsonMap(dynamic raw) {
    if (raw is Map<String, dynamic>) {
      return raw;
    }
    if (raw is Map) {
      return raw.cast<String, dynamic>();
    }
    return const <String, dynamic>{};
  }

  static String? _extractErrorCode(dynamic raw) {
    final Map<String, dynamic> root = _asJsonMap(raw);
    final Map<String, dynamic> err = _asJsonMap(root['error']);
    final Object? code = err['code'];
    return code is String ? code : null;
  }

  static Value<T?> _nullableValue<T extends Object>(T? value) {
    return value == null ? Value<T?>.absent() : Value<T?>(value);
  }
}
