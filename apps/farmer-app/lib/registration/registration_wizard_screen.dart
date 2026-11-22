import 'package:drift/drift.dart' show Value;
import 'package:material_ui/material_ui.dart';

import '../main.dart';
import '../sync/outbox_database.dart';
import 'registration_api_client.dart';

/// Offline-resilient DS2 self-service registration wizard (WP-21 / FR-ID-001).
/// Persists non-sensitive form fields across restarts/offline outages while keeping
/// raw NIN & OTP strictly in volatile memory (Doc/AGENTS.md Rule 1).
class SelfServiceRegistrationScreen extends StatefulWidget {
  const SelfServiceRegistrationScreen({
    super.key,
    required this.database,
    required this.apiClient,
    this.nowUtc,
  });

  final AppOutboxDatabase database;
  final RegistrationApiClient apiClient;
  final DateTime Function()? nowUtc;

  @override
  State<SelfServiceRegistrationScreen> createState() =>
      _SelfServiceRegistrationScreenState();
}

class _SelfServiceRegistrationScreenState
    extends State<SelfServiceRegistrationScreen> {
  final TextEditingController _ninController = TextEditingController();
  final TextEditingController _phoneController = TextEditingController();
  final TextEditingController _emailController = TextEditingController();
  final TextEditingController _stateCodeController = TextEditingController();
  final TextEditingController _lgaNameController = TextEditingController();
  final TextEditingController _otpController = TextEditingController();

  bool _loadingDraft = true;
  bool _submitting = false;
  int _stepIndex = 0; // 0: form, 1: OTP, 2: under review, 3: active
  String? _pendingIdentityId;
  String? _farmerId;
  String? _verifiedFullName;
  String? _reviewQueueItemId;
  String? _statusMessage;
  String? _lastErrorCode;
  DateTime? _lockoutUntilUtc;

  DateTime _utcNow() => (widget.nowUtc ?? () => DateTime.now().toUtc())();

  @override
  void initState() {
    super.initState();
    _restoreDraft();
  }

  @override
  void dispose() {
    _ninController.dispose();
    _phoneController.dispose();
    _emailController.dispose();
    _stateCodeController.dispose();
    _lgaNameController.dispose();
    _otpController.dispose();
    super.dispose();
  }

  Future<void> _restoreDraft() async {
    final RegistrationDraft? draft =
        await widget.database.loadRegistrationDraft();
    if (!mounted) {
      return;
    }

    if (draft != null) {
      _phoneController.text = draft.phone;
      _emailController.text = draft.email;
      _stateCodeController.text = draft.stateCode;
      _lgaNameController.text = draft.lgaName;
      _stepIndex = draft.stepIndex;
      _pendingIdentityId = draft.pendingIdentityId;
      _farmerId = draft.farmerId;
      _statusMessage = draft.statusMessage;
      _lastErrorCode = draft.lastErrorCode;
      _lockoutUntilUtc = draft.lockoutUntilUtc?.toUtc();
    }

    setState(() {
      _loadingDraft = false;
    });
  }

  Future<void> _persistDraft({
    int? stepIndex,
    String? pendingIdentityId,
    bool clearPendingIdentity = false,
    String? farmerId,
    String? statusMessage,
    String? errorCode,
    DateTime? lockoutUntilUtc,
    bool clearLockout = false,
  }) async {
    final int nextStep = stepIndex ?? _stepIndex;
    final String? nextPendingId =
        clearPendingIdentity ? null : (pendingIdentityId ?? _pendingIdentityId);
    final String? nextFarmerId = farmerId ?? _farmerId;
    final DateTime? nextLockout =
        clearLockout ? null : (lockoutUntilUtc ?? _lockoutUntilUtc);

    await widget.database.saveRegistrationDraft(
      RegistrationDraftsCompanion(
        id: const Value<int>(1),
        stepIndex: Value<int>(nextStep),
        phone: Value<String>(_phoneController.text.trim()),
        email: Value<String>(_emailController.text.trim()),
        stateCode: Value<String>(_stateCodeController.text.trim()),
        lgaName: Value<String>(_lgaNameController.text.trim()),
        pendingIdentityId: Value<String?>(nextPendingId),
        farmerId: Value<String?>(nextFarmerId),
        statusMessage: Value<String?>(statusMessage),
        lastErrorCode: Value<String?>(errorCode),
        lockoutUntilUtc: Value<DateTime?>(nextLockout),
        updatedAt: Value<DateTime>(_utcNow()),
      ),
    );
  }

  Future<void> _handleSubmitRegistration() async {
    final String nin = _ninController.text.trim();
    final String phone = _phoneController.text.trim();

    if (nin.length != 11 || int.tryParse(nin) == null) {
      setState(() {
        _lastErrorCode = 'VALIDATION_ERROR';
        _statusMessage = 'Enter your 11-digit National Identification Number.';
      });
      return;
    }

    if (phone.isEmpty) {
      setState(() {
        _lastErrorCode = 'VALIDATION_ERROR';
        _statusMessage = 'Enter your Nigerian mobile number.';
      });
      return;
    }

    setState(() {
      _submitting = true;
      _statusMessage = null;
      _lastErrorCode = null;
    });

    await _persistDraft(stepIndex: 0);

    final RegistrationSubmitResult result =
        await widget.apiClient.submitRegistration(
      RegistrationSubmissionInput(
        nin: nin,
        phone: phone,
        email: _emailController.text.trim(),
      ),
    );

    if (!mounted) {
      return;
    }

    switch (result.kind) {
      case RegistrationSubmitOutcomeKind.pendingOtp:
        final String fullName = <String?>[result.firstName, result.lastName]
            .whereType<String>()
            .where((String s) => s.isNotEmpty)
            .join(' ');
        final String info = result.resumed
            ? 'Resumed your incomplete registration. Enter the SMS verification code.'
            : 'Verification code sent by SMS. Enter the 6-digit code below.';
        await _persistDraft(
          stepIndex: 1,
          pendingIdentityId: result.identityId,
          statusMessage: info,
          errorCode: null,
        );
        if (!mounted) {
          return;
        }
        setState(() {
          _submitting = false;
          _stepIndex = 1;
          _pendingIdentityId = result.identityId;
          _verifiedFullName = fullName.isEmpty ? null : fullName;
          _statusMessage = info;
          _lastErrorCode = null;
        });

      case RegistrationSubmitOutcomeKind.underReview:
        final String reviewMsg = result.message ??
            'Your registration is under review, typically resolved within 48 hours.';
        await _persistDraft(
          stepIndex: 2,
          statusMessage: reviewMsg,
          errorCode: result.errorCode ?? 'NIN_LOOKUP_UNDER_REVIEW',
        );
        if (!mounted) {
          return;
        }
        setState(() {
          _submitting = false;
          _stepIndex = 2;
          _reviewQueueItemId = result.reviewQueueItemId;
          _statusMessage = reviewMsg;
          _lastErrorCode = result.errorCode ?? 'NIN_LOOKUP_UNDER_REVIEW';
        });

      case RegistrationSubmitOutcomeKind.providerUnavailable:
      case RegistrationSubmitOutcomeKind.networkError:
        final String retryMsg = result.message ??
            'Verification service is temporarily unavailable. Your form details are saved for retry.';
        await _persistDraft(
          stepIndex: 0,
          statusMessage: retryMsg,
          errorCode: result.errorCode,
        );
        if (!mounted) {
          return;
        }
        setState(() {
          _submitting = false;
          _statusMessage = retryMsg;
          _lastErrorCode = result.errorCode;
        });

      case RegistrationSubmitOutcomeKind.ninNotFound:
      case RegistrationSubmitOutcomeKind.ninAlreadyRegistered:
      case RegistrationSubmitOutcomeKind.validationError:
        final String errMsg =
            result.message ?? 'Registration could not be completed.';
        await _persistDraft(
          stepIndex: 0,
          statusMessage: errMsg,
          errorCode: result.errorCode,
        );
        if (!mounted) {
          return;
        }
        setState(() {
          _submitting = false;
          _statusMessage = errMsg;
          _lastErrorCode = result.errorCode;
        });
    }
  }

  Future<void> _handleVerifyOtp() async {
    final String? identityId = _pendingIdentityId;
    final String otp = _otpController.text.trim();
    if (identityId == null || otp.length != 6) {
      setState(() {
        _lastErrorCode = 'INVALID_OTP';
        _statusMessage = 'Enter the 6-digit SMS code.';
      });
      return;
    }

    setState(() {
      _submitting = true;
      _statusMessage = null;
    });

    final OtpVerifyResult result = await widget.apiClient.verifyOtp(
      identityId: identityId,
      otp: otp,
    );

    if (!mounted) {
      return;
    }

    switch (result.kind) {
      case OtpVerifyOutcomeKind.activated:
        final String doneMsg = 'Registration complete. Your Farmer ID is ready.';
        await _persistDraft(
          stepIndex: 3,
          farmerId: result.farmerId,
          statusMessage: doneMsg,
          errorCode: null,
          clearLockout: true,
        );
        if (!mounted) {
          return;
        }
        setState(() {
          _submitting = false;
          _stepIndex = 3;
          _farmerId = result.farmerId;
          _lockoutUntilUtc = null;
          _statusMessage = doneMsg;
          _lastErrorCode = null;
        });

      case OtpVerifyOutcomeKind.locked:
        final int waitSeconds = result.retryAfterSeconds ?? 60;
        final DateTime lockedUntil =
            _utcNow().add(Duration(seconds: waitSeconds));
        final String lockMsg =
            'Too many wrong codes. Try again in $waitSeconds seconds.';
        await _persistDraft(
          stepIndex: 1,
          statusMessage: lockMsg,
          errorCode: 'OTP_LOCKED',
          lockoutUntilUtc: lockedUntil,
        );
        if (!mounted) {
          return;
        }
        setState(() {
          _submitting = false;
          _lockoutUntilUtc = lockedUntil;
          _statusMessage = lockMsg;
          _lastErrorCode = 'OTP_LOCKED';
        });

      case OtpVerifyOutcomeKind.invalidOtp:
        final int? rem = result.attemptsRemaining;
        final String badMsg = rem != null
            ? 'Incorrect code. $rem attempt(s) remaining.'
            : (result.message ?? 'Incorrect verification code.');
        await _persistDraft(
          stepIndex: 1,
          statusMessage: badMsg,
          errorCode: result.errorCode,
        );
        if (!mounted) {
          return;
        }
        setState(() {
          _submitting = false;
          _statusMessage = badMsg;
          _lastErrorCode = result.errorCode;
        });

      case OtpVerifyOutcomeKind.expired:
      case OtpVerifyOutcomeKind.validationError:
      case OtpVerifyOutcomeKind.networkError:
        final String msg =
            result.message ?? 'Could not verify code right now. Please retry.';
        await _persistDraft(
          stepIndex: 1,
          statusMessage: msg,
          errorCode: result.errorCode,
        );
        if (!mounted) {
          return;
        }
        setState(() {
          _submitting = false;
          _statusMessage = msg;
          _lastErrorCode = result.errorCode;
        });
    }
  }

  Future<void> _handleResendOtp() async {
    final String? identityId = _pendingIdentityId;
    if (identityId == null) {
      return;
    }

    setState(() {
      _submitting = true;
    });
    final bool ok = await widget.apiClient.resendOtp(identityId: identityId);
    if (!mounted) {
      return;
    }

    setState(() {
      _submitting = false;
      _statusMessage = ok
          ? 'A new 6-digit verification code has been sent by SMS.'
          : 'Could not resend right now. Try again shortly.';
    });
  }

  Future<void> _handleResetDraft() async {
    await widget.database.clearRegistrationDraft();
    if (!mounted) {
      return;
    }
    _ninController.clear();
    _phoneController.clear();
    _emailController.clear();
    _stateCodeController.clear();
    _lgaNameController.clear();
    _otpController.clear();
    setState(() {
      _stepIndex = 0;
      _pendingIdentityId = null;
      _farmerId = null;
      _verifiedFullName = null;
      _reviewQueueItemId = null;
      _statusMessage = null;
      _lastErrorCode = null;
      _lockoutUntilUtc = null;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: FarmBasicsApp.creamCanvas,
      appBar: AppBar(
        backgroundColor: FarmBasicsApp.forestPrimary,
        foregroundColor: Colors.white,
        title: const Text('Farmer Self-Registration'),
      ),
      body: _loadingDraft
          ? const Center(child: CircularProgressIndicator())
          : SingleChildScrollView(
              padding: const EdgeInsets.all(20),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: <Widget>[
                  if (_statusMessage != null)
                    Container(
                      key: const ValueKey<String>('registration-status-banner'),
                      margin: const EdgeInsets.only(bottom: 16),
                      padding: const EdgeInsets.all(14),
                      decoration: BoxDecoration(
                        color: const Color(0xFFFFF8E1),
                        borderRadius: BorderRadius.circular(10),
                        border: Border.all(color: const Color(0xFFD9A300)),
                      ),
                      child: Text(
                        _statusMessage!,
                        style: const TextStyle(
                          color: FarmBasicsApp.ink900,
                          fontSize: 15,
                          fontWeight: FontWeight.w500,
                        ),
                      ),
                    ),
                  if (_stepIndex == 0) _buildDetailsForm(),
                  if (_stepIndex == 1) _buildOtpStep(),
                  if (_stepIndex == 2) _buildUnderReviewStep(),
                  if (_stepIndex == 3) _buildCompletedStep(),
                ],
              ),
            ),
    );
  }

  Widget _buildDetailsForm() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        const Text(
          'Step 1 of 2 — Identity & Contact Details',
          style: TextStyle(
            color: FarmBasicsApp.ink900,
            fontSize: 18,
            fontWeight: FontWeight.w700,
          ),
        ),
        const SizedBox(height: 12),
        TextField(
          key: const ValueKey<String>('nin-input'),
          controller: _ninController,
          keyboardType: TextInputType.number,
          maxLength: 11,
          decoration: const InputDecoration(
            labelText: '11-digit NIN',
            border: OutlineInputBorder(),
            helperText: 'Kept only in memory during verification; never stored.',
          ),
        ),
        const SizedBox(height: 12),
        TextField(
          key: const ValueKey<String>('phone-input'),
          controller: _phoneController,
          keyboardType: TextInputType.phone,
          onChanged: (_) => _persistDraft(stepIndex: 0),
          decoration: const InputDecoration(
            labelText: 'Phone number (e.g. +2348012345678)',
            border: OutlineInputBorder(),
          ),
        ),
        const SizedBox(height: 12),
        TextField(
          key: const ValueKey<String>('email-input'),
          controller: _emailController,
          keyboardType: TextInputType.emailAddress,
          onChanged: (_) => _persistDraft(stepIndex: 0),
          decoration: const InputDecoration(
            labelText: 'Email (optional)',
            border: OutlineInputBorder(),
          ),
        ),
        const SizedBox(height: 12),
        TextField(
          key: const ValueKey<String>('state-input'),
          controller: _stateCodeController,
          onChanged: (_) => _persistDraft(stepIndex: 0),
          decoration: const InputDecoration(
            labelText: 'State (e.g. Lagos)',
            border: OutlineInputBorder(),
          ),
        ),
        const SizedBox(height: 12),
        TextField(
          key: const ValueKey<String>('lga-input'),
          controller: _lgaNameController,
          onChanged: (_) => _persistDraft(stepIndex: 0),
          decoration: const InputDecoration(
            labelText: 'LGA (e.g. Ikeja)',
            border: OutlineInputBorder(),
          ),
        ),
        const SizedBox(height: 20),
        SizedBox(
          height: 52,
          child: FilledButton(
            key: const ValueKey<String>('submit-registration-button'),
            style: FilledButton.styleFrom(
              backgroundColor: FarmBasicsApp.forestPrimary,
            ),
            onPressed: _submitting ? null : _handleSubmitRegistration,
            child: Text(
              _lastErrorCode == 'NIN_PROVIDER_UNAVAILABLE' ||
                      _lastErrorCode == 'OFFLINE_OR_TIMEOUT'
                  ? 'Retry Verification'
                  : 'Verify NIN & Send Code',
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildOtpStep() {
    final DateTime now = _utcNow();
    final bool isLocked =
        _lockoutUntilUtc != null && _lockoutUntilUtc!.isAfter(now);
    final int remainingSeconds = isLocked
        ? _lockoutUntilUtc!.difference(now).inSeconds.clamp(1, 3600)
        : 0;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        const Text(
          'Step 2 of 2 — Confirm Phone Code',
          style: TextStyle(
            color: FarmBasicsApp.ink900,
            fontSize: 18,
            fontWeight: FontWeight.w700,
          ),
        ),
        const SizedBox(height: 8),
        Text(
          'Phone: ${_phoneController.text}',
          style: const TextStyle(color: FarmBasicsApp.ink900, fontSize: 15),
        ),
        if (_verifiedFullName != null)
          Text(
            'Verified Name: $_verifiedFullName',
            style: const TextStyle(color: FarmBasicsApp.ink900, fontSize: 15),
          ),
        if (isLocked) ...<Widget>[
          const SizedBox(height: 12),
          Container(
            key: const ValueKey<String>('otp-lockout-banner'),
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: const Color(0xFFFFEBEE),
              borderRadius: BorderRadius.circular(8),
              border: Border.all(color: const Color(0xFFC62828)),
            ),
            child: Text(
              'Locked for $remainingSeconds second(s) due to repeated wrong attempts.',
              style: const TextStyle(
                color: Color(0xFFB71C1C),
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
        const SizedBox(height: 12),
        TextField(
          key: const ValueKey<String>('otp-input'),
          controller: _otpController,
          keyboardType: TextInputType.number,
          maxLength: 6,
          decoration: const InputDecoration(
            labelText: '6-digit SMS Code',
            border: OutlineInputBorder(),
          ),
        ),
        const SizedBox(height: 16),
        SizedBox(
          height: 52,
          child: FilledButton(
            key: const ValueKey<String>('verify-otp-button'),
            style: FilledButton.styleFrom(
              backgroundColor: FarmBasicsApp.forestPrimary,
            ),
            onPressed: (_submitting || isLocked) ? null : _handleVerifyOtp,
            child: const Text('Confirm Code & Activate Account'),
          ),
        ),
        const SizedBox(height: 12),
        SizedBox(
          height: 48,
          child: OutlinedButton(
            key: const ValueKey<String>('resend-otp-button'),
            onPressed: _submitting ? null : _handleResendOtp,
            child: const Text('Resend SMS Code'),
          ),
        ),
        const SizedBox(height: 8),
        TextButton(
          onPressed: _submitting
              ? null
              : () async {
                  await _persistDraft(stepIndex: 0);
                  if (!mounted) {
                    return;
                  }
                  setState(() {
                    _stepIndex = 0;
                  });
                },
          child: const Text('Back to Registration Details'),
        ),
      ],
    );
  }

  Widget _buildUnderReviewStep() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        const Text(
          'Registration Under Review',
          style: TextStyle(
            color: FarmBasicsApp.ink900,
            fontSize: 20,
            fontWeight: FontWeight.w700,
          ),
        ),
        const SizedBox(height: 12),
        Text(
          'Queue Reference: ${_reviewQueueItemId ?? 'Pending'}',
          style: const TextStyle(color: FarmBasicsApp.ink900),
        ),
        const SizedBox(height: 20),
        SizedBox(
          height: 48,
          child: OutlinedButton(
            onPressed: _handleResetDraft,
            child: const Text('Start New Registration'),
          ),
        ),
      ],
    );
  }

  Widget _buildCompletedStep() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        const Text(
          'Account Activated',
          style: TextStyle(
            color: FarmBasicsApp.forestPrimary,
            fontSize: 22,
            fontWeight: FontWeight.w700,
          ),
        ),
        const SizedBox(height: 12),
        Container(
          key: const ValueKey<String>('farmer-id-card'),
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            color: Colors.white,
            borderRadius: BorderRadius.circular(12),
            border: Border.all(color: FarmBasicsApp.forestPrimary),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              const Text('Your Farmer ID:'),
              const SizedBox(height: 4),
              Text(
                _farmerId ?? '',
                style: const TextStyle(
                  fontSize: 24,
                  fontWeight: FontWeight.w800,
                  color: FarmBasicsApp.ink900,
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 20),
        SizedBox(
          height: 48,
          child: OutlinedButton(
            onPressed: _handleResetDraft,
            child: const Text('Register Another Farmer'),
          ),
        ),
      ],
    );
  }
}
