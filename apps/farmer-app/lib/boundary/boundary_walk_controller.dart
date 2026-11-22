import 'dart:convert';

import 'package:flutter/foundation.dart';

import '../sync/offline_sync_engine.dart';
import '../sync/outbox_database.dart';
import 'boundary_walk_models.dart';

/// Offline-first controller for walked boundary GPS capture & Drift outbox queuing (WP-53 / WP-55).
class BoundaryWalkController extends ChangeNotifier {
  BoundaryWalkController({
    required this.database,
    required this.syncEngine,
    required this.sessionId,
    WalkFeedbackSignal? feedbackSignal,
    DateTime Function()? nowUtc,
    String? endpointPath,
  })  : _feedback = feedbackSignal ?? const PlatformWalkFeedbackSignal(),
        _nowUtc = nowUtc ?? (() => DateTime.now().toUtc()),
        endpointPath = endpointPath ??
            '/v1/production-units/boundary-sessions/$sessionId/points/batch';

  static const double maxAcceptedAccuracyMeters = 15.0;
  static const double closureCueRadiusMeters = 10.0;

  final AppOutboxDatabase database;
  final OfflineSyncEngine syncEngine;
  final String sessionId;
  final String endpointPath;
  final WalkFeedbackSignal _feedback;
  final DateTime Function() _nowUtc;

  final List<WalkPoint> _points = <WalkPoint>[];
  WalkGpsState _state = WalkGpsState.acquiring;
  bool _closureCueTriggered = false;
  double _distanceMeters = 0.0;
  int _rejectedLowAccuracyCount = 0;

  WalkGpsState get state => _state;
  List<WalkPoint> get points => List<WalkPoint>.unmodifiable(_points);
  int get acceptedPointCount => _points.length;
  int get pendingOutboxCount =>
      _points.where((WalkPoint p) => p.syncStatus == 'pending').length;
  double get distanceMeters => _distanceMeters;
  int get rejectedLowAccuracyCount => _rejectedLowAccuracyCount;
  bool get closureCueTriggered => _closureCueTriggered;

  /// Restores an in-progress walked boundary session from local Drift storage after reconnect/restart.
  Future<void> restoreSession() async {
    final List<LocalOutboxMutation> persisted =
        await database.getMutationsForParent(
      entityType: 'tracking_point',
      parentEntityLocalId: sessionId,
    );

    _points.clear();
    for (final LocalOutboxMutation row in persisted) {
      final Map<String, dynamic> decoded =
          (jsonDecode(row.payloadJson) as Map).cast<String, dynamic>();
      _points.add(
        WalkPoint.fromJson(
          decoded,
          clientGuid: row.clientGuid,
          syncStatus: row.syncStatus,
        ),
      );
    }

    _recomputeDistanceAndClosureState(triggerFeedback: false);
    if (_points.isNotEmpty && _state == WalkGpsState.acquiring) {
      _state = WalkGpsState.active;
    }
    notifyListeners();
  }

  /// Processes an incoming GPS coordinate fix and persists accepted points to Drift immediately.
  Future<bool> recordGpsFix({
    required double lat,
    required double lng,
    required double accuracyMeters,
    DateTime? recordedAtOverride,
  }) async {
    if (_state == WalkGpsState.paused || _state == WalkGpsState.stopped) {
      return false;
    }

    if (accuracyMeters <= 0 || accuracyMeters > maxAcceptedAccuracyMeters) {
      _rejectedLowAccuracyCount++;
      notifyListeners();
      return false;
    }

    if (_state == WalkGpsState.acquiring) {
      _state = WalkGpsState.active;
    }

    final int nextSeq = _points.isEmpty
        ? 1
        : (_points.map((WalkPoint p) => p.sequenceNumber).reduce(
                (int a, int b) => a > b ? a : b,
              ) +
            1);

    final DateTime recordedAt = (recordedAtOverride ?? _nowUtc()).toUtc();
    final WalkPoint candidate = WalkPoint(
      clientGuid: '',
      sequenceNumber: nextSeq,
      lat: lat,
      lng: lng,
      accuracyMeters: accuracyMeters,
      recordedAt: recordedAt,
      syncStatus: 'pending',
    );

    final LocalOutboxMutation stored = await syncEngine.enqueueOfflineRecord(
      entityType: 'tracking_point',
      entityLocalId: '${sessionId}_$nextSeq',
      parentEntityLocalId: sessionId,
      endpointPath: endpointPath,
      payloadJson: jsonEncode(candidate.toJson()),
      clientRecordedAtOverride: recordedAt,
    );

    _points.add(
      WalkPoint(
        clientGuid: stored.clientGuid,
        sequenceNumber: nextSeq,
        lat: lat,
        lng: lng,
        accuracyMeters: accuracyMeters,
        recordedAt: stored.clientRecordedAt.toUtc(),
        syncStatus: stored.syncStatus,
      ),
    );

    await _recomputeDistanceAndClosureStateAsync(triggerFeedback: true);
    notifyListeners();
    return true;
  }

  void pause() {
    if (_state == WalkGpsState.stopped) {
      return;
    }
    _state = WalkGpsState.paused;
    notifyListeners();
  }

  void resume() {
    if (_state != WalkGpsState.paused) {
      return;
    }
    _state = _closureCueTriggered ? WalkGpsState.almostComplete : WalkGpsState.active;
    notifyListeners();
  }

  void stopWalk() {
    _state = WalkGpsState.stopped;
    notifyListeners();
  }

  /// Drains queued offline walk points via OfflineSyncEngine when connectivity returns.
  Future<OutboxDrainResult> syncPendingPoints({bool respectBackoff = true}) async {
    final OutboxDrainResult result = await syncEngine.drainOutbox(
      respectBackoff: respectBackoff,
    );
    await restoreSession();
    return result;
  }

  Future<void> _recomputeDistanceAndClosureStateAsync({
    required bool triggerFeedback,
  }) async {
    final bool shouldTrigger =
        _recomputeDistanceAndClosureState(triggerFeedback: triggerFeedback);
    if (shouldTrigger) {
      await _feedback.onAlmostComplete();
    }
  }

  bool _recomputeDistanceAndClosureState({required bool triggerFeedback}) {
    double dist = 0.0;
    for (int i = 1; i < _points.length; i++) {
      dist += haversineDistanceMeters(
        _points[i - 1].lat,
        _points[i - 1].lng,
        _points[i].lat,
        _points[i].lng,
      );
    }
    _distanceMeters = dist;

    if (_points.length >= 3 && _state != WalkGpsState.paused && _state != WalkGpsState.stopped) {
      final WalkPoint first = _points.first;
      final WalkPoint last = _points.last;
      final double distanceToStart = haversineDistanceMeters(
        last.lat,
        last.lng,
        first.lat,
        first.lng,
      );
      if (distanceToStart <= closureCueRadiusMeters) {
        final bool firstTrigger = !_closureCueTriggered;
        _closureCueTriggered = true;
        _state = WalkGpsState.almostComplete;
        return triggerFeedback && firstTrigger;
      }
    }

    return false;
  }
}
