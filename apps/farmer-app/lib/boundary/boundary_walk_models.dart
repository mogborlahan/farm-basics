import 'dart:math';

import 'package:flutter/services.dart';

/// GPS & lifecycle state machine for walked field boundary capture (WP-53 / Doc 14 §3).
enum WalkGpsState {
  acquiring,
  active,
  paused,
  almostComplete,
  stopped,
}

/// Recorded vertex from device location updates or resumed local Drift storage.
class WalkPoint {
  const WalkPoint({
    required this.clientGuid,
    required this.sequenceNumber,
    required this.lat,
    required this.lng,
    required this.accuracyMeters,
    required this.recordedAt,
    this.syncStatus = 'pending',
  });

  final String clientGuid;
  final int sequenceNumber;
  final double lat;
  final double lng;
  final double accuracyMeters;
  final DateTime recordedAt;
  final String syncStatus;

  Map<String, Object?> toJson() => <String, Object?>{
        'clientGuid': clientGuid,
        'sequenceNumber': sequenceNumber,
        'lat': lat,
        'lng': lng,
        'accuracyM': accuracyMeters,
        'recordedAt': recordedAt.toUtc().toIso8601String(),
      };

  static WalkPoint fromJson(
    Map<String, dynamic> json, {
    required String clientGuid,
    required String syncStatus,
  }) {
    return WalkPoint(
      clientGuid: clientGuid,
      sequenceNumber: (json['sequenceNumber'] as num).toInt(),
      lat: (json['lat'] as num).toDouble(),
      lng: (json['lng'] as num).toDouble(),
      accuracyMeters: (json['accuracyM'] as num).toDouble(),
      recordedAt: DateTime.parse(json['recordedAt'] as String).toUtc(),
      syncStatus: syncStatus,
    );
  }
}

/// Non-text sensory feedback abstraction for closure cues (Doc 14 §3).
abstract class WalkFeedbackSignal {
  Future<void> onAlmostComplete();
}

/// Default device haptic + system alert sound feedback for low-literacy walk cues.
class PlatformWalkFeedbackSignal implements WalkFeedbackSignal {
  const PlatformWalkFeedbackSignal();

  @override
  Future<void> onAlmostComplete() async {
    await HapticFeedback.heavyImpact();
    await SystemSound.play(SystemSoundType.alert);
  }
}

/// Geodesic Haversine distance helper in metres.
double haversineDistanceMeters(
  double lat1,
  double lng1,
  double lat2,
  double lng2,
) {
  const double earthRadiusMeters = 6371008.8;
  final double phi1 = lat1 * pi / 180.0;
  final double phi2 = lat2 * pi / 180.0;
  final double dPhi = (lat2 - lat1) * pi / 180.0;
  final double dLambda = (lng2 - lng1) * pi / 180.0;

  final double a = sin(dPhi / 2.0) * sin(dPhi / 2.0) +
      cos(phi1) * cos(phi2) * sin(dLambda / 2.0) * sin(dLambda / 2.0);
  final double c = 2.0 * atan2(sqrt(a), sqrt(1.0 - a));
  return earthRadiusMeters * c;
}
