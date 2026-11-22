# Farm Basics — Farmer / Field Mobile App (`WP-04` Offline Sync Spike)

Flutter offline-first client implementing Sprint 0 **`WP-04`** (`S0.5.M1`–`S0.5.M3`) per `Doc/01B-ENG-Offline-Sync-and-Conflict-Resolution-Spec.md` and `Doc/01A-ENG-Technology-Stack-and-Tooling-Plan.md`.

## Verified Offline-Sync Pattern (for all subsequent Sprint 1+ writes)

1. **Client-owned UUIDv4 identity (`client_guid`):** Every offline write allocates its own `client_guid` locally at capture time along with UTC `client_recorded_at` and monotonic `monotonic_elapsed_ms` (`lib/sync/offline_sync_engine.dart`).
2. **Durable SQLite outbox (`drift`):** Every write persists to `local_outbox_mutation` (`lib/sync/outbox_database.dart`) with `sync_status = 'pending'` before any network call is attempted, matching all 20 columns in `Doc/01B-ENG` §1.2.
3. **Strict FIFO drain & bounded backoff:** `OfflineSyncEngine.drainOutbox()` reads pending items ordered ascending by `seq_id`, sends `Idempotency-Key: row.clientGuid`, keeps `sync_status = 'pending'` with `5s * 2^retry` (max `3600s`) backoff on `408`/`429`/`5xx`/network errors, and moves deterministic `4xx` rejections to `conflict_rejected`.
4. **Idempotent replay guarantee (`POST /v1/sync/spike-records`):** Replaying an identical payload with the same `Idempotency-Key` returns the winner's stored `201` response and identical `recordId` without inserting duplicate server rows (`platform.idempotency_keys` + `UNIQUE NULLS NOT DISTINCT` in `FarmBasics.Api`); reusing the key with a tampered payload fails closed with `409 IDEMPOTENCY_KEY_REUSED`.
5. **Fail-closed TLS certificate pinning (`D-14`):** `lib/network/certificate_pinning_adapter.dart` configures `Dio` with an `IOHttpClientAdapter` checking SHA-256 certificate digests when pins are provided (only permitting cleartext loopback when explicitly enabled for localhost testing).

## Local Commands

```powershell
cd apps/farmer-app
flutter pub get
dart run build_runner build --delete-conflicting-outputs
flutter analyze
flutter test
```
