import 'package:dio/dio.dart';
import 'package:material_ui/material_ui.dart';

import 'boundary/boundary_walk_screen.dart';
import 'crop_cycles/crop_task_list_screen.dart';
import 'registration/registration_api_client.dart';
import 'registration/registration_wizard_screen.dart';
import 'sync/offline_sync_engine.dart';
import 'sync/outbox_database.dart';

void main() {
  runApp(const FarmBasicsApp());
}

/// Design System 2 offline-ready root shell (Sprint 0 WP-04 + Sprint 2 WP-21).
class FarmBasicsApp extends StatelessWidget {
  const FarmBasicsApp({
    super.key,
    this.apiBaseUrl = 'http://localhost:8080',
  });

  final String apiBaseUrl;

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
      home: _SyncSpikeHomePage(apiBaseUrl: apiBaseUrl),
    );
  }
}

class _SyncSpikeHomePage extends StatelessWidget {
  const _SyncSpikeHomePage({required this.apiBaseUrl});

  final String apiBaseUrl;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: FarmBasicsApp.creamCanvas,
      appBar: AppBar(
        backgroundColor: FarmBasicsApp.forestPrimary,
        foregroundColor: Colors.white,
        title: const Text('Farm Basics'),
      ),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              const Text(
                'Offline-first outbox ready (WP-04)',
                style: TextStyle(
                  color: FarmBasicsApp.ink900,
                  fontSize: 18,
                  fontWeight: FontWeight.w600,
                ),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 20),
              SizedBox(
                height: 52,
                child: FilledButton(
                  style: FilledButton.styleFrom(
                    backgroundColor: FarmBasicsApp.forestPrimary,
                  ),
                  onPressed: () {
                    final AppOutboxDatabase db = AppOutboxDatabase(
                      openPersistentOutboxExecutor(),
                    );
                    final DioRegistrationApiClient apiClient =
                        DioRegistrationApiClient(
                      dio: Dio(BaseOptions(baseUrl: apiBaseUrl)),
                    );
                    Navigator.of(context).push(
                      MaterialPageRoute<void>(
                        builder: (_) => SelfServiceRegistrationScreen(
                          database: db,
                          apiClient: apiClient,
                        ),
                      ),
                    );
                  },
                  child: const Text('Self-Service Registration'),
                ),
              ),
              const SizedBox(height: 12),
              SizedBox(
                height: 52,
                child: OutlinedButton(
                  onPressed: () {
                    final AppOutboxDatabase db = AppOutboxDatabase(
                      openPersistentOutboxExecutor(),
                    );
                    final Dio dio = Dio(BaseOptions(baseUrl: apiBaseUrl));
                    final OfflineSyncEngine syncEngine = OfflineSyncEngine(
                      database: db,
                      dio: dio,
                    );
                    Navigator.of(context).push(
                      MaterialPageRoute<void>(
                        builder: (_) => BoundaryWalkScreen(
                          database: db,
                          syncEngine: syncEngine,
                        ),
                      ),
                    );
                  },
                  child: const Text('Walk Field Boundary'),
                ),
              ),
              const SizedBox(height: 12),
              SizedBox(
                height: 52,
                child: OutlinedButton(
                  onPressed: () {
                    final AppOutboxDatabase db = AppOutboxDatabase(
                      openPersistentOutboxExecutor(),
                    );
                    final Dio dio = Dio(BaseOptions(baseUrl: apiBaseUrl));
                    final OfflineSyncEngine syncEngine = OfflineSyncEngine(
                      database: db,
                      dio: dio,
                    );
                    Navigator.of(context).push(
                      MaterialPageRoute<void>(
                        builder: (_) => CropTaskListScreen(
                          database: db,
                          syncEngine: syncEngine,
                        ),
                      ),
                    );
                  },
                  child: const Text('Crop Cycle Tasks'),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
