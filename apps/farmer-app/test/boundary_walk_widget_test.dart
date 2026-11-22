import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:farm_basics_app/boundary/boundary_walk_controller.dart';
import 'package:farm_basics_app/boundary/boundary_walk_models.dart';
import 'package:farm_basics_app/boundary/boundary_walk_screen.dart';
import 'package:farm_basics_app/sync/offline_sync_engine.dart';
import 'package:farm_basics_app/sync/outbox_database.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:material_ui/material_ui.dart';

class _RecordingFeedbackSignal implements WalkFeedbackSignal {
  int almostCompleteCount = 0;

  @override
  Future<void> onAlmostComplete() async {
    almostCompleteCount++;
  }
}

void main() {
  testWidgets(
    'WP-53 Walk UI transitions acquiring -> active -> paused -> active -> almostComplete with non-text cue',
    (WidgetTester tester) async {
      final AppOutboxDatabase db = AppOutboxDatabase(NativeDatabase.memory());
      addTearDown(db.close);

      final Dio dio = Dio(BaseOptions(baseUrl: 'http://127.0.0.1:1'));
      final OfflineSyncEngine syncEngine = OfflineSyncEngine(
        database: db,
        dio: dio,
      );
      final _RecordingFeedbackSignal feedback = _RecordingFeedbackSignal();
      final BoundaryWalkController controller = BoundaryWalkController(
        database: db,
        syncEngine: syncEngine,
        sessionId: 'walk-ui-session-1',
        feedbackSignal: feedback,
        nowUtc: () => DateTime.utc(2026, 11, 22, 10, 0, 0),
      );
      addTearDown(controller.dispose);

      await tester.pumpWidget(
        MaterialApp(
          home: BoundaryWalkScreen(
            database: db,
            syncEngine: syncEngine,
            sessionId: 'walk-ui-session-1',
            controller: controller,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('GPS Acquiring Signal'), findsOneWidget);

      // First low-accuracy fix stays in acquiring state and does not queue.
      await controller.recordGpsFix(lat: 7.4, lng: 3.9, accuracyMeters: 35.0);
      await tester.pump();
      expect(find.text('GPS Acquiring Signal'), findsOneWidget);
      expect(find.text('Accepted Points: 0'), findsOneWidget);

      // Valid GPS fixes move to active state.
      await controller.recordGpsFix(lat: 7.4000, lng: 3.9000, accuracyMeters: 4.5);
      await controller.recordGpsFix(lat: 7.4000, lng: 3.9010, accuracyMeters: 4.2);
      await tester.pump();
      expect(find.text('GPS Active — Recording Walk'), findsOneWidget);
      expect(find.text('Accepted Points: 2'), findsOneWidget);

      // Pause ignores incoming GPS fixes until resumed.
      await tester.tap(find.byKey(const ValueKey<String>('walk-pause-resume-button')));
      await tester.pump();
      expect(find.text('Walk Paused'), findsOneWidget);
      expect(find.text('Resume Walk'), findsOneWidget);

      final bool acceptedWhilePaused = await controller.recordGpsFix(
        lat: 7.4010,
        lng: 3.9010,
        accuracyMeters: 4.0,
      );
      expect(acceptedWhilePaused, isFalse);

      // Resume and walk back near start pin -> triggers non-text almostComplete cue once.
      await tester.tap(find.byKey(const ValueKey<String>('walk-pause-resume-button')));
      await tester.pump();
      expect(find.text('GPS Active — Recording Walk'), findsOneWidget);

      await controller.recordGpsFix(lat: 7.4010, lng: 3.9010, accuracyMeters: 3.8);
      await controller.recordGpsFix(lat: 7.40004, lng: 3.90004, accuracyMeters: 3.5);
      await tester.pump();

      expect(controller.state, equals(WalkGpsState.almostComplete));
      expect(feedback.almostCompleteCount, equals(1));
      expect(
        find.byKey(const ValueKey<String>('walk-almost-complete-cue')),
        findsOneWidget,
      );
    },
  );
}
