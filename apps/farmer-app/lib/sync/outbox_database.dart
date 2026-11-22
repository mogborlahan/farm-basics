import 'dart:io';

import 'package:drift/drift.dart';
import 'package:drift/native.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';

part 'outbox_database.g.dart';

/// Offline mutation outbox table implementing Doc/01B-ENG §1.2.
@DataClassName('LocalOutboxMutation')
class LocalOutboxMutations extends Table {
  @override
  String get tableName => 'local_outbox_mutation';

  IntColumn get seqId => integer().autoIncrement()();
  TextColumn get clientGuid => text().unique()();
  TextColumn get entityType => text()();
  TextColumn get entityLocalId => text()();
  TextColumn get parentEntityLocalId => text().nullable()();
  TextColumn get httpMethod => text()();
  TextColumn get endpointPath => text()();
  TextColumn get payloadJson => text()();
  TextColumn get attachmentLocalPaths => text().nullable()();
  DateTimeColumn get clientRecordedAt => dateTime()();
  IntColumn get monotonicElapsedMs => integer()();
  TextColumn get actorIdentityId => text().nullable()();
  TextColumn get agentSessionId => text().nullable()();
  TextColumn get fobIdUsed => text().nullable()();
  DateTimeColumn get fobTappedAt => dateTime().nullable()();
  TextColumn get syncStatus => text().withDefault(const Constant('pending'))();
  IntColumn get retryCount => integer().withDefault(const Constant(0))();
  DateTimeColumn get nextRetryAt => dateTime().nullable()();
  TextColumn get lastErrorCode => text().nullable()();
  DateTimeColumn get serverReceivedAt => dateTime().nullable()();
}

/// Non-sensitive self-service registration wizard draft (WP-21 / Rule 1: never stores raw NIN or OTP).
@DataClassName('RegistrationDraft')
class RegistrationDrafts extends Table {
  @override
  String get tableName => 'registration_draft';

  IntColumn get id => integer()();
  IntColumn get stepIndex => integer().withDefault(const Constant(0))();
  TextColumn get phone => text().withDefault(const Constant(''))();
  TextColumn get email => text().withDefault(const Constant(''))();
  TextColumn get stateCode => text().withDefault(const Constant(''))();
  TextColumn get lgaName => text().withDefault(const Constant(''))();
  TextColumn get pendingIdentityId => text().nullable()();
  TextColumn get farmerId => text().nullable()();
  TextColumn get statusMessage => text().nullable()();
  TextColumn get lastErrorCode => text().nullable()();
  DateTimeColumn get lockoutUntilUtc => dateTime().nullable()();
  DateTimeColumn get updatedAt => dateTime()();

  @override
  Set<Column<Object>> get primaryKey => <Column<Object>>{id};
}

/// Drift-backed SQLite database for Farm Basics offline-first mutation capture and registration state.
@DriftDatabase(tables: [LocalOutboxMutations, RegistrationDrafts])
class AppOutboxDatabase extends _$AppOutboxDatabase {
  /// Opens the database with an explicit [QueryExecutor] (supports in-memory tests).
  AppOutboxDatabase(super.e);

  @override
  int get schemaVersion => 2;

  @override
  MigrationStrategy get migration => MigrationStrategy(
        onCreate: (Migrator m) => m.createAll(),
        onUpgrade: (Migrator m, int from, int to) async {
          if (from < 2) {
            await m.createTable(registrationDrafts);
          }
        },
      );

  /// Enqueues an offline mutation into the durable outbox.
  Future<int> enqueueMutation(LocalOutboxMutationsCompanion entry) {
    return into(localOutboxMutations).insert(entry);
  }

  /// Returns pending outbox rows in strict FIFO ascending [seqId] order.
  Future<List<LocalOutboxMutation>> getPendingBatch({int limit = 25}) {
    return (select(localOutboxMutations)
          ..where((t) => t.syncStatus.equals('pending'))
          ..orderBy([(t) => OrderingTerm.asc(t.seqId)])
          ..limit(limit))
        .get();
  }

  /// Marks an outbox mutation as synced and persists the server timestamp.
  Future<void> markSynced({
    required int seqId,
    required DateTime serverReceivedAt,
  }) {
    return (update(localOutboxMutations)..where((t) => t.seqId.equals(seqId)))
        .write(
      LocalOutboxMutationsCompanion(
        syncStatus: const Value('synced'),
        serverReceivedAt: Value(serverReceivedAt),
        lastErrorCode: const Value(null),
      ),
    );
  }

  /// Records a transient transport/server error while keeping the item pending.
  Future<void> markRetryableFailure({
    required int seqId,
    required int nextRetryCount,
    required DateTime nextRetryAt,
    required String errorCode,
  }) {
    return (update(localOutboxMutations)..where((t) => t.seqId.equals(seqId)))
        .write(
      LocalOutboxMutationsCompanion(
        syncStatus: const Value('pending'),
        retryCount: Value(nextRetryCount),
        nextRetryAt: Value(nextRetryAt),
        lastErrorCode: Value(errorCode),
      ),
    );
  }

  /// Moves a deterministic 4xx payload/idempotency rejection to conflict_rejected.
  Future<void> markConflictRejected({
    required int seqId,
    required String errorCode,
  }) {
    return (update(localOutboxMutations)..where((t) => t.seqId.equals(seqId)))
        .write(
      LocalOutboxMutationsCompanion(
        syncStatus: const Value('conflict_rejected'),
        lastErrorCode: Value(errorCode),
      ),
    );
  }

  /// Fetches a single outbox mutation by its client-generated UUID.
  Future<LocalOutboxMutation?> getByClientGuid(String clientGuid) {
    return (select(localOutboxMutations)
          ..where((t) => t.clientGuid.equals(clientGuid)))
        .getSingleOrNull();
  }

  /// Loads the persisted singleton self-service registration wizard draft (if any).
  Future<RegistrationDraft?> loadRegistrationDraft() {
    return (select(registrationDrafts)..where((t) => t.id.equals(1)))
        .getSingleOrNull();
  }

  /// Saves or updates non-sensitive self-service registration wizard progress.
  Future<void> saveRegistrationDraft(RegistrationDraftsCompanion draft) {
    return into(registrationDrafts).insertOnConflictUpdate(draft);
  }

  /// Clears any persisted self-service registration draft.
  Future<void> clearRegistrationDraft() {
    return (delete(registrationDrafts)..where((t) => t.id.equals(1))).go();
  }
}

/// Opens the persistent on-device SQLite outbox database inside app documents.
LazyDatabase openPersistentOutboxExecutor({
  String filename = 'farm_basics_outbox.sqlite',
}) {
  return LazyDatabase(() async {
    final Directory dir = await getApplicationDocumentsDirectory();
    final File dbFile = File(p.join(dir.path, filename));
    return NativeDatabase.createInBackground(dbFile);
  });
}
