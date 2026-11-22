import 'package:dio/dio.dart';

class RegistrationSubmissionInput {
  const RegistrationSubmissionInput({
    required this.nin,
    required this.phone,
    this.email,
    this.stateId,
    this.lgaId,
  });

  final String nin;
  final String phone;
  final String? email;
  final String? stateId;
  final String? lgaId;
}

enum RegistrationSubmitOutcomeKind {
  pendingOtp,
  underReview,
  ninNotFound,
  providerUnavailable,
  ninAlreadyRegistered,
  validationError,
  networkError,
}

class RegistrationSubmitResult {
  const RegistrationSubmitResult({
    required this.kind,
    this.identityId,
    this.verificationStep,
    this.resumed = false,
    this.smsDispatched = false,
    this.reviewQueueItemId,
    this.firstName,
    this.lastName,
    this.errorCode,
    this.message,
  });

  final RegistrationSubmitOutcomeKind kind;
  final String? identityId;
  final String? verificationStep;
  final bool resumed;
  final bool smsDispatched;
  final String? reviewQueueItemId;
  final String? firstName;
  final String? lastName;
  final String? errorCode;
  final String? message;
}

enum OtpVerifyOutcomeKind {
  activated,
  invalidOtp,
  locked,
  expired,
  validationError,
  networkError,
}

class OtpVerifyResult {
  const OtpVerifyResult({
    required this.kind,
    this.identityId,
    this.farmerId,
    this.attemptsRemaining,
    this.retryAfterSeconds,
    this.errorCode,
    this.message,
  });

  final OtpVerifyOutcomeKind kind;
  final String? identityId;
  final String? farmerId;
  final int? attemptsRemaining;
  final int? retryAfterSeconds;
  final String? errorCode;
  final String? message;
}

abstract interface class RegistrationApiClient {
  Future<RegistrationSubmitResult> submitRegistration(
    RegistrationSubmissionInput input,
  );

  Future<OtpVerifyResult> verifyOtp({
    required String identityId,
    required String otp,
  });

  Future<bool> resendOtp({
    required String identityId,
  });
}

class DioRegistrationApiClient implements RegistrationApiClient {
  DioRegistrationApiClient({required this.dio});

  final Dio dio;

  @override
  Future<RegistrationSubmitResult> submitRegistration(
    RegistrationSubmissionInput input,
  ) async {
    try {
      final Response<dynamic> response = await dio.post<dynamic>(
        '/v1/registrations/self-service',
        data: <String, Object?>{
          'nin': input.nin,
          'phone': input.phone,
          if (input.email != null && input.email!.trim().isNotEmpty)
            'email': input.email!.trim(),
          if (input.stateId != null) 'stateId': input.stateId,
          if (input.lgaId != null) 'lgaId': input.lgaId,
        },
        options: Options(validateStatus: (_) => true),
      );

      final int statusCode = response.statusCode ?? 0;
      final Map<String, dynamic> root = _asMap(response.data);
      final Map<String, dynamic> data = _asMap(root['data']);
      final Map<String, dynamic> err = _asMap(root['error']);

      if (statusCode == 201) {
        return RegistrationSubmitResult(
          kind: RegistrationSubmitOutcomeKind.pendingOtp,
          identityId: data['identityId'] as String?,
          verificationStep: data['verificationStep'] as String?,
          resumed: data['resumed'] == true,
          smsDispatched: data['smsDispatched'] == true,
          firstName: data['firstName'] as String?,
          lastName: data['lastName'] as String?,
        );
      }

      if (statusCode == 202) {
        return RegistrationSubmitResult(
          kind: RegistrationSubmitOutcomeKind.underReview,
          reviewQueueItemId: data['reviewQueueItemId'] as String?,
          errorCode: data['code'] as String?,
          message: data['message'] as String?,
        );
      }

      final String? errorCode = err['code'] as String?;
      final String? message = err['message'] as String?;

      if (errorCode == 'NIN_NOT_FOUND') {
        return RegistrationSubmitResult(
          kind: RegistrationSubmitOutcomeKind.ninNotFound,
          errorCode: errorCode,
          message: message,
        );
      }

      if (statusCode == 503 || errorCode == 'NIN_PROVIDER_UNAVAILABLE') {
        return RegistrationSubmitResult(
          kind: RegistrationSubmitOutcomeKind.providerUnavailable,
          errorCode: errorCode ?? 'NIN_PROVIDER_UNAVAILABLE',
          message: message,
        );
      }

      if (statusCode == 409) {
        return RegistrationSubmitResult(
          kind: RegistrationSubmitOutcomeKind.ninAlreadyRegistered,
          errorCode: errorCode,
          message: message,
        );
      }

      return RegistrationSubmitResult(
        kind: RegistrationSubmitOutcomeKind.validationError,
        errorCode: errorCode ?? 'HTTP_$statusCode',
        message: message ?? 'Please check the details entered and retry.',
      );
    } on DioException {
      return const RegistrationSubmitResult(
        kind: RegistrationSubmitOutcomeKind.networkError,
        errorCode: 'OFFLINE_OR_TIMEOUT',
        message:
            'No internet connection right now. Your details are saved so you can retry when back online.',
      );
    }
  }

  @override
  Future<OtpVerifyResult> verifyOtp({
    required String identityId,
    required String otp,
  }) async {
    try {
      final Response<dynamic> response = await dio.post<dynamic>(
        '/v1/registrations/self-service/$identityId/verify-otp',
        data: <String, Object?>{
          'otp': otp,
          'channel': 'phone',
        },
        options: Options(validateStatus: (_) => true),
      );

      final int statusCode = response.statusCode ?? 0;
      final Map<String, dynamic> root = _asMap(response.data);
      final Map<String, dynamic> data = _asMap(root['data']);
      final Map<String, dynamic> err = _asMap(root['error']);
      final Map<String, dynamic> details = _asMap(err['details']);

      if (statusCode == 200) {
        return OtpVerifyResult(
          kind: OtpVerifyOutcomeKind.activated,
          identityId: data['identityId'] as String?,
          farmerId: data['farmerId'] as String?,
        );
      }

      final String? errorCode = err['code'] as String?;
      final String? message = err['message'] as String?;

      if (statusCode == 429 || errorCode == 'OTP_LOCKED') {
        return OtpVerifyResult(
          kind: OtpVerifyOutcomeKind.locked,
          retryAfterSeconds: details['retryAfterSeconds'] as int? ?? 60,
          errorCode: errorCode ?? 'OTP_LOCKED',
          message: message,
        );
      }

      if (errorCode == 'OTP_EXPIRED') {
        return OtpVerifyResult(
          kind: OtpVerifyOutcomeKind.expired,
          errorCode: errorCode,
          message: message,
        );
      }

      if (statusCode == 400) {
        return OtpVerifyResult(
          kind: OtpVerifyOutcomeKind.invalidOtp,
          attemptsRemaining: details['attemptsRemaining'] as int?,
          errorCode: errorCode ?? 'INVALID_OTP',
          message: message,
        );
      }

      return OtpVerifyResult(
        kind: OtpVerifyOutcomeKind.validationError,
        errorCode: errorCode ?? 'HTTP_$statusCode',
        message: message,
      );
    } on DioException {
      return const OtpVerifyResult(
        kind: OtpVerifyOutcomeKind.networkError,
        errorCode: 'OFFLINE_OR_TIMEOUT',
        message:
            'Unable to reach the server right now. Your progress stays saved for retry.',
      );
    }
  }

  @override
  Future<bool> resendOtp({required String identityId}) async {
    try {
      final Response<dynamic> response = await dio.post<dynamic>(
        '/v1/registrations/self-service/$identityId/resend-otp',
        data: const <String, Object?>{'channel': 'phone'},
        options: Options(validateStatus: (_) => true),
      );
      return response.statusCode == 200;
    } on DioException {
      return false;
    }
  }

  static Map<String, dynamic> _asMap(dynamic raw) {
    if (raw is Map<String, dynamic>) {
      return raw;
    }
    if (raw is Map) {
      return raw.cast<String, dynamic>();
    }
    return const <String, dynamic>{};
  }
}
