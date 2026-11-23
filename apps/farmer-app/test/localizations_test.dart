import 'package:farm_basics_app/l10n/farm_basics_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:material_ui/material_ui.dart';

void main() {
  test('S11.2.M1 supports all 5 Nigerian pilot locales with Doc 16A copy and governance flag', () async {
    expect(FarmBasicsLocalizations.supportedLocales.length, equals(5));

    const List<Locale> expectedLocales = <Locale>[
      Locale('en', 'NG'),
      Locale('ha'),
      Locale('yo'),
      Locale('ig'),
      Locale('pcm'),
    ];

    const List<String> errorCodes = <String>[
      'NIN_ALREADY_REGISTERED',
      'NIN_LOOKUP_UNDER_REVIEW',
      'NIN_NOT_FOUND',
      'INVALID_OTP',
      'INSUFFICIENT_POINTS',
      'BOUNDARY_REQUIRED',
      'STAGE_GATE_BLOCKED',
      'CAPABILITY_DISABLED',
      'OFFLINE_QUEUED',
    ];

    final Set<String> registerActions = <String>{};
    final Set<String> insufficientPointsCopy = <String>{};

    for (final Locale locale in expectedLocales) {
      expect(FarmBasicsLocalizations.delegate.isSupported(locale), isTrue);
      final FarmBasicsLocalizations l10n =
          await FarmBasicsLocalizations.delegate.load(locale);

      expect(l10n.appTitle, equals('Farm Basics'));
      expect(l10n.registerFarmerAction.trim(), isNotEmpty);
      expect(l10n.walkBoundaryAction.trim(), isNotEmpty);
      expect(l10n.cropCycleTasksAction.trim(), isNotEmpty);
      expect(l10n.syncNowAction.trim(), isNotEmpty);
      expect(l10n.pendingReviewNotice.trim(), isNotEmpty);

      registerActions.add(l10n.registerFarmerAction);
      insufficientPointsCopy.add(l10n.errorMessage('INSUFFICIENT_POINTS'));

      for (final String code in errorCodes) {
        final String msg = l10n.errorMessage(code);
        expect(msg.trim(), isNotEmpty);
        expect(msg, isNot(contains(code)));
      }

      final String ninMsg =
          l10n.errorMessage('NIN_ALREADY_REGISTERED').toLowerCase();
      for (final String leakTerm in const <String>[
        'another',
        'someone else',
        'anoda',
        'wani asusu',
        'agbe miran',
        'onye ozo',
      ]) {
        expect(ninMsg, isNot(contains(leakTerm)));
      }

      if (locale.languageCode == 'en') {
        expect(l10n.isNativeSpeakerVerified, isTrue);
        expect(
          l10n.nativeSpeakerSignOffStatus,
          equals('verified_normative_en_ng'),
        );
      } else {
        expect(l10n.isNativeSpeakerVerified, isFalse);
        expect(
          l10n.nativeSpeakerSignOffStatus,
          equals('pending_native_speaker_signoff_doc_16a_s7'),
        );
      }
    }

    // Distinct localized copy across Nigerian languages and Pidgin.
    expect(registerActions.length, greaterThanOrEqualTo(4));
    expect(insufficientPointsCopy.length, equals(5));
  });

  testWidgets('S11.2.M1 renders localized actions inside MaterialApp for ha, yo, ig, pcm without external l10n deps', (
    WidgetTester tester,
  ) async {
    for (final Locale locale in FarmBasicsLocalizations.supportedLocales) {
      await tester.pumpWidget(
        MaterialApp(
          supportedLocales: const <Locale>[Locale('en', 'NG')],
          localizationsDelegates: FarmBasicsLocalizations.localizationsDelegates,
          builder: (BuildContext context, Widget? child) {
            return FarmBasicsLocalizations.wrapWithLocale(
              context: context,
              locale: locale,
              child: child ?? const SizedBox.shrink(),
            );
          },
          home: Builder(
            builder: (BuildContext context) {
              final FarmBasicsLocalizations l10n =
                  FarmBasicsLocalizations.of(context);
              return Scaffold(
                body: Column(
                  children: <Widget>[
                    Text(l10n.walkBoundaryAction),
                    Text(l10n.errorMessage('OFFLINE_QUEUED')),
                    Text(l10n.nativeSpeakerSignOffStatus),
                  ],
                ),
              );
            },
          ),
        ),
      );
      await tester.pumpAndSettle();

      final FarmBasicsLocalizations loaded =
          await FarmBasicsLocalizations.delegate.load(locale);
      expect(find.text(loaded.walkBoundaryAction), findsOneWidget);
      expect(find.text(loaded.errorMessage('OFFLINE_QUEUED')), findsOneWidget);
    }
  });
}
