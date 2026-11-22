import 'dart:convert';

import 'package:flutter/foundation.dart';

import '../sync/offline_sync_engine.dart';
import '../sync/outbox_database.dart';
import 'crop_task_models.dart';

/// Offline-first controller for CropCycle tasks, validation feedback, and immutable Flag overrides (WP-73).
class CropTaskController extends ChangeNotifier {
  CropTaskController({
    required this.database,
    required this.syncEngine,
    required this.cycleId,
    required this.cropName,
    required List<CropTaskItem> initialTasks,
    DateTime Function()? nowUtc,
  })  : _tasks = List<CropTaskItem>.from(initialTasks),
        _nowUtc = nowUtc ?? (() => DateTime.now().toUtc());

  final AppOutboxDatabase database;
  final OfflineSyncEngine syncEngine;
  final String cycleId;
  final String cropName;
  final List<CropTaskItem> _tasks;
  final DateTime Function() _nowUtc;

  List<CropTaskItem> get tasks => List<CropTaskItem>.unmodifiable(_tasks);

  /// Records a task locally into Drift outbox preserving the exact real task date [taskDateUtc].
  Future<CropTaskItem> logTask({
    required String taskDefinitionId,
    double? numericValue,
    DateTime? taskDateUtc,
  }) async {
    final CropTaskItem task = _tasks.firstWhere(
      (CropTaskItem t) => t.taskDefinitionId == taskDefinitionId,
    );

    final DateTime recordedAt = (taskDateUtc ?? _nowUtc()).toUtc();
    CropTaskValidationStatus nextStatus = CropTaskValidationStatus.ok;
    String nextMessage = 'Task completed on ${recordedAt.toIso8601String().split('T').first}.';

    if (numericValue != null && task.minValue != null && task.maxValue != null) {
      final ({CropTaskValidationStatus status, String message}) eval =
          validateNumericTaskValue(
        value: numericValue,
        minValue: task.minValue!,
        maxValue: task.maxValue!,
        unitLabel: task.unitLabel ?? '',
        cropName: cropName,
      );
      nextStatus = eval.status;
      nextMessage = eval.message;
    }

    task.status = nextStatus;
    task.validationMessage = nextMessage;
    task.recordedAt = recordedAt;
    task.lastNumericValue = numericValue;
    task.overrideReason = null;

    await syncEngine.enqueueOfflineRecord(
      entityType: 'operation_log',
      entityLocalId: '${cycleId}_$taskDefinitionId',
      parentEntityLocalId: cycleId,
      endpointPath: '/v1/crop-cycles/$cycleId/tasks/$taskDefinitionId/log',
      payloadJson: jsonEncode(<String, Object?>{
        'taskDefinitionId': taskDefinitionId,
        'status': nextStatus.name,
        'numericValue': ?numericValue,
        'loggedAt': recordedAt.toIso8601String(),
      }),
      clientRecordedAtOverride: recordedAt,
    );

    notifyListeners();
    return task;
  }

  /// Appends an override reason without erasing the original Flag status (BR-A-09 / S7.4.M2).
  Future<bool> overrideFlag({
    required String taskDefinitionId,
    required String reason,
  }) async {
    final String trimmed = reason.trim();
    if (trimmed.isEmpty) {
      return false;
    }

    final CropTaskItem task = _tasks.firstWhere(
      (CropTaskItem t) => t.taskDefinitionId == taskDefinitionId,
    );
    if (task.status != CropTaskValidationStatus.flag) {
      return false;
    }

    // Keep task.status == CropTaskValidationStatus.flag untouched; only attach overrideReason.
    task.overrideReason = trimmed;

    await syncEngine.enqueueOfflineRecord(
      entityType: 'operation_log_override',
      entityLocalId: '${cycleId}_${taskDefinitionId}_override',
      parentEntityLocalId: cycleId,
      endpointPath: '/v1/operation-logs/$taskDefinitionId/override',
      payloadJson: jsonEncode(<String, Object?>{
        'taskDefinitionId': taskDefinitionId,
        'originalStatus': 'flag',
        'reason': trimmed,
      }),
      clientRecordedAtOverride: _nowUtc(),
    );

    notifyListeners();
    return true;
  }

  Future<OutboxDrainResult> syncPendingTasks({bool respectBackoff = true}) {
    return syncEngine.drainOutbox(respectBackoff: respectBackoff);
  }
}
