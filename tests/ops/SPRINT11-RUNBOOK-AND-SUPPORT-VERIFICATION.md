# Sprint 11 — End-to-End Runbook, Agent Manual & Support Script Verification (WP-B3 / S11.4.M1)

**Verification Date:** 2026-11-23  
**Scope:** Cross-check `Doc/09-ENG-Disaster-Recovery-Runbook.md`, `Doc/15-OPS-Extension-Agent-Field-Manual.md`, `Doc/16-OPS-Customer-Support-Playbook.md`, and `Doc/16A-OPS-Error-Code-and-Plain-Language-Copy-Catalog.md` against the implemented backend endpoints, SQL migrations, infrastructure configs, and `apps/farmer-app` UI/localization.

---

## 1. DR Runbook Verification (`Doc/09-ENG-Disaster-Recovery-Runbook.md`)

| Doc 09 Procedure | Shipped Implementation Target | Verification Status & Notes |
|---|---|---|
| §3 & §4.1 PostgreSQL continuous WAL + PITR (`wal-g`, ≤5 min RPO) | `infra/backup/walg-backup.sh`, `infra/backup/restore-pitr.sh`, `infra/docker-compose.yml` (`postgres` archive command) | **Verified in code/compose config.** Physical single-VM dry-run restore (`WP-A2`: `S10.3.M1`–`M2`) remains open under `F-32` pending staging VM availability. |
| §3 Encrypted `.env` backup via SOPS + `age` | `infra/.sops.yaml`, `infra/.env.sops.yaml`, `Plan/tools/archive-specs.ps1` | **Verified.** Offline `age` ceremony documented in `infra/provisioning/BREAK-GLASS-DRILL-RUNBOOK.md` (`Doc/04A-ENG` §11.7). |
| §4.1 Post-recovery reconciliation verification | `PlatformMetrics.InvariantReconciliationOpenAlerts`, `InvariantCanaryService` (`/v1/admin/observability/canaries`, `/metrics`) | **Verified by integration tests** (`Sprint10ObservabilityCanaryIntegrationTests`). Must show `allInvariantsHealthy = true` before declaring P1 resolved. |
| §4.5 Third-party NIMC/Termii/PayGuard degradation | `SyncSpikeEndpoint`, `SelfServiceRegistrationEndpoints` review queue (`NIN_LOOKUP_UNDER_REVIEW`), `PayGuardWebhookEndpoints` + manual fallback runbooks | **Verified by integration tests** (`Sprint2`, `Sprint4`, `Sprint6`). |

---

## 2. Extension Agent Field Manual Verification (`Doc/15-OPS-Extension-Agent-Field-Manual.md`)

| Doc 15 Rule / Step | Shipped Implementation Target | Verification Status & Notes |
|---|---|---|
| §2 Rule 2–3: Never reuse agent phone or store farmer NIN on paper/device | `identity.identity` (`nin_hash` HMAC-SHA256 only, never raw NIN stored or logged), `SensitiveDataRedactionTests` | **Verified.** Raw NIN never persists in PostgreSQL, sync outbox, logs, or `/v1/admin/observability/pilot-cohort`. |
| §2 Rule 4–5 & §5: Hardware fob bound to present farmer; lost fob quarantine | `POST /v1/agent/farmers/register`, `POST /v1/agent/fobs/quarantine`, `003_sprint3_agent_and_fobs.sql` | **Verified by integration tests** (`Sprint3AgentAndFobIntegrationTests`) enforcing `ADR-010` revocation-first quarantine. |
| §2 Rule 6: Honest flagged operation log allowed & preserved (anti-gaming) | `POST /v1/crop-cycles/{id}/logs` (`CropCycleEndpoints`), `FarmBasicsLocalizations` plain-language messages, `PlatformMetrics.PilotOperationLogsTotal` | **Verified.** Out-of-window honest operations save with `status = "flag"` rather than rejection, and telemetry records count without PII. |
| §2 Rule 7: Field boundary must be GPS-walked (never tapped polygon) | `ProductionUnitEndpoints` (`/v1/production-units/boundary-sessions/*`), `BoundaryWalkScreen`, `BoundaryWalkController` | **Verified.** `< 3` distinct GPS points returns HTTP `422 INSUFFICIENT_POINTS`; self-intersecting / irregular walks flag `review_required`. |
| §4.2 Steps 1–7: Self/Agent registration, voice OTP, community confirmation & teach-back attestation | `SelfServiceRegistrationEndpoints`, `AgentOnboardingEndpoints`, `RegistrationWizardScreen`, `FarmBasicsLocalizations` | **Verified.** Attestation (`teach_back_passed`) recorded in `identity.registration_attestation` and aggregated zero-PII at `/v1/admin/observability/pilot-cohort`. |

---

## 3. Customer Support Playbook & Copy Catalog Verification (`Doc/16-OPS` & `Doc/16A-OPS`)

| Doc 16 / 16A Requirement | Shipped Implementation Target | Verification Status & Notes |
|---|---|---|
| `16-OPS` §2.2 & `Doc/02E-ENG`: Narrow `CustomerSupportAgent` role (`unlock`, `otp-resend`, `recovery-ticket`, zero escrow/tier overrides) | `SupportOpsEndpoints` (`POST /v1/support/accounts/{id}/unlock`, `POST /v1/support/accounts/{id}/otp-resend`, `POST /v1/support/recovery-tickets`), `008_sprint9_harvest_support.sql` | **Verified by integration tests** (`Sprint9HarvestOpsAndSupportIntegrationTests`): `CustomerSupportAgent` receives `403 PERMISSION_DENIED` on ledger/escrow/master-data endpoints. |
| `16A-OPS` §2–§6 Error-code copy parity | `FarmBasicsLocalizations.errorMessage(...)` (`en-NG` normative baseline for `NIN_ALREADY_REGISTERED`, `NIN_LOOKUP_UNDER_REVIEW`, `NIN_NOT_FOUND`, `INVALID_OTP`, `INSUFFICIENT_POINTS`, `BOUNDARY_REQUIRED`, `STAGE_GATE_BLOCKED`, `CAPABILITY_DISABLED`, `OFFLINE_QUEUED`) | **Verified by Flutter widget/unit tests** (`apps/farmer-app/test/localizations_test.dart`). |
| `16A-OPS` §7 Native-speaker verification gate | `FarmBasicsLocalizations.isNativeSpeakerVerified` (`true` for `en-NG`; `false` with `pending_native_speaker_signoff_doc_16a_s7` for `ha`, `yo`, `ig`, `pcm`) | **Verified in code & tests.** Release runbook (`RELEASE-AND-CERT-PINNING-RUNBOOK.md` §2) blocks reliance on non-English farmer scripts until human native-speaker sign-off is logged. |
| `16-OPS` §3 Inbound voice IVR & WhatsApp policy | **External / Pending Decision (`P13` WhatsApp / Inbound IVR)** | **Explicitly flagged non-software gap.** Outbound voice OTP/call confirmation is wired via `ISmsProvider`; inbound support phone tree & WhatsApp channel stay gated per `16-OPS` §18 gap 2 & §20. |
