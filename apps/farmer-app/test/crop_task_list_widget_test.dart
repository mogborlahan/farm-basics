import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:farm_basics_app/crop_cycles/crop_task_controller.dart';
import 'package:farm_basics_app/crop_cycles/crop_task_list_screen.dart';
import 'package:farm_basics_app/crop_cycles/crop_task_models.dart';
import 'package:farm_basics_app/sync/offline_sync_engine.dart';
import 'package:farm_basics_app/sync/outbox_database.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:material_ui/material_ui.dart';

void main() {
  testWidgets(
    'WP-73 Crop task list renders OK/Warning/Flag validation, plain-language explanation, and keeps original Flag visible on override',
    (WidgetTester tester) async {
      final AppOutboxDatabase db = AppOutboxDatabase(NativeDatabase.memory());
      addTearDown(db.close);

      final Dio dio = Dio(BaseOptions(baseUrl: 'http://127.0.0.1:1'));
      final OfflineSyncEngine syncEngine = OfflineSyncEngine(
        database: db,
        dio: dio,
      );

      final CropTaskController controller = CropTaskController(
        database: db,
        syncEngine: syncEngine,
        cycleId: 'cycle-widget-1',
        cropName: 'Maize',
        initialTasks: <CropTaskItem>[
          CropTaskItem(
            taskDefinitionId: 'PRE-01',
            stage: 'pre_planting',
            name: 'Land clearing & bed preparation',
            required: true,
          ),
          CropTaskItem(
            taskDefinitionId: 'PRE-06',
            stage: 'pre_planting',
            name: 'Soil sampling & pH test',
            required: true,
            unitLabel: 'pH',
            minValue: 5.5,
            maxValue: 7.0,
          ),
          CropTaskItem(
            taskDefinitionId: 'PLT-01',
            stage: 'planting',
            name: 'Seed sowing & depth check',
            required: false,
            unitLabel: 'cm',
            minValue: 3.0,
            maxValue: 5.0,
          ),
        ],
        nowUtc: () => DateTime.utc(2026, 11, 22, 12, 0, 0),
      );
      addTearDown(controller.dispose);

      await tester.pumpWidget(
        MaterialApp(
          home: CropTaskListScreen(
            database: db,
            syncEngine: syncEngine,
            controller: controller,
          ),
        ),
      );
      await tester.pumpAndSettle();

      // Capability-disabled tasks (CC-06) are absent completely.
      expect(find.textContaining('CC-06'), findsNothing);

      // 1. Complete required non-numeric task -> OK.
      final Finder pre01Btn = find.byKey(
        const ValueKey<String>('task-log-btn-PRE-01'),
      );
      await tester.ensureVisible(pre01Btn);
      await tester.tap(pre01Btn);
      await tester.pumpAndSettle();
      expect(
        find.descendant(
          of: find.byKey(const ValueKey<String>('task-card-PRE-01')),
          matching: find.text('OK'),
        ),
        findsOneWidget,
      );

      // 2. Log slightly out-of-range sowing depth (5.3 cm vs max 5.0 -> +-10% band) -> Warning.
      final Finder plt01Input = find.byKey(
        const ValueKey<String>('task-input-PLT-01'),
      );
      await tester.ensureVisible(plt01Input);
      await tester.enterText(plt01Input, '5.3');
      final Finder plt01Btn = find.byKey(
        const ValueKey<String>('task-log-btn-PLT-01'),
      );
      await tester.ensureVisible(plt01Btn);
      await tester.tap(plt01Btn);
      await tester.pumpAndSettle();
      expect(find.text('Warning (Advisory)'), findsOneWidget);

      // 3. Log severe soil pH flag (3.8 vs 5.5..7.0) -> Flag + plain-language explanation + override UX.
      final Finder pre06Input = find.byKey(
        const ValueKey<String>('task-input-PRE-06'),
      );
      await tester.ensureVisible(pre06Input);
      await tester.enterText(pre06Input, '3.8');
      final Finder pre06Btn = find.byKey(
        const ValueKey<String>('task-log-btn-PRE-06'),
      );
      await tester.ensureVisible(pre06Btn);
      await tester.tap(pre06Btn);
      await tester.pumpAndSettle();
      expect(find.text('Flag — Attention Required'), findsOneWidget);
      expect(find.textContaining('Double-check the reading'), findsOneWidget);

      // 4. Record override reason -> unblocks while keeping original Flag status permanently visible.
      final Finder ovInput = find.byKey(
        const ValueKey<String>('task-override-input-PRE-06'),
      );
      await tester.ensureVisible(ovInput);
      await tester.enterText(ovInput, 'Dolomitic lime applied across plot');
      final Finder ovBtn = find.byKey(
        const ValueKey<String>('task-override-btn-PRE-06'),
      );
      await tester.ensureVisible(ovBtn);
      await tester.tap(ovBtn);
      await tester.pumpAndSettle();

      expect(find.text('Flag (Overridden)'), findsOneWidget);
      expect(
        find.textContaining('Dolomitic lime applied across plot'),
        findsOneWidget,
      );
      expect(controller.tasks[1].status, equals(CropTaskValidationStatus.flag));
    },
  );
}
