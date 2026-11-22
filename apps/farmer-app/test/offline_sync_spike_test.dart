import 'dart:convert';
import 'dart:io';

import 'package:crypto/crypto.dart';
import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:farm_basics_app/network/certificate_pinning_adapter.dart';
import 'package:farm_basics_app/sync/offline_sync_engine.dart';
import 'package:farm_basics_app/sync/outbox_database.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  late HttpServer server;
  late String serverBaseUrl;
  final Map<String, _StoredIdempotency> idempotencyStore =
      <String, _StoredIdempotency>{};
  final List<Map<String, Object?>> serverRows = <Map<String, Object?>>[];
  int sequenceCounter = 0;

  setUp(() async {
    idempotencyStore.clear();
    serverRows.clear();
    sequenceCounter = 0;

    server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    serverBaseUrl = 'http://${server.address.address}:${server.port}';
    server.listen((HttpRequest request) async {
      if (request.method == 'POST' &&
          request.uri.path == '/v1/sync/spike-records') {
        final String? idempotencyKey = request.headers.value('Idempotency-Key');
        if (idempotencyKey == null || idempotencyKey.trim().isEmpty) {
          request.response.statusCode = 400;
          request.response.headers.contentType = ContentType.json;
          request.response.write(
            jsonEncode(<String, Object?>{
              'error': <String, String>{
                'code': 'IDEMPOTENCY_KEY_REQUIRED',
                'message': 'Idempotency-Key header is required.',
              },
            }),
          );
          await request.response.close();
          return;
        }

        final String rawBody = await utf8.decoder.bind(request).join();
        final Map<String, dynamic> body =
            (jsonDecode(rawBody) as Map).cast<String, dynamic>();
        final String clientGuid = body['clientGuid'] as String;
        final String entityType = body['entityType'] as String;
        final String payloadJson = body['payloadJson'] as String;
        final String clientRecordedAt = body['clientRecordedAt'] as String;

        final String canonicalFingerprint = sha256
            .convert(
              utf8.encode(
                jsonEncode(<String, String>{
                  'clientGuid': clientGuid,
                  'entityType': entityType,
                  'payloadJson': payloadJson,
                  'clientRecordedAt':
                      DateTime.parse(clientRecordedAt).toUtc().toIso8601String(),
                }),
              ),
            )
            .toString();

        final _StoredIdempotency? existing = idempotencyStore[idempotencyKey];
        if (existing != null) {
          if (existing.fingerprint != canonicalFingerprint) {
            request.response.statusCode = 409;
            request.response.headers.contentType = ContentType.json;
            request.response.write(
              jsonEncode(<String, Object?>{
                'error': <String, String>{
                  'code': 'IDEMPOTENCY_KEY_REUSED',
                  'message': 'Idempotency key reused with a different payload.',
                },
              }),
            );
            await request.response.close();
            return;
          }

          request.response.statusCode = existing.statusCode;
          request.response.headers.contentType = ContentType.json;
          request.response.write(existing.responseBody);
          await request.response.close();
          return;
        }

        final bool duplicateClientGuid = serverRows.any(
          (Map<String, Object?> r) => r['clientGuid'] == clientGuid,
        );
        if (duplicateClientGuid) {
          request.response.statusCode = 409;
          request.response.headers.contentType = ContentType.json;
          request.response.write(
            jsonEncode(<String, Object?>{
              'error': <String, String>{
                'code': 'CLIENT_GUID_ALREADY_SYNCED',
                'message': 'clientGuid already exists.',
              },
            }),
          );
          await request.response.close();
          return;
        }

        sequenceCounter++;
        final String recordId =
            '00000000-0000-4000-8000-${sequenceCounter.toString().padLeft(12, '0')}';
        final String serverReceivedAt = DateTime.now().toUtc().toIso8601String();
        serverRows.add(<String, Object?>{
          'recordId': recordId,
          'clientGuid': clientGuid,
          'entityType': entityType,
          'payloadJson': payloadJson,
          'clientRecordedAt': clientRecordedAt,
          'serverReceivedAt': serverReceivedAt,
        });

        final String responseJson = jsonEncode(<String, Object?>{
          'data': <String, Object?>{
            'recordId': recordId,
            'clientGuid': clientGuid,
            'clientRecordedAt': clientRecordedAt,
            'serverReceivedAt': serverReceivedAt,
          },
          'meta': <String, Object?>{
            'requestId': 'test-req-$sequenceCounter',
          },
        });

        idempotencyStore[idempotencyKey] = _StoredIdempotency(
          fingerprint: canonicalFingerprint,
          statusCode: 201,
          responseBody: responseJson,
        );

        request.response.statusCode = 201;
        request.response.headers.contentType = ContentType.json;
        request.response.write(responseJson);
        await request.response.close();
      } else {
        request.response.statusCode = 404;
        await request.response.close();
      }
    });
  });

  tearDown(() async {
    await server.close(force: true);
  });

  test(
    'WP-04 spike: offline drift enqueue -> disconnected retry -> reconnect sync -> 2x idempotent replay asserts 1 server row',
    () async {
      final AppOutboxDatabase db = AppOutboxDatabase(NativeDatabase.memory());
      addTearDown(db.close);

      // Start disconnected by pointing Dio at an unreachable ephemeral port.
      final Dio dio = Dio(
        BaseOptions(
          baseUrl: 'http://127.0.0.1:1',
          connectTimeout: const Duration(milliseconds: 300),
          receiveTimeout: const Duration(milliseconds: 300),
        ),
      )..httpClientAdapter = createPinnedHttpClientAdapter(
          allowedSha256Pins: const <String>{'pinned-test-digest'},
          allowInsecureLocalhost: true,
        );

      final DateTime fixedOfflineTime = DateTime.utc(2026, 11, 20, 8, 30, 0);
      final OfflineSyncEngine engine = OfflineSyncEngine(
        database: db,
        dio: dio,
        nowUtc: () => fixedOfflineTime,
      );

      // 1. Capture offline operation locally in Drift (S0.5.M1).
      final LocalOutboxMutation created = await engine.enqueueOfflineRecord(
        entityType: 'operation_log',
        entityLocalId: 'local-op-001',
        payloadJson: '{"crop":"cassava","task":"planting"}',
        clientRecordedAtOverride: fixedOfflineTime,
      );

      expect(created.syncStatus, equals('pending'));
      expect(created.retryCount, equals(0));
      expect(created.clientGuid, isNotEmpty);
      expect(created.clientRecordedAt.toUtc(), equals(fixedOfflineTime));

      // Drain while disconnected: record remains pending and backoff increments.
      final OutboxDrainResult offlineDrain = await engine.drainOutbox();
      expect(offlineDrain.syncedCount, equals(0));
      expect(offlineDrain.retryScheduledCount, equals(1));
      expect(serverRows, isEmpty);

      final LocalOutboxMutation? afterOfflineDrain =
          await db.getByClientGuid(created.clientGuid);
      expect(afterOfflineDrain, isNotNull);
      expect(afterOfflineDrain!.syncStatus, equals('pending'));
      expect(afterOfflineDrain.retryCount, equals(1));
      expect(afterOfflineDrain.nextRetryAt, isNotNull);

      // 2. Restore connectivity and drain outbox once (S0.5.M2).
      dio.options.baseUrl = serverBaseUrl;
      final OutboxDrainResult syncedDrain =
          await engine.drainOutbox(respectBackoff: false);
      expect(syncedDrain.syncedCount, equals(1));
      expect(syncedDrain.retryScheduledCount, equals(0));
      expect(syncedDrain.conflictCount, equals(0));
      expect(serverRows.length, equals(1));

      final String initialRecordId =
          syncedDrain.syncedRecordIds[created.clientGuid]!;
      expect(initialRecordId, isNotEmpty);

      final LocalOutboxMutation? afterSync =
          await db.getByClientGuid(created.clientGuid);
      expect(afterSync!.syncStatus, equals('synced'));
      expect(afterSync.serverReceivedAt, isNotNull);

      // Verify historical clientRecordedAt is preserved faithfully on server.
      expect(
        DateTime.parse(serverRows.single['clientRecordedAt']! as String).toUtc(),
        equals(fixedOfflineTime),
      );

      // 3. Replay exact same mutation TWICE with same Idempotency-Key (S0.5.M2).
      final SyncReplayOutcome replay1 =
          await engine.replayMutation(created.clientGuid);
      final SyncReplayOutcome replay2 =
          await engine.replayMutation(created.clientGuid);

      expect(replay1.statusCode, equals(201));
      expect(replay1.recordId, equals(initialRecordId));
      expect(replay2.statusCode, equals(201));
      expect(replay2.recordId, equals(initialRecordId));
      expect(serverRows.length, equals(1));

      // 4. Reusing same Idempotency-Key with tampered payload triggers 409.
      final SyncReplayOutcome tampered = await engine.replayMutation(
        created.clientGuid,
        overridePayloadJson: '{"crop":"cassava","task":"tampered"}',
      );
      expect(tampered.statusCode, equals(409));
      expect(tampered.errorCode, equals('IDEMPOTENCY_KEY_REUSED'));
      expect(serverRows.length, equals(1));
    },
  );
}

class _StoredIdempotency {
  const _StoredIdempotency({
    required this.fingerprint,
    required this.statusCode,
    required this.responseBody,
  });

  final String fingerprint;
  final int statusCode;
  final String responseBody;
}
