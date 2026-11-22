import 'dart:convert';
import 'dart:io';

import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:farm_basics_app/crop_cycles/crop_task_controller.dart';
import 'package:farm_basics_app/crop_cycles/crop_task_models.dart';
import 'package:farm_basics_app/network/certificate_pinning_adapter.dart';
import 'package:farm_basics_app/sync/offline_sync_engine.dart';
import 'package:farm_basics_app/sync/outbox_database.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  late HttpServer server;
  late String serverBaseUrl;
  final List<Map<String, Object?>> syncedPayloads = <Map<String, Object?>>[];

  setUp(() async {
    syncedPayloads.clear();
    server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    serverBaseUrl = 'http://${server.address.address}:${server.port}';
    server.listen((HttpRequest request) async {
      final String rawBody = await utf8.decoder.bind(request).join();
      final Map<String, dynamic> body =
          (jsonDecode(rawBody) as Map).cast<String, dynamic>();
      final Map<String, dynamic> inner =
          (jsonDecode(body['payloadJson'] as String) as Map)
              .cast<String, dynamic>();
      syncedPayloads.add(<String, Object?>{
        'path': request.uri.path,
        'clientGuid': body['clientGuid'],
        'clientRecordedAt': body['clientRecordedAt'],
        'innerLoggedAt': inner['loggedAt'],
        'entityType': body['entityType'],
      });

      request.response.statusCode = 201;
      request.response.headers.contentType = ContentType.json;
      request.response.write(
        jsonEncode(<String, Object?>{
          'data': <String, Object?>{
            'recordId': body['clientGuid'],
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
    'WP-73 Offline crop task logging preserves original 3-day-old physical task date on later sync',
    () async {
      final AppOutboxDatabase db = AppOutboxDatabase(NativeDatabase.memory());
      addTearDown(db.close);

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

      final DateTime syncDayClock = DateTime.utc(2026, 11, 22, 14, 0, 0);
      final OfflineSyncEngine syncEngine = OfflineSyncEngine(
        database: db,
        dio: dio,
        nowUtc: () => syncDayClock,
      );

      final CropTaskController controller = CropTaskController(
        database: db,
        syncEngine: syncEngine,
        cycleId: 'cycle-offline-77',
        cropName: 'Maize',
        initialTasks: <CropTaskItem>[
          CropTaskItem(
            taskDefinitionId: 'PRE-06',
            stage: 'pre_planting',
            name: 'Soil sampling & pH test',
            required: true,
            unitLabel: 'pH',
            minValue: 5.5,
            maxValue: 7.0,
          ),
        ],
        nowUtc: () => syncDayClock,
      );
      addTearDown(controller.dispose);

      // Farmer records physical task offline on 2026-11-19.
      final DateTime realTaskDate = DateTime.utc(2026, 11, 19, 6, 30, 0);
      final CropTaskItem logged = await controller.logTask(
        taskDefinitionId: 'PRE-06',
        numericValue: 6.1,
        taskDateUtc: realTaskDate,
      );
      expect(logged.status, equals(CropTaskValidationStatus.ok));

      // Offline drain keeps row queued.
      final OutboxDrainResult offline = await controller.syncPendingTasks();
      expect(offline.retryScheduledCount, equals(1));
      expect(syncedPayloads, isEmpty);

      // Reconnect 3 days later -> server receives original 2026-11-19 timestamp untouched.
      dio.options.baseUrl = serverBaseUrl;
      final OutboxDrainResult online =
          await controller.syncPendingTasks(respectBackoff: false);
      expect(online.syncedCount, equals(1));
      expect(syncedPayloads.length, equals(1));
      expect(
        DateTime.parse(syncedPayloads.single['clientRecordedAt']! as String)
            .toUtc(),
        equals(realTaskDate),
      );
      expect(
        DateTime.parse(syncedPayloads.single['innerLoggedAt']! as String)
            .toUtc(),
        equals(realTaskDate),
      );
    },
  );
}
