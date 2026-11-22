import 'package:material_ui/material_ui.dart';

import '../main.dart';
import '../sync/offline_sync_engine.dart';
import '../sync/outbox_database.dart';
import 'boundary_walk_controller.dart';
import 'boundary_walk_models.dart';

/// DS2-styled offline field boundary walk capture screen (WP-53 / Doc 14 §3).
class BoundaryWalkScreen extends StatefulWidget {
  const BoundaryWalkScreen({
    super.key,
    required this.database,
    required this.syncEngine,
    this.sessionId = 'local-walk-session-1',
    this.controller,
    this.feedbackSignal,
    this.nowUtc,
  });

  final AppOutboxDatabase database;
  final OfflineSyncEngine syncEngine;
  final String sessionId;
  final BoundaryWalkController? controller;
  final WalkFeedbackSignal? feedbackSignal;
  final DateTime Function()? nowUtc;

  @override
  State<BoundaryWalkScreen> createState() => _BoundaryWalkScreenState();
}

class _BoundaryWalkScreenState extends State<BoundaryWalkScreen> {
  late final BoundaryWalkController _controller;
  late final bool _ownsController;
  String? _syncNotice;

  @override
  void initState() {
    super.initState();
    if (widget.controller != null) {
      _controller = widget.controller!;
      _ownsController = false;
    } else {
      _controller = BoundaryWalkController(
        database: widget.database,
        syncEngine: widget.syncEngine,
        sessionId: widget.sessionId,
        feedbackSignal: widget.feedbackSignal,
        nowUtc: widget.nowUtc,
      );
      _ownsController = true;
    }
    _controller.addListener(_onControllerUpdated);
    _controller.restoreSession();
  }

  @override
  void dispose() {
    _controller.removeListener(_onControllerUpdated);
    if (_ownsController) {
      _controller.dispose();
    }
    super.dispose();
  }

  void _onControllerUpdated() {
    if (mounted) {
      setState(() {});
    }
  }

  Future<void> _handleSync() async {
    final OutboxDrainResult result =
        await _controller.syncPendingPoints(respectBackoff: false);
    if (!mounted) {
      return;
    }
    setState(() {
      _syncNotice =
          'Synced ${result.syncedCount} point(s); ${result.retryScheduledCount} pending retry.';
    });
  }

  @override
  Widget build(BuildContext context) {
    final WalkGpsState state = _controller.state;
    final bool isPaused = state == WalkGpsState.paused;
    final bool isStopped = state == WalkGpsState.stopped;

    return Scaffold(
      backgroundColor: FarmBasicsApp.creamCanvas,
      appBar: AppBar(
        backgroundColor: FarmBasicsApp.forestPrimary,
        foregroundColor: Colors.white,
        title: const Text('Walk Field Boundary'),
      ),
      body: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: <Widget>[
            _buildGpsStatusBanner(state),
            const SizedBox(height: 16),
            if (_controller.closureCueTriggered) ...<Widget>[
              Container(
                key: const ValueKey<String>('walk-almost-complete-cue'),
                padding: const EdgeInsets.all(14),
                decoration: BoxDecoration(
                  color: const Color(0xFFFFF4D6),
                  border: Border.all(color: const Color(0xFFB45309), width: 2),
                  borderRadius: BorderRadius.circular(10),
                ),
                child: const Row(
                  children: <Widget>[
                    Icon(
                      Icons.notifications_active,
                      color: Color(0xFFB45309),
                      size: 28,
                    ),
                    SizedBox(width: 12),
                    Expanded(
                      child: Text(
                        'Almost complete — return to start pin (tone & vibration cue active)',
                        style: TextStyle(
                          color: FarmBasicsApp.ink900,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 16),
            ],
            Container(
              padding: const EdgeInsets.all(16),
              decoration: BoxDecoration(
                color: Colors.white,
                borderRadius: BorderRadius.circular(10),
                border: Border.all(color: const Color(0xFFD6D0C4)),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  Text(
                    'Accepted Points: ${_controller.acceptedPointCount}',
                    key: const ValueKey<String>('walk-points-count'),
                    style: const TextStyle(
                      color: FarmBasicsApp.ink900,
                      fontSize: 16,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 6),
                  Text(
                    'Distance Walked: ${_controller.distanceMeters.toStringAsFixed(1)} m',
                    key: const ValueKey<String>('walk-distance-meters'),
                    style: const TextStyle(color: FarmBasicsApp.ink900),
                  ),
                  const SizedBox(height: 6),
                  Text(
                    'Offline Pending Sync: ${_controller.pendingOutboxCount}',
                    key: const ValueKey<String>('walk-pending-count'),
                    style: const TextStyle(color: FarmBasicsApp.ink900),
                  ),
                ],
              ),
            ),
            if (_syncNotice != null) ...<Widget>[
              const SizedBox(height: 12),
              Text(
                _syncNotice!,
                key: const ValueKey<String>('walk-sync-notice'),
                style: const TextStyle(
                  color: FarmBasicsApp.forestPrimary,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ],
            const Spacer(),
            SizedBox(
              height: 52,
              child: FilledButton(
                key: const ValueKey<String>('walk-pause-resume-button'),
                style: FilledButton.styleFrom(
                  backgroundColor: FarmBasicsApp.forestPrimary,
                ),
                onPressed: isStopped
                    ? null
                    : () {
                        if (isPaused) {
                          _controller.resume();
                        } else {
                          _controller.pause();
                        }
                      },
                child: Text(isPaused ? 'Resume Walk' : 'Pause Walk'),
              ),
            ),
            const SizedBox(height: 12),
            SizedBox(
              height: 52,
              child: OutlinedButton(
                key: const ValueKey<String>('walk-stop-button'),
                onPressed: isStopped ? null : _controller.stopWalk,
                child: const Text('Stop & Review'),
              ),
            ),
            const SizedBox(height: 12),
            SizedBox(
              height: 52,
              child: OutlinedButton(
                key: const ValueKey<String>('walk-sync-button'),
                onPressed: _handleSync,
                child: const Text('Sync Pending Walk Points'),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildGpsStatusBanner(WalkGpsState state) {
    final String label = switch (state) {
      WalkGpsState.acquiring => 'GPS Acquiring Signal',
      WalkGpsState.active => 'GPS Active — Recording Walk',
      WalkGpsState.paused => 'Walk Paused',
      WalkGpsState.almostComplete => 'Almost Complete — Close Loop',
      WalkGpsState.stopped => 'Walk Stopped — Ready to Confirm',
    };

    final IconData icon = switch (state) {
      WalkGpsState.acquiring => Icons.gps_not_fixed,
      WalkGpsState.active => Icons.gps_fixed,
      WalkGpsState.paused => Icons.pause_circle_filled,
      WalkGpsState.almostComplete => Icons.vibration,
      WalkGpsState.stopped => Icons.check_circle,
    };

    return Container(
      key: const ValueKey<String>('walk-gps-state'),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: FarmBasicsApp.forestPrimary,
        borderRadius: BorderRadius.circular(10),
      ),
      child: Row(
        children: <Widget>[
          Icon(icon, color: Colors.white, size: 28),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              label,
              style: const TextStyle(
                color: Colors.white,
                fontSize: 16,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
