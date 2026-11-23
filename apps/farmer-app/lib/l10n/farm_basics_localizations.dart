import 'package:flutter/foundation.dart';
import 'package:material_ui/material_ui.dart';

/// Zero-dependency 5-locale localization catalog for Farm Basics (WP-B1 / S11.2.M1).
///
/// Supported locales align with Doc/16A-OPS §7 and Doc/Farm-Basics-Design-System-2:
/// - `en-NG` (English baseline, normative)
/// - `ha` (Hausa — flagged pending native-speaker sign-off before field release)
/// - `yo` (Yoruba — flagged pending native-speaker sign-off before field release)
/// - `ig` (Igbo — flagged pending native-speaker sign-off before field release)
/// - `pcm` (Nigerian Pidgin — flagged pending native-speaker sign-off before field release)
class FarmBasicsLocalizations {
  const FarmBasicsLocalizations(this.locale);

  final Locale locale;

  static const List<Locale> supportedLocales = <Locale>[
    Locale('en', 'NG'),
    Locale('ha'),
    Locale('yo'),
    Locale('ig'),
    Locale('pcm'),
  ];

  static const LocalizationsDelegate<FarmBasicsLocalizations> delegate =
      _FarmBasicsLocalizationsDelegate();

  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates =
      <LocalizationsDelegate<dynamic>>[
        delegate,
        DefaultMaterialLocalizations.delegate,
        DefaultWidgetsLocalizations.delegate,
      ];

  static Widget wrapWithLocale({
    required BuildContext context,
    required Locale locale,
    required Widget child,
  }) {
    final Locale effectiveLocale =
        delegate.isSupported(locale) ? locale : const Locale('en', 'NG');
    return Localizations.override(
      context: context,
      locale: effectiveLocale,
      delegates: const <LocalizationsDelegate<dynamic>>[delegate],
      child: child,
    );
  }

  static FarmBasicsLocalizations of(BuildContext context) {
    final FarmBasicsLocalizations? result =
        Localizations.of<FarmBasicsLocalizations>(
          context,
          FarmBasicsLocalizations,
        );
    assert(result != null, 'No FarmBasicsLocalizations found in context.');
    return result!;
  }

  String get _lang {
    final String code = locale.languageCode.toLowerCase();
    return _localizedStrings.containsKey(code) ? code : 'en';
  }

  bool get isNativeSpeakerVerified => _lang == 'en';

  String get nativeSpeakerSignOffStatus => isNativeSpeakerVerified
      ? 'verified_normative_en_ng'
      : 'pending_native_speaker_signoff_doc_16a_s7';

  String _pick(String key) {
    final Map<String, String> langMap =
        _localizedStrings[_lang] ?? _localizedStrings['en']!;
    return langMap[key] ?? _localizedStrings['en']![key] ?? key;
  }

  String get appTitle => _pick('appTitle');
  String get registerFarmerAction => _pick('registerFarmerAction');
  String get walkBoundaryAction => _pick('walkBoundaryAction');
  String get cropCycleTasksAction => _pick('cropCycleTasksAction');
  String get syncNowAction => _pick('syncNowAction');
  String get pendingReviewNotice => _pick('pendingReviewNotice');

  String errorMessage(String errorCode) {
    final String normalized = errorCode.trim().toUpperCase();
    return switch (normalized) {
      'NIN_ALREADY_REGISTERED' => _pick('errNinAlreadyRegistered'),
      'NIN_LOOKUP_UNDER_REVIEW' => _pick('errNinLookupUnderReview'),
      'NIN_NOT_FOUND' => _pick('errNinNotFound'),
      'INVALID_OTP' => _pick('errInvalidOtp'),
      'INSUFFICIENT_POINTS' => _pick('errInsufficientPoints'),
      'BOUNDARY_REQUIRED' => _pick('errBoundaryRequired'),
      'STAGE_GATE_BLOCKED' => _pick('errStageGateBlocked'),
      'CAPABILITY_DISABLED' => _pick('errCapabilityDisabled'),
      'OFFLINE_QUEUED' => _pick('errOfflineQueued'),
      _ => _pick('errGeneric'),
    };
  }

  static const Map<String, Map<String, String>> _localizedStrings =
      <String, Map<String, String>>{
        'en': <String, String>{
          'appTitle': 'Farm Basics',
          'registerFarmerAction': 'Register New Farmer',
          'walkBoundaryAction': 'Walk Field Boundary',
          'cropCycleTasksAction': 'Crop Cycle Tasks',
          'syncNowAction': 'Sync Offline Records',
          'pendingReviewNotice':
              'Saved safely. A reviewer will confirm this record.',
          'errNinAlreadyRegistered':
              'We could not complete registration with this ID number. If you registered before, please sign in or ask your extension agent for help.',
          'errNinLookupUnderReview':
              'We are checking your NIN details with NIMC. This is normal and usually clears within 48 hours.',
          'errNinNotFound':
              'This NIN number was not found. Please check each digit carefully and try again.',
          'errInvalidOtp':
              'That spoken code did not match. Ask for a fresh call and listen carefully.',
          'errInsufficientPoints':
              'Keep walking slowly around the edge of the field until at least 3 clear GPS points are saved.',
          'errBoundaryRequired':
              'Please walk and confirm this field boundary before starting a crop cycle here.',
          'errStageGateBlocked':
              'Finish and record the earlier farming step before moving to this stage.',
          'errCapabilityDisabled':
              'This action is not enabled for your account yet. Contact your organization supervisor.',
          'errOfflineQueued':
              'No network right now. Your record is saved safely on this device and will upload automatically when signal returns.',
          'errGeneric':
              'Something did not go through. Your saved work is safe; please try again when signal is steady.',
        },
        'ha': <String, String>{
          'appTitle': 'Farm Basics',
          'registerFarmerAction': 'Yi Rajistar Sabon Manomi',
          'walkBoundaryAction': 'Taka Iyakar Gona',
          'cropCycleTasksAction': 'Ayyukan Shuka',
          'syncNowAction': 'Aika Bayanan Da Aka Ajiye',
          'pendingReviewNotice':
              'An adana lafiya. Mai dubawa zai tabbatar da wannan bayani.',
          'errNinAlreadyRegistered':
              'Ba a iya kammala rajista da wannan lambar NIN ba. Idan ka yi rajista a baya, shiga asusunka ko ka nemi taimakon malamin gona.',
          'errNinLookupUnderReview':
              'Ana duba bayanan NIN dinka. Wannan al\'ada ce kuma yawanci yana kammaluwa cikin sa\'o\'i 48.',
          'errNinNotFound':
              'Ba a sami wannan lambar NIN ba. Duba kowace lamba sannan ka sake gwadawa.',
          'errInvalidOtp':
              'Lambar sirrin da aka karanta ba daidai ba ce. Nemi sabon kira ka saurara da kyau.',
          'errInsufficientPoints':
              'Ci gaba da zagaya gefen gona a hankali har a sami wuraren GPS akalla 3.',
          'errBoundaryRequired':
              'Da fatan za a taka a tabbatar da iyakar gonar kafin fara zagayen shuka.',
          'errStageGateBlocked':
              'Kammala matakin baya na aikin gona kafin wucewa zuwa wannan mataki.',
          'errCapabilityDisabled':
              'Ba a bude wannan damar ga asusunka ba tukuna. Tuntubi mai kula da kai.',
          'errOfflineQueued':
              'Babu hanyar sadarwa yanzu. An adana aikinka lafiya a na\'urar, zai tafi idan sadarwa ta dawo.',
          'errGeneric':
              'Wani abu bai tafi yadda ya kamata ba. An adana aikinka; sake gwadawa idan hanyar sadarwa ta gyaru.',
        },
        'yo': <String, String>{
          'appTitle': 'Farm Basics',
          'registerFarmerAction': 'Forukosile Agbe Tuntun',
          'walkBoundaryAction': 'Rin Yika Aala Oko',
          'cropCycleTasksAction': 'Awon Ise Oko',
          'syncNowAction': 'Fi Awon Akosile Ranṣẹ',
          'pendingReviewNotice':
              'A ti toju re lailewu. Oluyẹwo yoo fidi akosile yii mule.',
          'errNinAlreadyRegistered':
              'A ko le pari iforukosile pelu nomba ID yii. Ti o ba ti forukosile tele, jowo wole tabi beere iranlowo lowo osise ogbin re.',
          'errNinLookupUnderReview':
              'A n ṣe ayẹwo alaye NIN re pẹlu NIMC. Yoo pari laarin wakati 48.',
          'errNinNotFound':
              'A ko ri nomba NIN yii. Jowo ṣayẹwo nomba kookan ki o si tun gbiyanju.',
          'errInvalidOtp':
              'Koodu ohun ti o tẹ ko ba mu. Beere fun ipe tuntun ki o si tẹtisile daradara.',
          'errInsufficientPoints':
              'Maa rin rọra ni eti oko titi ti yoo fi gba ojuami GPS mẹta tabi ju bẹẹ lọ.',
          'errBoundaryRequired':
              'Jowo rin ki o si fidi aala oko yii mule ki o to bẹrẹ igbesẹ gbigbin.',
          'errStageGateBlocked':
              'Pari igbesẹ oko ti o ṣaaju ki o to tẹsiwaju si ipele yii.',
          'errCapabilityDisabled':
              'Iṣẹ yii ko ti ṣii fun akọọlẹ rẹ. Kan si alabojuto ẹgbẹ rẹ.',
          'errOfflineQueued':
              'Ko si nẹtiwọọki bayii. Akosile rẹ wa ni pamọ lori ẹrọ yii yoo si ranṣẹ nigba ti ifihan agbara ba pada.',
          'errGeneric':
              'Nkan kan ko lọ bi a ti rọ. Iṣẹ ti o fi pamọ wa lailewu; jowo tun gbiyanju.',
        },
        'ig': <String, String>{
          'appTitle': 'Farm Basics',
          'registerFarmerAction': 'Debanye Onye Oru Ugbo Ohuru',
          'walkBoundaryAction': 'Gaa Gburugburu Oke Ugbo',
          'cropCycleTasksAction': 'Oru Ihe Okuku',
          'syncNowAction': 'Zipu Ndekọ E Chekwara',
          'pendingReviewNotice':
              'E chekwala ya nke oma. Onye nyocha ga-akwado ndekọ a.',
          'errNinAlreadyRegistered':
              'Anyi enweghi ike imecha ndebanye aha site na nomba ID a. O buru na i debanyela aha mbu, biko banye ma o bu rịọ onye nkwado ugbo gi maka enyemaka.',
          'errNinLookupUnderReview':
              'Anyị na-enyocha NIN gị ugbu a. O na-ewekarị ihe ruru awa 48.',
          'errNinNotFound':
              'Ahụghị nomba NIN a. Biko lelee nomba nke obula nke oma wee nwaa ozo.',
          'errInvalidOtp':
              'Koodu ekwuputara adabaghi. Rịọ maka oku ohuru ma gee nti nke oma.',
          'errInsufficientPoints':
              'Gaa n\'ihu na-eje ije nwayọọ n\'akụkụ ugbo ruo mgbe GPS ruru ma o dịkarịa ala 3.',
          'errBoundaryRequired':
              'Biko gaa ma kwado oke ugbo a tupu i bido usoro okuku ohuru.',
          'errStageGateBlocked':
              'Mechaa nzọụkwụ ugbo bu ụzọ tupu i gaa n\'ọkwa a.',
          'errCapabilityDisabled':
              'Emebeghị ka oru a rụọ ọrụ na akauntu gi. Kpọọ onye nlekọta gi.',
          'errOfflineQueued':
              'Netwọk adịghị ugbu a. E chekwara ndekọ gi na ngwaọrụ a ma o ga-eziga mgbe netwọk lọtara.',
          'errGeneric':
              'Ihe emezughị nke oma. E chekwara oru gi nke oma; biko nwaa ozo.',
        },
        'pcm': <String, String>{
          'appTitle': 'Farm Basics',
          'registerFarmerAction': 'Register New Farmer',
          'walkBoundaryAction': 'Waka Round Farm Edge',
          'cropCycleTasksAction': 'Crop Work List',
          'syncNowAction': 'Send Saved Farm Work',
          'pendingReviewNotice':
              'We don keep am safe. Reviewer go check and confirm dis record.',
          'errNinAlreadyRegistered':
              'We no fit finish registration with dis ID number. If you don register before, abeg sign in or ask your extension agent for help.',
          'errNinLookupUnderReview':
              'We dey check your NIN details with NIMC. E normal and e dey clear within 48 hours.',
          'errNinNotFound':
              'We no see dis NIN number. Check every digit well well make you try again.',
          'errInvalidOtp':
              'Di code wey you hear no match. Ask make dem call again and listen well.',
          'errInsufficientPoints':
              'Continue to waka jeje round di farm edge till at least 3 GPS point enter.',
          'errBoundaryRequired':
              'Abeg waka confirm dis farm boundary before you start planting cycle.',
          'errStageGateBlocked':
              'Finish di first farm step first before you enter dis next stage.',
          'errCapabilityDisabled':
              'Dem never open dis feature for your account. Talk to your supervisor.',
          'errOfflineQueued':
              'Network no dey now. Your record don save well for phone and e go send sharp-sharp once network come back.',
          'errGeneric':
              'Something no go through now. Your saved work still intact; try again when network steady.',
        },
      };
}

class _FarmBasicsLocalizationsDelegate
    extends LocalizationsDelegate<FarmBasicsLocalizations> {
  const _FarmBasicsLocalizationsDelegate();

  @override
  bool isSupported(Locale locale) {
    return const <String>['en', 'ha', 'yo', 'ig', 'pcm']
        .contains(locale.languageCode.toLowerCase());
  }

  @override
  Future<FarmBasicsLocalizations> load(Locale locale) {
    return SynchronousFuture<FarmBasicsLocalizations>(
      FarmBasicsLocalizations(locale),
    );
  }

  @override
  bool shouldReload(_FarmBasicsLocalizationsDelegate old) => false;
}
