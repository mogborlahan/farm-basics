import 'package:material_ui/material_ui.dart';

import '../main.dart';
import '../sync/offline_sync_engine.dart';
import '../sync/outbox_database.dart';
import 'crop_task_controller.dart';
import 'crop_task_models.dart';

/// DS2 crop-cycle task list with OK/Warning/Flag badges and plain-language override UX (WP-73 / Vol A §15.2).
class CropTaskListScreen extends StatefulWidget {
  const CropTaskListScreen({
    super.key,
    required this.database,
    required this.syncEngine,
    this.controller,
  });

  final AppOutboxDatabase database;
  final OfflineSyncEngine syncEngine;
  final CropTaskController? controller;

  @override
  State<CropTaskListScreen> createState() => _CropTaskListScreenState();
}

class _CropTaskListScreenState extends State<CropTaskListScreen> {
  late final CropTaskController _controller;
  late final bool _ownsController;
  final Map<String, TextEditingController> _valueInputs =
      <String, TextEditingController>{};
  final Map<String, TextEditingController> _overrideInputs =
      <String, TextEditingController>{};

  @override
  void initState() {
    super.initState();
    if (widget.controller != null) {
      _controller = widget.controller!;
      _ownsController = false;
    } else {
      _controller = CropTaskController(
        database: widget.database,
        syncEngine: widget.syncEngine,
        cycleId: 'local-cycle-1',
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
        ],
      );
      _ownsController = true;
    }
    _controller.addListener(_onUpdated);
  }

  @override
  void dispose() {
    _controller.removeListener(_onUpdated);
    for (final TextEditingController c in _valueInputs.values) {
      c.dispose();
    }
    for (final TextEditingController c in _overrideInputs.values) {
      c.dispose();
    }
    if (_ownsController) {
      _controller.dispose();
    }
    super.dispose();
  }

  void _onUpdated() {
    if (mounted) {
      setState(() {});
    }
  }

  TextEditingController _valueControllerFor(String taskId) {
    return _valueInputs.putIfAbsent(taskId, TextEditingController.new);
  }

  TextEditingController _overrideControllerFor(String taskId) {
    return _overrideInputs.putIfAbsent(taskId, TextEditingController.new);
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: FarmBasicsApp.creamCanvas,
      appBar: AppBar(
        backgroundColor: FarmBasicsApp.forestPrimary,
        foregroundColor: Colors.white,
        title: Text('${_controller.cropName} Task List'),
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: <Widget>[
          for (final CropTaskItem task in _controller.tasks) ...<Widget>[
            _buildTaskCard(task),
            const SizedBox(height: 12),
          ],
          SizedBox(
            height: 52,
            child: OutlinedButton(
              key: const ValueKey<String>('crop-tasks-sync-button'),
              onPressed: () => _controller.syncPendingTasks(respectBackoff: false),
              child: const Text('Sync Pending Task Logs'),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildTaskCard(CropTaskItem task) {
    final String statusLabel = switch (task.status) {
      CropTaskValidationStatus.pending => 'Pending',
      CropTaskValidationStatus.ok => 'OK',
      CropTaskValidationStatus.warning => 'Warning (Advisory)',
      CropTaskValidationStatus.flag =>
        task.isOverridden ? 'Flag (Overridden)' : 'Flag — Attention Required',
    };

    final Color badgeColor = switch (task.status) {
      CropTaskValidationStatus.pending => const Color(0xFF525252),
      CropTaskValidationStatus.ok => FarmBasicsApp.forestPrimary,
      CropTaskValidationStatus.warning => const Color(0xFFB45309),
      CropTaskValidationStatus.flag => const Color(0xFFB91C1C),
    };

    return Container(
      key: ValueKey<String>('task-card-${task.taskDefinitionId}'),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: const Color(0xFFD6D0C4)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Row(
            children: <Widget>[
              Expanded(
                child: Text(
                  '${task.taskDefinitionId}: ${task.name}',
                  style: const TextStyle(
                    color: FarmBasicsApp.ink900,
                    fontWeight: FontWeight.w700,
                    fontSize: 15,
                  ),
                ),
              ),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                decoration: BoxDecoration(
                  color: badgeColor.withAlpha(24),
                  borderRadius: BorderRadius.circular(6),
                ),
                child: Text(
                  task.required ? 'Required' : 'Optional',
                  style: TextStyle(color: badgeColor, fontSize: 12),
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            statusLabel,
            key: ValueKey<String>('task-status-${task.taskDefinitionId}'),
            style: TextStyle(color: badgeColor, fontWeight: FontWeight.w600),
          ),
          if (task.validationMessage != null) ...<Widget>[
            const SizedBox(height: 6),
            Text(
              task.validationMessage!,
              key: ValueKey<String>('task-message-${task.taskDefinitionId}'),
              style: const TextStyle(color: FarmBasicsApp.ink900),
            ),
          ],
          if (task.isOverridden) ...<Widget>[
            const SizedBox(height: 6),
            Text(
              'Override reason kept on record: ${task.overrideReason}',
              key: ValueKey<String>('task-override-note-${task.taskDefinitionId}'),
              style: const TextStyle(
                color: FarmBasicsApp.forestPrimary,
                fontWeight: FontWeight.w600,
              ),
            ),
          ],
          const SizedBox(height: 10),
          if (task.minValue != null) ...<Widget>[
            TextField(
              key: ValueKey<String>('task-input-${task.taskDefinitionId}'),
              controller: _valueControllerFor(task.taskDefinitionId),
              keyboardType:
                  const TextInputType.numberWithOptions(decimal: true),
              decoration: InputDecoration(
                labelText: 'Enter ${task.unitLabel ?? 'value'}',
                border: const OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 8),
          ],
          SizedBox(
            height: 48,
            child: FilledButton(
              key: ValueKey<String>('task-log-btn-${task.taskDefinitionId}'),
              style: FilledButton.styleFrom(
                backgroundColor: FarmBasicsApp.forestPrimary,
              ),
              onPressed: () async {
                final String raw =
                    _valueControllerFor(task.taskDefinitionId).text.trim();
                final double? parsed =
                    raw.isEmpty ? null : double.tryParse(raw);
                await _controller.logTask(
                  taskDefinitionId: task.taskDefinitionId,
                  numericValue: parsed,
                );
              },
              child: const Text('Log Task'),
            ),
          ),
          if (task.status == CropTaskValidationStatus.flag &&
              !task.isOverridden) ...<Widget>[
            const SizedBox(height: 10),
            TextField(
              key: ValueKey<String>(
                  'task-override-input-${task.taskDefinitionId}'),
              controller: _overrideControllerFor(task.taskDefinitionId),
              decoration: const InputDecoration(
                labelText: 'Why are you continuing despite this warning?',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 8),
            SizedBox(
              height: 48,
              child: OutlinedButton(
                key: ValueKey<String>(
                    'task-override-btn-${task.taskDefinitionId}'),
                onPressed: () async {
                  await _controller.overrideFlag(
                    taskDefinitionId: task.taskDefinitionId,
                    reason:
                        _overrideControllerFor(task.taskDefinitionId).text,
                  );
                },
                child: const Text('Record Override Reason'),
              ),
            ),
          ],
        ],
      ),
    );
  }
}
