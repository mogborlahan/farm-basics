import 'dart:convert';
import 'dart:io';

import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:farm_basics_app/boundary/boundary_walk_controller.dart';
import 'package:farm_basics_app/network/certificate_pinning_adapter.dart';
import 'package:farm_basics_app/sync/offline_sync_engine.dart';
import 'package:farm_basics_app/sync/outbox_database.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  late HttpServer server;
  late String serverBaseUrl;
  final List<Map<String, Object?>> receivedPoints = <Map<String, Object?>>[];
  final Set<String> seenGuids = <String>{};

  setUp(() async {
    receivedPoints.clear();
    seenGuids.clear();

    server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    serverBaseUrl = 'http://${server.address.address}:${server.port}';
    server.listen((HttpRequest request) async {
      final String rawBody = await utf8.decoder.bind(request).join();
      final Map<String, dynamic> body =
          (jsonDecode(rawBody) as Map).cast<String, dynamic>();
      final String clientGuid = body['clientGuid'] as String;

      if (!seenGuids.add(clientGuid)) {
        request.response.statusCode = 409;
        request.response.headers.contentType = ContentType.json;
        request.response.write(
          jsonEncode(<String, Object?>{
            'error': <String, String>{
              'code': 'CLIENT_GUID_ALREADY_SYNCED',
              'message': 'Duplicate clientGuid rejected as replay.',
            },
          }),
        );
        await request.response.close();
        return;
      }

      final Map<String, dynamic> payload =
          (jsonDecode(body['payloadJson'] as String) as Map)
              .cast<String, dynamic>();
      receivedPoints.add(<String, Object?>{
        'clientGuid': clientGuid,
        'sequenceNumber': payload['sequenceNumber'],
        'lat': payload['lat'],
        'lng': payload['lng'],
        'clientRecordedAt': body['clientRecordedAt'],
        'payloadRecordedAt': payload['recordedAt'],
      });

      request.response.statusCode = 202;
      request.response.headers.contentType = ContentType.json;
      request.response.write(
        jsonEncode(<String, Object?>{
          'data': <String, Object?>{
            'recordId': clientGuid,
            'accepted': 1,
            'serverReceivedAt': DateTime.now().toUtc().toIso8601String(),
          },
        }),
      );
      await request.response.close();
    });
  });

  tearDown(() async {
    await server.close(force: true);
  });

  test(
    'WP-55 Offline walk suite: 4-day offline sync preserves original timestamps, mid-walk resume persists via Drift, and duplicate GUID is rejected as replay',
    () async {
      final AppOutboxDatabase db = AppOutboxDatabase(NativeDatabase.memory());
      addTearDown(db.close);

      // Offline at day 0: Dio points at unreachable port.
      final Dio dio = Dio(
        BaseOptions(
          baseUrl: 'http://127.0.0.1:1',
          connectTimeout: const Duration(milliseconds: 250),
          receiveTimeout: const Duration(milliseconds: 250),
        ),
      )..httpClientAdapter = createPinnedHttpClientAdapter(
          allowedSha256Pins: const <String>{'pinned-test-digest'},
          allowInsecureLocalhost: true,
        );

      DateTime simulatedClock = DateTime.utc(2026, 11, 18, 7, 15, 0);
      final OfflineSyncEngine engine1 = OfflineSyncEngine(
        database: db,
        dio: dio,
        nowUtc: () => simulatedClock,
      );

      final BoundaryWalkController walkPart1 = BoundaryWalkController(
        database: db,
        syncEngine: engine1,
        sessionId: 'walk-offline-session-99',
        nowUtc: () => simulatedClock,
      );

      // Record first two corners offline on 2026-11-18.
      final DateTime t1 = DateTime.utc(2026, 11, 18, 7, 15, 0);
      final DateTime t2 = DateTime.utc(2026, 11, 18, 7, 18, 30);
      expect(
        await walkPart1.recordGpsFix(
          lat: 7.3800,
          lng: 3.8900,
          accuracyMeters: 4.0,
          recordedAtOverride: t1,
        ),
        isTrue,
      );
      expect(
        await walkPart1.recordGpsFix(
          lat: 7.3800,
          lng: 3.8912,
          accuracyMeters: 4.1,
          recordedAtOverride: t2,
        ),
        isTrue,
      );

      // Attempted drain offline leaves both points queued for retry.
      final OutboxDrainResult offlineAttempt =
          await walkPart1.syncPendingPoints();
      expect(offlineAttempt.syncedCount, equals(0));
      expect(offlineAttempt.retryScheduledCount, equals(2));
      expect(receivedPoints, isEmpty);
      walkPart1.dispose();

      // Simulate connectivity loss + app restart later; new controller resumes same session from Drift.
      simulatedClock = DateTime.utc(2026, 11, 22, 9, 45, 0);
      final OfflineSyncEngine engine2 = OfflineSyncEngine(
        database: db,
        dio: dio,
        nowUtc: () => simulatedClock,
      );
      final BoundaryWalkController walkResumed = BoundaryWalkController(
        database: db,
        syncEngine: engine2,
        sessionId: 'walk-offline-session-99',
        nowUtc: () => simulatedClock,
      );
      addTearDown(walkResumed.dispose);

      await walkResumed.restoreSession();
      expect(walkResumed.acceptedPointCount, equals(2));
      expect(walkResumed.pendingOutboxCount, equals(2));

      final DateTime t3 = DateTime.utc(2026, 11, 22, 9, 45, 0);
      final DateTime t4 = DateTime.utc(2026, 11, 22, 9, 48, 0);
      await walkResumed.recordGpsFix(
        lat: 7.3812,
        lng: 3.8912,
        accuracyMeters: 3.9,
        recordedAtOverride: t3,
      );
      await walkResumed.recordGpsFix(
        lat: 7.3812,
        lng: 3.8900,
        accuracyMeters: 3.7,
        recordedAtOverride: t4,
      );
      expect(walkResumed.acceptedPointCount, equals(4));

      // Reconnect 4 days later -> all 4 points sync in strict sequence preserving 2026-11-18 timestamps.
      dio.options.baseUrl = serverBaseUrl;
      final OutboxDrainResult synced =
          await walkResumed.syncPendingPoints(respectBackoff: false);
      expect(synced.syncedCount, equals(4));
      expect(synced.retryScheduledCount, equals(0));
      expect(synced.conflictCount, equals(0));
      expect(walkResumed.pendingOutboxCount, equals(0));
      expect(receivedPoints.length, equals(4));
      expect(
        receivedPoints.map((Map<String, Object?> r) => r['sequenceNumber']).toList(),
        equals(<int>[1, 2, 3, 4]),
      );
      expect(
        DateTime.parse(receivedPoints[0]['clientRecordedAt']! as String).toUtc(),
        equals(t1),
      );
      expect(
        DateTime.parse(receivedPoints[1]['clientRecordedAt']! as String).toUtc(),
        equals(t2),
      );
      expect(
        DateTime.parse(receivedPoints[2]['clientRecordedAt']! as String).toUtc(),
        equals(t3),
      );
      expect(
        DateTime.parse(receivedPoints[3]['clientRecordedAt']! as String).toUtc(),
        equals(t4),
      );

      // Replaying an already-synced clientGuid is rejected by the endpoint as a duplicate replay.
      final String firstGuid = walkResumed.points.first.clientGuid;
      final SyncReplayOutcome duplicateReplay =
          await engine2.replayMutation(firstGuid);
      expect(duplicateReplay.statusCode, equals(409));
      expect(duplicateReplay.errorCode, equals('CLIENT_GUID_ALREADY_SYNCED'));
      expect(receivedPoints.length, equals(4));
    },
  );
}
