# Farm Basics — Pilot Release & TLS Certificate Pinning Runbook (WP-B1 / S11.2.M3)

**Authority:** `Doc/01A-ENG-Technology-Stack-and-Tooling-Plan.md` (§2.1, §7.2), `Doc/04-ENG-Security-Architecture-and-Threat-Model.md`, `Doc/07-ENG-CI-CD-and-Environment-Strategy.md`, `codemagic.yaml`, `apps/farmer-app/lib/network/certificate_pinning_adapter.dart`  
**Audience:** Release operator (solo founder), on-call responder

---

## 1. Purpose & Core Rule

`apps/farmer-app/lib/network/certificate_pinning_adapter.dart` implements `createPinnedHttpClientAdapter`, which fails closed (`badCertificateCallback` returns `false`) unless the server X.509 certificate SHA-256 fingerprint matches `allowedSha256Pins` (or `allowInsecureLocalhost` is explicitly enabled on `127.0.0.1` / `localhost` / `::1`).

**Non-negotiable dual-pin invariant:** Never ship a production or pilot mobile build carrying only a single SHA-256 pin. Every mobile release built via `codemagic.yaml` (`farmer-app-android-internal` or `farmer-app-ios-testflight`) must embed:
1. `CERT_PINS_PROD_PRIMARY` — current active origin/edge certificate SHA-256 SPKI/cert digest, AND
2. `CERT_PINS_PROD_BACKUP` — pre-generated standby keypair/certificate SHA-256 digest stored offline / sealed with SOPS+`age`.

Rotating a live server TLS certificate before clients carry the next backup pin breaks offline-first field devices unable to update over poor rural links.

---

## 2. Pre-Release Gate Checklist (Pilot `mobile-vX.Y.Z` Tag)

1. Confirm `Plan/Farm-Basics-Implementation-Sprints.md` Flutter and backend verification suites pass (`flutter analyze`, `flutter test`, `dotnet test`).
2. Verify all 5 locale catalogs in `apps/farmer-app/lib/l10n/farm_basics_localizations.dart` (`en-NG`, `ha`, `yo`, `ig`, `pcm`).
   - Per `Doc/16A-OPS` §7, **`en-NG` is normative**. Do **not** instruct field agents to rely on non-English translations (`ha`, `yo`, `ig`, `pcm`) in farmer-facing scripts until `isNativeSpeakerVerified` native-speaker sign-off is recorded in `Doc/DECISIONS.md` and `FarmBasicsLocalizations`.
3. Extract and compare both production pins before tagging:
   ```bash
   openssl x509 -in current-prod.crt -outform DER | openssl dgst -sha256 -hex
   openssl x509 -in backup-standby.crt -outform DER | openssl dgst -sha256 -hex
   ```
   Confirm `CERT_PINS_SHA256` passed to `codemagic.yaml` is the lowercase comma-separated hex pair (`<primary64hex>,<backup64hex>`) matching the normalization logic in `certificate_pinning_adapter.dart` (`replaceAll(':', '').replaceAll('-', '').toLowerCase()`).
4. Trigger release build by pushing an annotated git tag `mobile-v<major>.<minor>.<patch>`:
   - Android (`farmer-app-android-internal`): builds signed `.aab`, uploads as **draft** on Google Play `internal` track with `rollout_fraction: 0.1` (10% pilot cohort).
   - iOS (`farmer-app-ios-testflight`): builds signed `.ipa` and uploads to TestFlight internal pilot group.

---

## 3. Routine Certificate Rotation Procedure (Zero Field Outage)

Carry out rotation in two separated windows so field tablets have time to receive the backup pin before the old primary expires:

1. **Step A (T-30 days before cert expiry — Ship Backup Pin):**
   - Generate new keypair + CSR/certificate (`cert-N+1`) and compute its SHA-256 fingerprint.
   - Update Codemagic secrets so `CERT_PINS_PROD_PRIMARY = cert-N`, `CERT_PINS_PROD_BACKUP = cert-N+1`.
   - Cut `mobile-v*` pilot/production release and wait until telemetry confirms pilot devices are on the new app version.
2. **Step B (T-7 days — Cut Over Edge/Caddy TLS Cert):**
   - Deploy `cert-N+1` on the origin/edge while both `cert-N` and `cert-N+1` remain pinned in active app builds.
   - Verify HTTPS health checks (`/healthz/live`, `/healthz/ready`) and `createPinnedHttpClientAdapter` handshake from a staged Android device.
3. **Step C (Post-Cutover — Retire Old Primary & Stage Next Backup):**
   - Generate fresh standby `cert-N+2`.
   - Next mobile release shifts pins to `CERT_PINS_PROD_PRIMARY = cert-N+1`, `CERT_PINS_PROD_BACKUP = cert-N+2`.

---

## 4. Emergency Pin-Mismatch Response (Revoked / Compromised Key)

1. Immediately deploy `cert-backup` (already embedded in all shipped clients as `CERT_PINS_PROD_BACKUP`) on the VM edge/Caddy (`infra/docker-compose.yml` + `infra/caddy/`). Because every compliant build carries the backup pin, existing field apps reconnect without requiring an emergency store review.
2. Within 24 hours, generate a replacement `cert-new-backup`, update Codemagic pins (`primary = cert-backup`, `backup = cert-new-backup`), and push a hotfix `mobile-v*` release so dual-pin redundancy is restored.
3. Record any emergency KMS/Caddy intervention under the break-glass workflow (`infra/provisioning/BREAK-GLASS-DRILL-RUNBOOK.md`, `POST /v1/admin/break-glass`).
