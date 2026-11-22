/// Validation state for an agronomic CropCycle operation task (PRD Volume A §3.7).
enum CropTaskValidationStatus {
  pending,
  ok,
  warning,
  flag,
}

/// Interactive model representing a CropCycle task row in the Farmer App.
class CropTaskItem {
  CropTaskItem({
    required this.taskDefinitionId,
    required this.stage,
    required this.name,
    required this.required,
    this.unitLabel,
    this.minValue,
    this.maxValue,
    this.status = CropTaskValidationStatus.pending,
    this.validationMessage,
    this.overrideReason,
    this.recordedAt,
    this.lastNumericValue,
  });

  final String taskDefinitionId;
  final String stage;
  final String name;
  final bool required;
  final String? unitLabel;
  final double? minValue;
  final double? maxValue;

  CropTaskValidationStatus status;
  String? validationMessage;
  String? overrideReason;
  DateTime? recordedAt;
  double? lastNumericValue;

  bool get isOverridden =>
      status == CropTaskValidationStatus.flag &&
      overrideReason != null &&
      overrideReason!.trim().isNotEmpty;
}

/// Client-side immediate validation preview matching server +-10% tolerance (PRD Volume A §4.3).
({CropTaskValidationStatus status, String message}) validateNumericTaskValue({
  required double value,
  required double minValue,
  required double maxValue,
  required String unitLabel,
  required String cropName,
}) {
  if (value >= minValue && value <= maxValue) {
    return (
      status: CropTaskValidationStatus.ok,
      message: 'Looks good ($value $unitLabel is in range for $cropName).',
    );
  }

  final double warningMin = minValue * 0.90;
  final double warningMax = maxValue * 1.10;
  if (value >= warningMin && value <= warningMax) {
    return (
      status: CropTaskValidationStatus.warning,
      message:
          'Slightly outside typical $cropName range ($minValue–$maxValue $unitLabel). Recorded as advisory.',
    );
  }

  return (
    status: CropTaskValidationStatus.flag,
    message:
        'Flagged: this value may harm $cropName growth or yield. Double-check the reading, or enter a short reason to continue.',
  );
}
