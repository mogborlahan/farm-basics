import 'package:material_ui/material_ui.dart';

void main() {
  runApp(const FarmBasicsApp());
}

/// Design System 2 offline-ready root shell (Sprint 0 WP-04 walking skeleton).
class FarmBasicsApp extends StatelessWidget {
  const FarmBasicsApp({super.key});

  // Doc/Farm-Basics-Design-System-2.md §23 tokens.
  static const Color creamCanvas = Color(0xFFF4F1EA);
  static const Color forestPrimary = Color(0xFF126A3A);
  static const Color ink900 = Color(0xFF1F2A22);

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Farm Basics',
      theme: ThemeData(
        scaffoldBackgroundColor: creamCanvas,
        colorScheme: ColorScheme.fromSeed(
          seedColor: forestPrimary,
          surface: creamCanvas,
        ),
        useMaterial3: true,
      ),
      home: const _SyncSpikeHomePage(),
    );
  }
}

class _SyncSpikeHomePage extends StatelessWidget {
  const _SyncSpikeHomePage();

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: FarmBasicsApp.creamCanvas,
      appBar: AppBar(
        backgroundColor: FarmBasicsApp.forestPrimary,
        foregroundColor: Colors.white,
        title: const Text('Farm Basics'),
      ),
      body: const Center(
        child: Padding(
          padding: EdgeInsets.all(24),
          child: Text(
            'Offline-first outbox ready (WP-04)',
            style: TextStyle(
              color: FarmBasicsApp.ink900,
              fontSize: 18,
              fontWeight: FontWeight.w600,
            ),
            textAlign: TextAlign.center,
          ),
        ),
      ),
    );
  }
}
