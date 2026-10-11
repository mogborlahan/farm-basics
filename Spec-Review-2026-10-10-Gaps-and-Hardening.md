# Farm Basics — Specification Gap & Hardening Review

**Date:** 2026-10-10
**Scope:** the whole `Doc/` specification set (34 Markdown/HTML docs; binaries excluded).
**Method:** five parallel cluster reviews (Product/PRD, Engineering core, Security/infra/ops, Legal/platform-ops, QA/design) against a world-scenario + hardening rubric (offline/connectivity, device/accessibility, power, payments, fraud, identity/KYC, agronomy/climate, logistics, regulation, inclusion, scale/seasonality, data quality; and hardening dimensions security / privacy · reliability · observability · correctness · fraud-controls · accessibility · ops-release · cost/scale / legal).
**Status:** first-pass exhaustive audit. This document is a *proposal/record* — no source-of-truth file was edited.

## Verification legend

Every finding below carries a status. Sub-agent claims were spot-checked against the actual files because the fan-out produced a measurable hallucination rate.

- **[verified]** — I reproduced the claim directly in the file (grep / read).
- **`[reported]`** — from a cluster pass; not individually re-verified. Treat as a lead to confirm, not a fact.
- **[disputed]** — checked and found false as stated; kept here so the record is honest.

**Hallucinations found and corrected during verification (do not act on the earlier phrasing):**
- [disputed] "`BRD.md` is byte-corrupted mojibake." → U+FFFD count is **0**; the file decodes cleanly. *(The real defect is different: the file is **truncated mid-sentence** — see X2.)*
- [disputed] "Duplicate `F-CP-01` in `BRD.md`." → **0** occurrences of `CP-01`. Not present.
- [disputed] "`unified-platform-spec.md` states it no longer exists / supersedes itself." → No such text. *(It is still a genuine superseded-revision problem — see X1 — but not for that reason.)*

---

## Part A — Cross-cutting / systemic findings (highest leverage)

- **X1 [Major] [verified] Superseded specs now sit inside `Doc/` beside the current set.** The recent flatten moved the old "re-download" revision into `Doc/`, so `Doc\` now holds **three documents that conflict with the authoritative set**: `BRD.md` (v1) vs `BRD-v2-Nigeria-Farm-Platform.md`; `unified-platform-spec.md` (13 domains, SQL-Server-era, pre-Consolidation) vs `Part A`/`Part B`; `admin-operations-console-spec.md` (older RBAC/role model, "thirteen domains") vs `Part B`. `unified-platform-spec.md` alone still says "**thirteen** previously-independent domain specs"; Part A's canonical count is **eleven**; the console spec and Part B disagree on the role model. → Decide one authority per pair and move the losers to `Archived/` (or add a `SUPERSEDED` banner + freeze). This is the single highest-risk correctness/compliance hazard in the set today.
- **X2 [Major] [verified] `Doc\BRD.md` is truncated mid-sentence.** It ends mid-clause inside acceptance criterion `AC-AD-04` ("…when]."), so the v1 BRD is incomplete regardless of the supersede question. → Archive it; do not cite it. (Verified: no mojibake; tail is a hard cutoff.)
- **X3 [Major] [verified] `AGENTS.md` still points at the pre-flatten folder taxonomy.** It references `Doc/02-Engineering/01A-ENG-…`, `Doc/02-Engineering/07-ENG-…`, `Doc/05-Design/Farm-Basics-Design-System-2.md`, `Doc/05-Design/farm-basics-styleguide-2.html`, and the `Doc/01-Business … 06-Operations` taxonomy. After the flatten, **none of those paths exist** (`Doc\05-Design` and `Doc\02-Engineering` are gone; the files are flat). → Update `AGENTS.md` to the flat paths (the design-system row, the stack-plan citation, and the CI/CD cite). Cheap, and it stops every future agent reading stale paths.
- **X3b [Major] [verified] One superseded dependency is still cited by a live QA doc.** `Doc\13-QA-UAT-and-Pilot-Protocol.md:241` references `unified-platform-spec.md` Appendix A for research-ethics processes — i.e., it depends on the superseded file from X1. → Repoint to the current source or the partner's own ethics process.
- **X3 [Major] [reported] No platform-wide offline write/sync conflict policy.** Each surface states its own degradation, but there is no single sync contract (queue + merge rule + conflict UX) applied consistently. Corroborated by the eng cluster's finding that `01-ENG §8.1` says "surface to the farmer" with no delta/cursor sync API. → Specify one sync model (checkpoint `updated_at` pull + client GUID dedupe + explicit conflict resolution).
- **X4 [Major] [reported] Localization is absent at the data layer.** `02-ENG`/`03-ENG` templates/`knowledge_article`/`crop_profile`/`chemical_product` carry no `locale`, and there is no content-language scope decision, although the target audience is majority low-literate and multilingual. Corroborated by `14-QA §3.1.1` ("Pending localization scope decision") and `PRD Master §23` (open). → Add a locale column/translation table + `Accept-Language` negotiation and close the language-scope question.
- **X5 [Major] [reported] Consent is asserted but not modelled as an entity.** Land-history sharing, research/ML use and marketing are all "consent-based and revocable," yet no consent-record entity/UI/withdrawal path is described in the ops/legal surface the way it is in `02-ENG` (`identity.consent_grant` exists in schema and is exercised by Doc 12). → Wire the schema's consent model through Part A/Part B/Privacy Policy so capture/withdraw/audit are one mechanism.
- **X5 [Major] [reported] Retention is prose, never enforced.** Retention is described across docs but no retention/purge job exists in the compliance console, and append-only audit/notification payloads (`audit_log_entry.payload`, `events_outbox.payload`, `NotificationDispatch`) may hold PII indefinitely. → Define one retention schedule, enforce it, treat append-only PII as a lifecycle-managed class.
- **X5 [Major] [reported] Breach/incident machinery behind the 72-hour NDPA clock is missing.** The clock is asserted (11-LEGAL §4.12) with no detection/triage object, processor flow-down, or NDPC filing tracker. → Add a `DataBreachIncident` workflow + processor obligations + comms runbook.
- **X7 [Major] [reported] Consent-management / DSAR identity verification missing.** No workflow verifies a requester's identity before export/erasure, and the DSAR enum (`Access/Erasure/Rectification`) omits NDPA's other rights. → Add requester verification + the full rights set.
- **X8 [Major] [reported] Sub-processor governance absent.** PayGuard, SMS/voice, labs, hosting are relied on with no DPA/audit-rights/international-transfer obligations stated. → Sub-processor register + mandatory DPAs.

---

## Part B — Critical findings

- **C1 [verified] The CEA engineering delta is not merged — two conflicting schemas are "current."** `Doc\02B-ENG-CEA-Schema-and-API-Delta.md` §22 says its changes "**supersede the Volume E text** and should be folded into the PRD on its next revision," and it renames `Field`→`ProductionUnit`, adds `growing_medium_class` (`soil_based|soilless`), `capture_method`, `system_type`, `tier_count`, `area_basis` — but **none of those terms appear in `02-ENG` or `03-ENG`**. An implementer starting from Doc 02 ships the pre-CEA model. → Merge 02B into `02-ENG`/`03-ENG` before Phase 0/1, or banner it explicitly non-normative. *(Note: 02B's `growing_medium_class` already answers the medium-gating question F-17 raised in the Volume E review — the PRD fix should reuse it, not invent a parallel `MediumType`.)*
- **C2 [verified] Pin-drop / address / centroid production units are unrepresentable in the schema.** `02-ENG §4.1` has `boundary geography(Polygon,4326) NOT NULL` and `chk_area_positive CHECK (area_square_meters > 0)`, yet `02B` A-3/A-5 require units with no polygon and farmer-declared area. → Make `boundary` nullable with a centroid fallback, add `capture_method` + `area_basis`, and require the region key (`lga_id`) when there is no polygon.
- **C3 [reported] Financial schema cannot express partial/split settlement.** Per the eng cluster: no `split`/partial-release state on the escrow entity, no reservation amount, and the `journal_entry` deferred balance trigger is vacuous if zero lines are inserted (empty/unbalanced entries can persist); reversal linkage is free-text. → Add explicit partial/split states + amounts, make the balance trigger fail on empty entries, and type the reversal link. *(Product cluster independently flagged Volume C as "full-release-only", risking a farmer's whole payment on a partial delivery — same root.)*
- **C4 [reported] Marketplace/escrow launch is gated on an unresolved CBN licensing position with no fallback.** `10-LEGAL §3` calls it "the central regulatory question"; `11-LEGAL §3.5.2` already concedes the escrow clause may be inadequate. → Do not ship escrow until counsel fixes the position; make the non-escrowed mode the default launch posture and pre-decide the path if a licence is refused.
- **C5 [reported] NDPA DPIA is preliminary and the sensitive-data basis is unsigned.** NIN storage, biometric liveness, and precise geolocation are high-risk; NIN is sensitive data under the NDPA 2023 and NIMC Act. Evidence: `10-LEGAL §4.5–4.7`; `04 §7.2` treats NIN tokenisation as optional. → Mandate provider-token NIN (no recoverable NIN), complete a formal DPIA, obtain counsel/NIMC sign-off before production data.
- **C6 [reported] Data residency / cross-border transfer is undecided.** No hosting-region commitment exists while off-region backups (`09-ENG §3`) and sensitive data are in scope — potentially a breach of the very NDPA commitment the set defers. → Decide in-country hosting or execute a valid transfer mechanism; fold backup-archive geography into the same decision.
- **C7 [reported] Erasure vs no-hard-delete/immutable-ledger is unresolved with no mechanism.** The platform mandates deactivate-never-delete and immutable ledgers; NDPA grants erasure. `11-LEGAL §4.9` and `Part B §16.2/§21` both leave it open. → Pre-specify erasure-by-pseudonymisation so both invariants coexist.
- **C8 [reported] A `Regulator` role can reach farmer PII with no lawful basis, scope, or console.** `Part A §2.1` puts `Regulator` in `RoleType`; it is absent from Part B's 13 admin roles and has no permission set. → Remove from the enum until a read-only, audited, purpose-limited design + legal basis exists.
- **C9 [reported] Backups are not client-side encrypted and the archive target has no immutability.** `09-ENG §3` mentions SOPS/`age` only for `.env`; WAL/base-backup encryption, object-lock/WORM, and a non-delete credential are unstated. → Encrypt every backup client-side before upload, hold the key under dual-custody, enable object-lock + write-only credential.
- **C10 [reported] The `age` private key is a single unescrowed master secret.** It decrypts the whole `.env`, break-glass artifacts, and (per `04 §7.2`) the NIN column key, with no rotation/escrow/custody defined. → Name a rotation cadence, escrow under dual-control, and separate the backup key from the deployment key.
- **C11 [reported] Volume B's pest/disease classifier has no accuracy floor or Nigerian-crop validation.** Its verdict shapes safety perception, yet no validation evidence, feedback loop, or retraining requirement exists. → Require a published accuracy bar + Nigerian-crop validation before the Phase-3 pilot, and a confirmed/rejected feedback pipeline.
- **C12 [reported] Master §30 authoritative Geography seed is unresolved** while `near-me`, outbreak-region, and vendor-region logic all depend on it. → Resolve the seed source (or stand up an admin-curated seed) before Phase 1.
- **C13 [reported] `02-ENG §3.1` uniqueness/trust constraints are defective:** `role_profile UNIQUE(identity_id, role_type, linked_organization_id)` treats NULLs as distinct (duplicate farmer/agent profiles insert), and `trust_profile.identity_id` is `PK NOT NULL`, making org-only trust impossible. → Use `UNIQUE NULLS NOT DISTINCT` and split/allow-null trust subject with an XOR check. *(The team clearly knows `NULLS NOT DISTINCT` — Doc 12 §§3.1–3.3 test it for idempotency and consent — so this is a schema-level miss, not a knowledge gap.)*

---

## Part C — Major findings by domain

### Legal / compliance / privacy
- **M1 [reported]** `11-LEGAL §3.13` liability cap (12 months' commission) is conceded "manifestly inadequate" for agrochemical-injury claims; `§3.14` broad indemnity against subsistence farmers; `§3.16` governing law/dispute forum unspecified ("arbitrate in Lagos over ₦50,000" = denial of remedy). → Narrow all three together; adopt an accessibility-aware tiered dispute path.
- **M2 [reported]** `11-LEGAL §3.1` "continued use = acceptance" for agent-assisted low-literate users fails informed consent. → Implement the §5.1 plain-language/audio/agent-script package and shift the legal basis to contract/legitimate-interest where possible.
- **M3 [reported]** ML-training licence over submitted farm photos is unmade (`11-LEGAL §5.3 item 2`), yet `§3.9` grants a broad content licence. → Decide and disclose (or exclude) training use explicitly.
- **M4 [reported]** `10-LEGAL §4.8/4.9`: DPO not designated; NDPC "Major Importance" registration/annual audit not actioned; no ROPA. → Assign a DPO and budget the NDPC cycle before launch.
- **M4 [reported]** Ten `11-LEGAL §5.3` drafting items (age/capacity, governing language, transfer, forum, etc.) remain open — each an enforceable-contract defect today.
- **M5 [reported]** `Part B` uses "NDPR" throughout though the governing law is the NDPA **2023**; obligations must be verified against the 2023 Act. → Rename and re-verify.
- **M6 [reported]** No VAT/WHT treatment on marketplace commission or produce sales. → Add tax postings before enabling live payments.
- **M7 [reported]** No consent entity anywhere despite consent-gated sharing. (Overlaps X7.)

### Security / infrastructure / operations
- **M3 [reported]** Security logs live only on the single VM; a breach lets the attacker delete them. → Ship security-relevant logs off-VM to an append-only sink with a write-only credential.
- **M4 [reported]** No dead-man's-switch / independent paging — a total VM/Alertmanager failure yields silence. → External heartbeat + a second paging channel.
- **M5 [reported]** Webhook HMAC has no replay defence (no timestamp window/nonce/dedupe) and the provider event-id type mismatches the uuid dedup key. → Signed timestamp + per-event dedupe + text key.
- **M6 [reported]** CI has no SBOM/signing/provenance despite Doc 10's SLSA/sigstore requirements; images neither signed nor verified at deploy. → SBOM + cosign signing + verify-at-deploy as blocking gates.
- **M10 [reported]** DR claims rebuild "from IaC" but Terraform is explicitly deferred → the "provision a new VM" path has no code. → Ship provider-agnostic provisioning IaC now or downgrade the RTO claim and mark the manual path untested.
- **M3 [reported]** Blue/Green runs two app containers while Hangfire recurring jobs live inside the app — recurring jobs can double-run. → Singleton job runner or explicit distributed-lock assertion + single-execution test.
- **M4 [reported]** Solo-builder roles: DR requires "Incident Commander + second senior engineer"; the release process requires a "designated approver" — no second person exists. → Define the recorded self-review substitute + external/retainer responder.
- **M5 [reported]** Break-glass is single-holder (founder); Type 2 dormant. Founder unavailability = no recovery. → Name a co-holder / second-location recovery code.
- **M6 [reported]** SMS-OTP is both admin MFA and the incident-comms channel — exposed to SIM swap/carrier outage in Nigeria. → TOTP/passkey primary, SMS fallback, non-SMS comms.
- **M7 [reported]** 30-day stateless JWT "revoked immediately on suspension" is impossible without introspection/denylist/backchannel logout; no JWT `alg` pinning stated. → Pin RS256/ES256, validate claims, add revocation.
- **M8 [reported]** Edge rate limiting keyed on IP only, but Nigerian mobile is heavily CGNAT-shared (over-block innocents, under-block rotators); Serilog redaction names only `nin/password/rawNin` (phone/BVN/account/GPS/email uncovered, spans/metrics unredacted). → Key coarse limits on IP+ASN with app-level identity limits authoritative; move to a loggable-field allowlist applied to spans/metrics too.
- **M9 [reported]** File-upload malware scanning named but uninstantiated; no upload endpoint, size/MIME caps, or lifecycle (liveness/photos are base64 in JSON or bare `photoUrls`). → Add scanner + quarantine + signed object storage + caps + EXIF stripping.
- **M10 [reported]** DR depends on the operator having power+internet during a regional event; no offline/printable runbook or second location. → Maintain a printable runbook + sealed credential with a secondary; treat missed restore drills as findings.
- **M11 [reported]** Only 2 backup copies (production + one off-region) — not 3-2-1; no third copy or accepted risk documented.

### Engineering / data
- **M12 [reported]** `02-ENG §6.1` farmer_certification allows both holder columns NULL (no XOR) and stores scope as unvalidated `uuid[]`; `regional_outbreak` uniqueness keys on a timestamp; `chemical/crop/knowledge` lack `source/reviewed_by/version` provenance columns although `06-DATA` mandates sign-off. → Add XOR checks, window-keyed uniqueness, provenance columns.
- **M13 [reported]** Provider webhook event IDs are strings but `platform.idempotency_keys` id type is `uuid`; idempotency TTL (24h/72h) is shorter than documented offline-queue windows; `Idempotency-Key` required only on scarce-reservation endpoints, not all money-state endpoints. → Type provider keys as text, reconcile TTL with offline windows, extend idempotency to all money endpoints.
- **M14 [reported]** Money is NGN-only `numeric(14,2)` (good, no floats) but `trade.offer.currency` exists with no FX table/guard. → Drop the column or add currency + rate + same-currency check.
- **M15 [reported]** No localization/i18n columns (see X4). No `lga_id`-scoped admin auth path in the field table.
- **M16 [reported]** `06-DATA §9` dismisses CC BY-SA share-alike as "unlikely," but serving GRID3-derived ward/community data via API is distribution → derivative/share-alike obligation untested. → Legal review of API exposure of derived reference data.
- **M17 [reported]** `05-ENG §2.4`: if PayGuard is unreachable at acceptance the reservation commits with no hold-expiry → reserved inventory starves; every reconciliation threshold is "pending confirmation." → Define a hold-expiry that voids the link and returns quantity; commit a sandbox-verified contract as a Phase-0 gate.

### Product / PRD / world-scenario
- **M18 [reported]** No Nigeria-specific KYC document set (NIN/BVN/phone) defined per `VerificationTier`. → Specify accepted proof + provider per tier.
- **M19 [reported]** `PRD-Volume-B §13` pre-synced regional Knowledge Bank bundles are "optional, not guaranteed in v1" although offline browsing is a core need. → Make bundles a v1 requirement.
- **M20 [reported]** `PRD-Volume-B §23` a `Banned` chemical status change doesn't force immediate re-review of advisory content (only the 12-month cycle catches it). → Link a ban to immediate re-review.
- **M21 [reported]** `PRD-Volume-E §3.1` CEA power-outage failure modes per `SystemType` (ebb-flow, fogponic) are unspecified though CEA cycles are power-dependent; the CEA extension is additive/unphased so it can slip into v1 unbudgeted. → Define per-type outage/degraded tasks and assign a delivery phase.
- **M22 [reported]** Admin high-stakes roles (Finance/MarketplaceOps dual sign-off) have no MFA/session-binding/step-up requirement; break-glass has no proactive alert/auto-interlock; the compliance console defers NDPA entirely with no interim workflow. → Mandate MFA+step-up; alert/interlock on break-glass; add an interim DSAR workflow.
- **M23 [reported]** Master §19/§31 "AI is suggestion-only" invariant has no runtime enforcement or acceptance test. → Attach a cross-volume AI-safety acceptance test.
- **M24 [reported]** Volume C: negotiated-split and partial-refund outcomes have no accounting posting rule; 2% commission basis undefined for refund/split; buyer financial-KYC depth per tier undefined. → Define journal postings + commission basis + KYC depth.

### QA / design
- **M25 [verified] `Doc\farm-basics-styleguide-2.html` uses a non-token color `#8a6209` at 3 sites** (lines 109 `.badge-gold` text, 126 `.field.warning .hint`, 865 inline) — outside the token set (`--gold:#FDBB26`, `--print-gold:#E0A81E`). The v1 styleguide uses it too. → Replace with a token; add a "no ad-hoc colors" check to the design-system governance gate.
- **M26 [reported]** `styleguide-2 §23` token JSON is abridged vs DS2 §23 (omits `surfaceDarkHover/Active`, `onDarkPrimary/Secondary`, several type roles, `easing.in`); DS2 §24 lists more screens than the HTML renders. → Regenerate the HTML from the DS2 token/section source so the two cannot drift.
- **M27 [verified] Dangling reference in `14-QA-Accessibility-Conformance-Plan.md` line 149** — "flagged as an open item (§19)" but Doc 14 has no §19 (it ends at §12; the intended target is §11 Open Decisions). → Fix the anchor.
- **M28 [reported] QA has no failover/DR test and no seasonal-peak/load test.** Doc 12 covers concurrency, offline, constraints, geodesic accuracy, and integration failures well, but nothing exercises the single-VM blast radius (failover/restore under load) or market-day/harvest surge volumes — both named world scenarios for this platform. → Add a DR/chaos suite and a load profile tuned to seasonal peaks.
- **M29 [reported] Localisation + admin-console mobile accessibility under-tested.** Doc 14 defers language-of-page to a pending scope decision and tests TalkBack only against the admin console "via a mobile browser scenario." → Add explicit localisation tests once scope is fixed; broaden AT coverage.

---

## Part D — Minor findings (quick wins)

- **[reported]** `02B §A-2` relaxing the one-active-cycle rule for non-`open_field` conflicts with `02-ENG §4.2`'s unconditional `uq_one_active_cycle_per_field`; no new partial-index predicate given.
- **[reported]** `02-ENG §3.3` `notification_dispatch` has no delivery dedup key → SMS can double-send under at-least-once outbox (cost).
- **[reported]** `02-ENG §4.2` `forum_answer.upvote_count` is a stored counter with no vote table (drift, no double/self-vote guard).
- **[reported]** `02-ENG §7` `feature_flag_override` PK omits `scope_type` (region/org overrides with equal UUIDs collide); `admin_role_assignment` has no live-unique.
- **[reported]** `02-ENG §3.4` `audit_log_entry.entity_id uuid` cannot reference text-PK entities; no `request_id`/actor-IP correlation.
- **[reported]** `02-ENG §4.1` `field_overlap_event` has no `UNIQUE(new_field_id, existing_field_id)` → duplicate events on re-run.
- **[reported]** `02-ENG §6.2` `vendor_profile.identity_or_org_id` unvalidated polymorphic (self-admitted PROB-014); `rating_average numeric(3,2)` vs `rating 1–5`.
- **[reported]** `02-ENG §6.3` `reservation` lacks `state_changed_at`, so `05-ENG §2.4`'s "stuck" detection has no staleness column.
- **[reported]** `06-DATA §5` a chemical past its 12-month re-verification stays `published`/listable; no expiry-driven downgrade.
- **[reported]** `04A §8` edge rate limits are IP-keyed (see M8); `04A §11.5` CrowdSec bouncer fail-open/closed undecided (an unknown that is itself a posture); `04A §4.1` egress unconditionally allowed.
- **[reported]** `04 §5` Argon2id parameters unspecified; NIN column-key rotation has no re-encryption path; `04 §7` no TLS min-version/cipher/HSTS/OCSP baseline; `04 §11/§16` reliability-score collusion unresolved.
- **[reported]** `01A §2.1` cert pinning has no backup pin / forced-upgrade gate; `07 §1` Staging infrastructure parity undefined; `08 §2/§3` "Log Store"/"Trace Store" unnamed and share fate with the VM; `08 §9` 99.5% SLO on a SPOF topology may be unachievable.
- **[reported]** `10-ENG` guardrails (SBOM/signing/sigstore, fuzzing) are unmapped to pipeline artifacts; agent tool-execution logs claimed as audit evidence but no storage/tamper-evidence defined.
- **[verified] `Farm-Basics-Design-System-2.md` is correctly the v2 authority** (§19 Accessibility, §23 Tokens, §24 Screen Patterns, §27 Governance) — the AGENTS.md renumbering note holds; the only v1/v2 risk is X1/X2 path + document drift.

---

## Part E — World-scenario coverage matrix

| Scenario | Covered well | Gap |
|---|---|---|
| Offline-first writes | Volume A boundary walk; Doc 12 §4.2 offline suite | No platform-wide sync contract (X3); Knowledge Bank bundles optional (M19) |
| Low-end device / low literacy | Doc 14 §§4–5 (low-literacy as a first-class dimension); DS2 §14 voice/SMS/USSD | Localization scope + numeracy remediation deferred; admin-console AT coverage thin (M29) |
| Low-bandwidth degradation | Doc 14 §5 | 2G/high-loss testing only "manual"; no load profile for it |
| Payments rails | Escrow model specified | CBN licensing gate (C4); no partial/split posting (C3/M24); AML/velocity beyond ₦2M cap |
| Fraud / abuse | Review COI, dual sign-off, resource constraints tested (Doc 12) | Test/device collateral: review gaming, agent fraud, SIM swap; reliability-score collusion open |
| Identity/KYC | Tokenised NIN intent | KYC document set per tier undefined; NIN storage default weak; no per-tier document set |
| Climate/seasonality/agronomy | Crop cycle + stage gates | No weather/agro-climatic signal feeding cycles; no seasonal load test; classifier accuracy unproven |
| Logistics/market | Pool/buyer-request partial fills exist | Escrow has no partial-delivery release; delivery-evidence/returns undesigned |
| Regulatory | NDPA/CBN/FCCPC named | DPIA incomplete; residency undecided; erasure mechanism absent; retention unenforced |
| Reliability/DR/ops | Runbook + DR doc exist | IaC deferred; solo roles unsatisfiable; no failover test; 2-1 backups |
| Accessibility | Strong (WCAG + low-literacy dimension) | Numeracy unresolved; localisation pending; high-contrast mode untested |

---

## Part E2 — What is already strong (do not over-remediate)

- **Doc 12 (Master Test Plan)** is unusually rigorous: risk-tiered, demands *genuine* races, *genuine* disconnection, invalid-transition assertions, and realistic Nigerian seed data — and it already anticipates `NULLS NOT DISTINCT` edge cases.
- **Doc 13 (UAT/Pilot)** correctly separates internal UAT from field pilot, over-weights the low-literate segment, names anti-metrics and quantified stop conditions, and builds in research ethics + a funded remediation commitment.
- **Doc 14 (Accessibility)** correctly treats low-literacy/numeracy as a *second* dimension WCAG doesn't measure, and is honest that its WCAG target is chosen, not legally mandated.
- **Design System 2** is a coherent, token-based system with accessibility, platform guidance, and governance sections.

---

## Part G — Verification pass results (2026-10-10)

A targeted confirmation pass re-read the source for each `[reported]` item. **Confirmed** (now firm): C1, C2, C9, C11, C13; the schema claims S1–S4, S9, S10; the infra claims T1, T4; the legal claims for residency (C6), erasure (C7), ToS continued-use (M2), ML-training licence (M3), DPO/NDPC (M4/§L8), and §5.3 open items; and the product claims M19, M20, M21, M24, X3 (offline per-workflow only), X4 (localization open, no locale columns).

The pass **corrected** the following — do not quote the earlier wording:

| Finding | Verdict | Evidence | Correction / effect |
|---|---|---|---|
| C4 — CBN escrow has "no fallback" | ✘ refuted | `10-LEGAL` §L112 "the Phase-1 non-escrowed fallback mode already built into the marketplace flow"; `Part A` §12.5.4 | A fallback exists. Licensing still **blocks Phase 2**, but it is a launch-config decision, not a dead-end. **Critical → Major.** |
| C12 — authoritative Geography seed unresolved | ✘ refuted | `06-DATA` §38/§64/§72 — a full GRID3 "seed and grow" sourcing strategy is specified | **Remove.** The seed is resolved. |
| M22 — admin MFA/step-up absent | ✘ refuted | `PRD-Volume-D` §444 "Mandatory MFA | Every admin login | No exceptions"; step-up at §567; break-glass escalation §458 | MFA **is** mandatory. Residual (break-glass proactive alert/interlock) is a **Minor**. |
| C10 — `age` key "no rotation/escrow" | ◐ partial | `04-ENG` §218 rotation **is** defined (90-day default + immediate on compromise); `09-ENG` §49 key "escrowed separately"; only the co-holder is dormant (`04A` §136) | Rotation/escrow exist. Residual = single-holder co-custody. **Critical → Major.** |
| C3 / S7 — financial schema | ◐ partial | `02-ENG` §1728 trigger fires **per line** → only an *empty* `journal_entry` persists (unbalanced non-empty **is** rejected, §1692); `signoff_decision` includes `'split'` (§1347), `03-ENG` §466 names partial refund; but `escrow_status` (§1263) has no partial state | Narrow to: (a) empty-ledger persists, (b) `escrow_status` lacks a partial-release state. Split/partial refunds are **not** wholly absent. |
| M1 — legal liability | ◐ partial | `11-LEGAL` §166 "governed by the laws of the Federal Republic of Nigeria" | Governing law **is** specified; only the dispute **forum** is open (§168). Cap/indemnity concerns stand. |
| M6 — JWT "no introspection" | ◐ partial | `10-ENG` §70 names "RFC 7662 token introspection (continuous validation)" | Introspection **is** named. Residual: no denylist / back-channel logout / `alg` pinning stated. |
| M8 — rate limiting / log redaction | ◐ partial | `04A` §166 IP-keyed zones confirmed; the "Serilog nin/password/rawNin field list" is **not** corroborated in `04`/`04A` (08-ENG §29 only says no raw NIN/password in logs) | Keep the IP-key point; drop the specific Serilog-field claim. |
| M10 — DR "no IaC" | ◐ partial | `07-ENG` §155 mandates "Terraform or equivalent for the VM itself"; `09-ENG` §67 references it | IaC is **specified, not deferred**; only the implementation code is absent. **Reword.** |
| M12 — reference-table provenance | ◐ partial | `02-ENG` §971 `knowledge_article` **has** `version` + `last_reviewed_at` | Only `crop_profile`/`chemical_product`/`fertilizer_product` lack provenance. |
| M13 / S8 — idempotency type mismatch | ◐ partial | `02-ENG` §458 idempotency key is `uuid` (confirmed); the "provider event id is a string" and "offline-queue window" claims were **not** found in `02`/`03-ENG` | Keep the `uuid` point only; drop the unverified sub-claims. |
| M14 / S6 — money currency | ◐ partial | `02-ENG` §1301 `trade.offer.currency char(3)` exists; amounts vary (`numeric(10,2)` §1277/§1300); **no floats** | Keep the stray-`currency`-no-FX point; drop "NGN-only numeric(14,2)" as universal. |
| M17 — PayGuard hold expiry | ◐ partial | `Part A` §12.5 (14-day no-confirmation → Disputed, never auto-release); `05-ENG` §238 stuck-reservation handling | Partial handling exists; the "commits with no hold-expiry" framing is overstated. |
| S5 — provenance columns | ◐ partial | as M12 | narrow |
| T2 — `age` rotation | ◐ partial | same as C10 | see C10 |
| M1 legal — forum | ◐ partial | `11-LEGAL` §166 governs = Nigeria | forum only |

**Net severity adjustment:** the firm **Critical** set is C1, C2, C3(partial), C5–C9, C11, C13; C4 and C10 move to **Major**; C12 is withdrawn; M22 → Minor. The document set is materially stronger than the raw cluster output implied on identity (04-ENG §7.3 raw-NIN-never-stored), admin MFA (Volume D §444), Geography sourcing (06-DATA), and secret rotation — those should **not** be logged as gaps.

---

## Part F — Recommended next actions (ordered)

1. **Resolve X1** — pick one authority per superseded pair and archive the rest. Fastest risk reduction.
2. **Fix X2/X3** — banner `BRD.md`; update `AGENTS.md` paths; repoint Doc 13's superseded citation.
3. **Apply C1/C2** — merge `02B` into `02-ENG`/`03-ENG` (Field→ProductionUnit, nullable boundary, `capture_method`/`area_basis`/`growing_medium_class`) before any field-module code.
4. **Close the legal gates (C4–C7)** before marketplace/escrow and production PII.
5. **Backfill the ops hardening (C9/C10 + security majors)** — backup encryption/immutability + key custody first.
6. **Verify the [reported] items** — run a targeted confirmation pass on each before turning any into a schema/spec change.
7. Feed confirmed findings into `Plan\sessions\Followups-Ledger.md` as new `F-##` entries (nothing here is committed to a source of truth).

---

*Method note: this review used a seven-lens expert panel adapted to a world-scenario + hardening rubric. Sub-agent output was spot-verified; hallucinated claims are recorded as [disputed] rather than deleted, so the audit trail shows what was checked and rejected.*
