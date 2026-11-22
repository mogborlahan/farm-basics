import 'package:drift/native.dart';
import 'package:farm_basics_app/registration/registration_api_client.dart';
import 'package:farm_basics_app/registration/registration_wizard_screen.dart';
import 'package:farm_basics_app/sync/outbox_database.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:material_ui/material_ui.dart';

void main() {
  testWidgets(
    'Preserves non-sensitive registration draft offline/across restart without persisting raw NIN, handles OTP lockout, and completes activation',
    (WidgetTester tester) async {
      final AppOutboxDatabase db = AppOutboxDatabase(NativeDatabase.memory());
      addTearDown(db.close);

      final _FakeRegistrationApiClient api = _FakeRegistrationApiClient();
      DateTime fixedNow = DateTime.utc(2026, 11, 22, 15, 30, 0);

      Future<void> pumpWizard() async {
        await tester.pumpWidget(
          MaterialApp(
            home: SelfServiceRegistrationScreen(
              database: db,
              apiClient: api,
              nowUtc: () => fixedNow,
            ),
          ),
        );
        await tester.pumpAndSettle();
      }

      await pumpWizard();

      // 1. Provider outage (503) preserves NIN in controller memory and saves phone/email/state in Drift.
      api.nextSubmitResult = const RegistrationSubmitResult(
        kind: RegistrationSubmitOutcomeKind.providerUnavailable,
        errorCode: 'NIN_PROVIDER_UNAVAILABLE',
        message: 'NIN service unavailable. Your details are saved for retry.',
      );

      await tester.enterText(
        find.byKey(const ValueKey<String>('nin-input')),
        '12345678901',
      );
      await tester.enterText(
        find.byKey(const ValueKey<String>('phone-input')),
        '+2348012345678',
      );
      await tester.enterText(
        find.byKey(const ValueKey<String>('email-input')),
        'amina@synthetic.farmbasics.test',
      );
      await tester.enterText(
        find.byKey(const ValueKey<String>('state-input')),
        'Lagos',
      );

      await tester.tap(
        find.byKey(const ValueKey<String>('submit-registration-button')),
      );
      await tester.pumpAndSettle();

      expect(
        find.byKey(const ValueKey<String>('registration-status-banner')),
        findsOneWidget,
      );
      expect(find.text('Retry Verification'), findsOneWidget);

      final RegistrationDraft? draftAfterOutage =
          await db.loadRegistrationDraft();
      expect(draftAfterOutage, isNotNull);
      expect(draftAfterOutage!.phone, equals('+2348012345678'));
      expect(draftAfterOutage.email, equals('amina@synthetic.farmbasics.test'));
      expect(draftAfterOutage.stateCode, equals('Lagos'));
      expect(draftAfterOutage.stepIndex, equals(0));

      // Verify raw NIN still lives in memory field while Retry succeeds once provider recovers.
      api.nextSubmitResult = const RegistrationSubmitResult(
        kind: RegistrationSubmitOutcomeKind.pendingOtp,
        identityId: '11111111-1111-4111-8111-111111111111',
        verificationStep: 'phone_otp',
        smsDispatched: true,
        firstName: 'Amina',
        lastName: 'Bello',
      );

      await tester.tap(
        find.byKey(const ValueKey<String>('submit-registration-button')),
      );
      await tester.pumpAndSettle();

      expect(find.text('Step 2 of 2 — Confirm Phone Code'), findsOneWidget);

      // 2. Simulate app close & cold reopen: OTP step + phone + identityId are restored from Drift.
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pump();
      await pumpWizard();

      expect(find.text('Step 2 of 2 — Confirm Phone Code'), findsOneWidget);
      expect(find.text('Phone: +2348012345678'), findsOneWidget);

      // 3. Repeated bad OTP returns 429 OTP_LOCKED with visible lockout banner & disabled confirm button.
      api.nextVerifyResult = const OtpVerifyResult(
        kind: OtpVerifyOutcomeKind.locked,
        retryAfterSeconds: 60,
        errorCode: 'OTP_LOCKED',
        message: 'Locked for 60 seconds.',
      );

      await tester.enterText(
        find.byKey(const ValueKey<String>('otp-input')),
        '000000',
      );
      await tester.tap(find.byKey(const ValueKey<String>('verify-otp-button')));
      await tester.pumpAndSettle();

      expect(
        find.byKey(const ValueKey<String>('otp-lockout-banner')),
        findsOneWidget,
      );

      // Advance synthetic clock past 60s lockout, reopen, and complete with valid OTP -> Farmer ID displayed.
      fixedNow = fixedNow.add(const Duration(seconds: 65));
      await pumpWizard();

      expect(
        find.byKey(const ValueKey<String>('otp-lockout-banner')),
        findsNothing,
      );

      api.nextVerifyResult = const OtpVerifyResult(
        kind: OtpVerifyOutcomeKind.activated,
        identityId: '11111111-1111-4111-8111-111111111111',
        farmerId: 'FB-NG-100001',
      );

      await tester.enterText(
        find.byKey(const ValueKey<String>('otp-input')),
        '482913',
      );
      await tester.tap(find.byKey(const ValueKey<String>('verify-otp-button')));
      await tester.pumpAndSettle();

      expect(find.text('Account Activated'), findsOneWidget);
      expect(find.text('FB-NG-100001'), findsOneWidget);
    },
  );
}

class _FakeRegistrationApiClient implements RegistrationApiClient {
  RegistrationSubmitResult nextSubmitResult = const RegistrationSubmitResult(
    kind: RegistrationSubmitOutcomeKind.pendingOtp,
    identityId: '11111111-1111-4111-8111-111111111111',
  );

  OtpVerifyResult nextVerifyResult = const OtpVerifyResult(
    kind: OtpVerifyOutcomeKind.activated,
    identityId: '11111111-1111-4111-8111-111111111111',
    farmerId: 'FB-NG-100001',
  );

  @override
  Future<RegistrationSubmitResult> submitRegistration(
    RegistrationSubmissionInput input,
  ) async {
    return nextSubmitResult;
  }

  @override
  Future<OtpVerifyResult> verifyOtp({
    required String identityId,
    required String otp,
  }) async {
    return nextVerifyResult;
  }

  @override
  Future<bool> resendOtp({required String identityId}) async => true;
}
