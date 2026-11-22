# Farm Basics — Progressive Implementation Sprints

> **How to use this document.** Every sprint in the Farm Basics implementation plan is decomposed here into **tasks → micro-tasks**. Each micro-task is an atomic, verifiable activity sized at **10 minutes or less** of focused work. Work through them progressively (top to bottom); completion checkboxes persist in your browser, so you can resume exactly where you stopped. Where a micro-task must wait on a real-world dependency (procurement, third-party contract, stakeholder decision), it is marked **gated** and the dependent step records that it *Requires Clarification* — proceed by implementing the surrounding logic with a documented interface and revisit once the gate clears.
>
> - **Companion plan:** `Plan\Farm-Basics-Sprint-Plan.md` (durations/effort indicative, *Requires Clarification*).
> - **Authority:** this sprint plan is derived from `Doc/19-ENG-Implementation-Sprint-Plan.md` (Document 19 — Implementation & Sprint Plan). Where this document and Document 19 disagree, Document 19 governs sequence and content. Effort estimates and calendar dates remain *Requires Clarification*; Document 19 §8 forbids dating anything until Sprints 0–2 have measured velocity.
> - **Operating rules:** the `Preamble` below is binding for every session — session-start ritual, reading/citation protocol, DoR/DoD, requirements traceability, guardrail injection, skills directive, change-control boundary, and the Sprint-Journal.
> - **Guardrails:** `Doc/10-ENG-AI-Agent-Security-Guardrails.md` (v1.1) plus Document 19 Appendix A (the agent hard rules, to be kept at the repository root as `AGENTS.md`); guardrails are injected per P0.5 and enforced per Document 19 §2.4 review levels.
> - **Skills:** discovered per P0.6 from `Plan\Recommended-Skills.md` and the installed skill locations; `skills\skills-lock.json` does not exist in this repo, so only installed skills may be assumed.
> - **Sources:** all references point into `Doc/` (BRD, PRD, Engineering 01–11 incl. 01A/02B/04A, QA, Design System 2, Parts A/B, Legal 10–11, Data). Short cites, e.g. `[02B §3.2]` = `Doc/02B-ENG-CEA-Schema-and-API-Delta.md` §3.2.
> - **Exit ticket:** each sprint ends with its acceptance gate (`[BRD §18.0]`, module AC straddle, or QA package pass). Leave a sprint only when its ticket is satisfied. Milestone gates (M0 exit, M2/pilot-readiness) per Document 19 §4 must hold before the next milestone begins.
> - **Effort note:** the focused micro-task inventory below is the *deterministic* implementation surface (hours). Full calendar sprint effort additionally includes orientation, environment access, integration, reviews, and capacity variance — those remain *Requires Clarification* per `BRD §7.1`.

---

## Preamble — Implementation Instructions (read first)

> **Status:** binding operating rules for this implementation programme. **Applies to:** AI coding agents (opencode/Claude), and every human implementer. **Controls:** the sprint task lists below are executed *through* the rules in this section. Every shall below is enforceable: an instruction that cannot be traced to a rule here, a source citation, or the guardrails has no authority.

### P0.1 — Session-start ritual

At the start of every working session (new or resumed), the executing agent **shall**:

1. Read this Preamble in full before touching any task below.
2. Read the current sprint section of this document (tasks → micro-tasks) and its sprint-plan companion entry.
3. Load the applicable skills per P0.6 and the guardrail injection per P0.5 before beginning hands-on work.
4. Open a **Sprint-Journal** entry (per P0.8) recording date, session goal, and start micro-task reference.
5. Confirm the session's first task remains un-ticked in this document; if the last session halted mid-sprint, resume exactly where the checkboxes + journal indicate.

### P0.2 — Reading & citation protocol

1. All references in this document use the short form declared in the header (e.g. `[02B §3.2]`, `[01A §5.3]`). The agent **shall** open and read the cited source section before implementing a micro-task whose check expects it; a short cite is a pointer, not a summary.
2. The `Doc/` set (BRD, PRDs, Engineering 01–11, QA, Design System 2, Parts A/B, Legal, Data), Document 19, the sprint plan, and this runbook are the **source of truth**. The agent **shall not** silently override them; corrective decisions **shall** be recorded in the Sprint-Journal and, where user-scoped, flagged as *Requires Clarification*.
3. Micro-task checkboxes persist in-browser. A ticked checkbox **shall** only record completed, verified work in accordance with P0.3.

### P0.3 — Definition of Ready / Definition of Done (DoR / DoD)

A sprint is **Ready** when: its exit ticket and all task-level acceptance checks are legible; gated/`*Requires Clarification*` items are identified and unblocked; the P0.5 guardrail injection and P0.6 skills for that sprint are loaded.

A task is **Done** when, per deliverable type:

| Deliverable | Definition of Done |
|---|---|
| Code (feature/fix) | Meets its micro-task AC, unit-tested, lint/format clean, CI green, no TODOs left undocumented, security guardrails checked |
| Infra/IaC | Declared in IaC, secret policy honoured, least-privilege verified, guardrails checked |
| Schema/migration | Matches the tagged ENG schema doc (Doc 02 / Doc 02B DDL), migration + rollback exercised, audit/log columns verified |
| Doc update | Changes trace to a source request, version note added, no obsolete sections left |
| Test package | Automated where possible; manual steps recorded; evidence (output files/screenshots) linked in the journal |

**Gate evidence standard:** every exit ticket **shall** be bumped only with artifacts — test output, CI link, restore drill log, or UAT sign-off — recorded in the Sprint-Journal (P0.8). A checkbox or gate **shall not** be closed on assertion alone.

### P0.4 — Requirements-traceability rule

1. Every micro-task **shall** trace to the requirement it serves: the bracketed source cite on the task line is the trace anchor.
2. The agent **shall not** implement capability that has no trace anchor in this document or the source set; if the runbook lacks an anchor for a required behaviour, it **shall** add the anchor by extending this document (approved update) rather than drifting.
3. Every sprint exit ticket **shall** be linked to its AC source and the artifact that satisfies it, forming a chain: requirement → micro-task → evidence.

### P0.5 — Guardrail injection map

Document 19 Appendix A (hard rules, repository root `AGENTS.md`) is the single guardrail source and is in effect for **every** sprint; `Doc/10-ENG-AI-Agent-Security-Guardrails.md` (v1.1, IDs T-1…T-5, C-1…C-5, V-1…V-5, I-1…I-4, L-1…L-2) supplies the detailed per-category intent where cited. Appendix A rule 5 (never weaken/remove a constraint, trigger, or test) is a hard rule: CI fails if a constraint-bearing migration changes without a corresponding test change.

| Sprint range | Primary focus | Guardrail emphasis |
|---|---|---|
| Sprint 0 | Deployment shape, CI, auth, redaction, backup, offline-sync spike | Appendix A rules 1–4, 6–11; Serilog redaction test; genuine-parallel harness |
| Sprint 1–2 | Identity, RBAC, phone uniqueness fix, registration race | Appendix A rules 1, 8, 10, 12; `nin_verification_ref UNIQUE`; no phone UNIQUE |
| Sprint 3–4 | Fob permanence, attestation, review engine, no-self-approval | Appendix A rules 7, 12; DB-enforced constraints |
| Sprints 5–11 | Geodesic area, ledger, offline sync, hardening | Appendix A rules 2, 3, 4, 6, 7; real Postgres+PostGIS only |

Intra-sprint, at every phase/section boundary the agent **shall** re-apply the threat-model delta before writing new surfaces, and **shall** stop-and-ask per Document 19 §2.3 whenever the specification is silent or contradictory, a constraint/test would be weakened, a dependency added, or two documents disagree (precedence table = Document 19 Appendix C).

### P0.6 — Skills directive

1. The agent **shall** discover skills from the authoritative install locations and `Plan\Recommended-Skills.md` (the research catalogue). `skills\skills-lock.json` does not exist in this repo; a skill that is not installed **shall not** be assumed available.
2. For **every** micro-task the agent **shall** load and apply the relevant skill(s) and record the skill used in the Sprint-Journal.
3. Where two skills overlap the same task, prefer the one already installed; otherwise the *Fit* column and reviewed caveats in the catalogue decide, with the choice journaled.

### P0.7 — Change-control boundary & autonomy

1. **Autonomous scope:** within a sprint the agent **may** proceed task-to-task without further sign-off, provided DoR holds and guardrails are applied. Re-slicing an over-10-minute micro-task into two and recording the split is mandated and **does not** require approval.
2. **Stop-and-ask (Requires Clarification):** the agent **shall** halt and surface a decision when any of the following occurs: a gated dependency is required; a `*Requires Clarification*` block is reached; the runbook conflicts with a source document; a budget, capacity, or licensing question arises; a change would alter an engineering document's decision beyond a journaled note. While halted, surrounding logic **shall** be implemented with a documented interface so work is not double-blocked.
3. **Approved-change path:** extending this runbook is an approved update.
4. **Non-modification policy for `Doc/` (solo-builder mode):** never edit existing base specification documents in `Doc/` in place (`Doc/00A-INDEX` §2, `Doc/DECISIONS.md` `ADR-007`). Record amendments as lettered addenda aligned to the base document number (e.g., `A`, `B`, `C`, `D`) and register them append-only in `Doc/PRECEDENCE.md` and `Doc/DECISIONS.md` alongside a Sprint-Journal entry (`P0.8`). Where a needed change introduces a new architectural, legal, or commercial decision, `P0.7.2` stop-and-ask still applies.

### P0.8 — Resumption protocol & Sprint-Journal

1. The **Sprint-Journal** (`Plan\sessions\Sprint-Journal.md`) is the session ledger. Each session **shall** append: date, session goal, micro-task IDs touched (start→end), skills applied, guardrails checked, gate evidence, decisions and *Requires Clarification* items, and the exact next un-ticked micro-task.
2. Suggested followups are recorded add-only in the **Followups Ledger** (`Plan\sessions\Followups-Ledger.md`), each as an `F-##` entry traced to the source session entry.
3. On resume the agent **shall** run P0.1, read the journal tail to locate the exact continuation point, and verify it against the un-ticked checkboxes before proceeding.
4. If a resume reveals drift between the journal and the unchecked boxes, the journal entry **shall** be reconciled and the discrepancy noted before continuing.

---

## Specification Completion Register (`WP-D01`–`WP-D15`)

> **Purpose:** Fifteen specification, legal, QA, and operational companion/addendum documents are explicitly called for by existing specifications or required to close build/pilot/stakeholder gates surfaced across `Doc/` and `Plan/sessions/Followups-Ledger.md`. Each is converted below into a self-contained work package (`WP-D01`–`WP-D15`) with a Document 19 Appendix B task card and atomic ≤10-minute micro-tasks embedded directly into the sprint whose code or exit gate depends on it. *(Immediate `Doc/` file housekeeping — duplicate `PRD-Master-Volume (2).md`, superseded `unified-platform-spec.md` / `admin-operations-console-spec.md`, and `02B-ENG` enum fold `F-24` — is deferred to a separate pass by founder direction.)*

| WP | Target Document | Sprint | Task ID | Review | Gates / Resolves |
|---|---|---|---|---|---|
| **WP-D01** | `Doc/DECISIONS.md` — Architectural & Product Decision Record | Sprint 0 | Task 0.0 (`S0.0.M1–M3`) | B | `Doc 19 §3.2, §7`; `PRECEDENCE.md §4`; `AGENTS.md` Rule 20 |
| **WP-D02** | `Doc/01B-ENG-Offline-Sync-and-Conflict-Resolution-Spec.md` — Platform-Wide Offline Sync, Conflict Resolution & Fob Quarantine Spec | Sprint 0 | Task 0.7 (`S0.7.M1–M4`) | **A** | `WP-04`; `PRECEDENCE.md P2, P23`; `Doc 15 §18 gap 6`; `F-28(2)` |
| **WP-D03** | `Doc/16A-OPS-Error-Code-and-Plain-Language-Copy-Catalog.md` — Error Code to Plain-Language & Localization Copy Catalog | Sprint 1 | Task 1.6 (`S1.6.M1–M4`) | B | `WP-14`; `Doc 03 §14`; `02B §4`; `Doc 16 §16`; `AGENTS.md` Rule 17 & DoD |
| **WP-D04** | `Doc/11A-LEGAL-Plain-Language-Summary-and-Pilot-Consent.md` — Plain-Language & Audio Summary of Terms/Privacy + Pilot Consent Sheet | Sprint 3 | Task 3.5 (`S3.5.M1–M4`) | **A** | `WP-32`; `Doc 11 §5.1`; `Doc 13 §10.1`; `Doc 17 §18 gap 2`; `P7, P11` |
| **WP-D05** | `Doc/15A-OPS-Paper-Fallback-Kit-and-Hardware-Spec.md` — Printable Paper Fallback Kit & NFC Fob/Tablet Hardware Spec | Sprint 3 | Task 3.6 (`S3.6.M1–M3`) | B | `WP-30, WP-31`; `Doc 15 §3, §12`; `Doc 09 §10`; `Doc 17 §18 gap 3` |
| **WP-D06** | `Doc/06A-DATA-Content-Governance-and-Lifecycle-Spec.md` — Knowledge Bank & Master-Data Content Governance Workflow Spec | Sprint 4 | Task 4.5 (`S4.5.M1–M4`) | **A** | `WP-42, WP-70`, Phase 3 `E3.2`; `Doc 18 §10 gap 5`; `P17`; `F-13, F-25(7), F-28(7)` |
| **WP-D07** | `Doc/18A-OPS-Reviewer-Gold-Case-Library-and-Calibration-Pack.md` — Reviewer Gold-Case Library & Calibration Test Pack | Sprint 4 | Task 4.6 (`S4.6.M1–M4`) | B | `WP-40, WP-41`, Phase 3 `E3.4`; `Doc 18 §8.3, §9.8, §11`; `P16–P18` |
| **WP-D08** | `Doc/10A-LEGAL-NDPA-DPIA-and-RoPA.md` — NDPA 2023 Data Protection Impact Assessment (DPIA) & Record of Processing Activities (RoPA) | Sprint 4 | Task 4.7 (`S4.7.M1–M4`) | **A** | **M0 exit gate** (`Doc 10 LEG-02/03`); `P10–P12`; `F-27` |
| **WP-D09** | `Doc/13A-QA-Pilot-Baseline-and-Remediation-Protocol.md` — Pilot Agronomic Baseline Instrument & Participant Remediation Reserve Protocol | Sprint 11 | Task 11.5 (`S11.5.M1–M3`) | **A** | **M2 pilot-readiness gate**; `Doc 13 §10.2, §12`; `Doc 17 §18 gaps 4, 7` |
| **WP-D10** | `Doc/17A-OPS-Partner-Agreement-and-External-Pilot-Brief.md` — Partner Organization MoU Template, Data-Sharing Covenant & External Pilot Summary | Sprint 11 | Task 11.6 (`S11.6.M1–M4`) | B | **M2 pilot-readiness gate**; `Doc 17 §18 gaps 5, 6, §20.1`; `Doc 15 §18 gap 8` |
| **WP-D11** | `Doc/BRD-v2A-Subscription-Feasibility-and-Defensibility-Addendum.md` — Freemium Subscription Model, Unit-Economics Feasibility & Anti-Clone Defensibility | Sprint 1 | Task 1.7 (`S1.7.M1–M2`) | B | `PRD-Master §18, §26, §30`; `PRD-Volume-C §16`; `BRD-v2 §1.3, §11.6, §17`; `PRECEDENCE.md #23, #24` |
| **WP-D12** | `Doc/02C-ENG-Commercial-Farm-RBAC-and-Subscription-Schema-Addendum.md` — Commercial Farm Multi-Staff RBAC & Subscription Entitlements Schema/API | Sprint 1 | Task 1.8 (`S1.8.M1–M3`) | **A** | `WP-11, WP-12`; `Doc 02 §3.1, §4.1, §6.4`; `Doc 03 §2, §13, §14`; `Volume A §2`; `PRECEDENCE.md #25` |
| **WP-D13** | `Doc/05A-ENG-PayGuard-Bank-Escrow-Ledger-Addendum.md` — PayGuard 3-Tier Non-Custodial Commercial Bank Escrow Ledger Technical Addendum | Sprint 4 | Task 4.8 (`S4.8.M1–M2`) | **A** | Phase 2 `E2.4`; `Doc 05 §2`; `F-25(5), F-26(3)`; `PRECEDENCE.md #26` |
| **WP-D14** | `Doc/10B-LEGAL-PayGuard-Escrow-and-WhatsApp-NDPA-Addendum.md` — PayGuard Commercial Bank Escrow Posture & NDPA 2023 WhatsApp / Unconsented Data-Sharing Prohibitions | Sprint 4 | Task 4.9 (`S4.9.M1–M2`) | **A** | **M0 exit gate** & Phase 2 `E2.4`; `Doc 10 §3.2, §4`; `10A-LEGAL`; `Doc 11 §3.5.2, §4.4`; `PRECEDENCE.md #26, #27` |
| **WP-D15** | `Doc/17B-OPS-Unit-Economics-and-WhatsApp-Channel-Policy-Addendum.md` — Subscription GTM Economics & Mandatory WhatsApp / Messaging Channel Compliance Policy | Sprint 11 | Task 11.7 (`S11.7.M1–M2`) | B | **M2 pilot-readiness gate**; `Doc 17 §7, §13, GTM-D11`; `Doc 15 §2`; `Doc 16 §3`; `17A-OPS Part C`; `PRECEDENCE.md #23, #27` |

---

## Sprint 0 — The walking skeleton (M0)

> **Phase 0 · Focus:** pre-flight decision ledger (`WP-D01`); deployment shape runs (monorepo, Docker Compose, Caddy, Postgres+PostGIS, Cloudflare); CI with the seven non-skippable stages, Testcontainers `postgis/postgis`, genuine-parallel harness; Keycloak + JWT + Serilog redaction with test; Blue/Green deploy to staging + `wal-g` off-provider backup + timed restore drill; offline-sync spike (Flutter + drift + dio, replay exactly once) and platform-wide offline sync specification (`WP-D02`); React admin shell login + Prometheus/Grafana with one real metric. **Exit ticket:** all WP-D01, WP-00…WP-05, WP-D02 done; feeds M0 exit gate after Sprint 4. **Primary:** `01A` §3–§6, §10; Doc 07 §3; Doc 09 §3, §7; Doc 12 §4.1, §9; `PRECEDENCE.md`. *If Sprint 0 takes two weeks, that is normal — it is the sprint that pays for all the others.* (Document 19 WP 00–05 + WP-D01, WP-D02)

### Task 0.0 — WP-D01: `Doc/DECISIONS.md` — Architectural & Product Decision Record

> **Task Card (`WP-D01`):** **Goal:** Create the repository's canonical append-only decision record (`Doc/DECISIONS.md`) so cold-start agent sessions never lose architectural or product decisions. · **Context:** `Doc 19 §3.2, §7`, `PRECEDENCE.md §1–§4`, `01A §13`, `17-OPS §2`, `AGENTS.md Rule 20`. · **Hard Rules:** Rule 15 (cite IDs), Rule 20 (record decisions). · **Review:** Level B. · **Stop and ask if:** an entry would resolve a `P1`–`P25` or `D1`–`D12` pending decision without founder sign-off.

- [x] **S0.0.M1** (≈8 min) — Scaffold `Doc/DECISIONS.md` with standard `Doc/` header (`Version`, `Status`, `Document Sequence`, `Depends On`, `Source of Truth`, `Audience`), append-only governance rules, and the ADR entry template (`ID`, `Date`, `Status`, `Context`, `Decision`, `Consequences`, `Supersedes/Amends`, `Refs`) [`Doc 19 §3.2`; `PRECEDENCE.md §4`; `AGENTS.md Rule 20`]
- [x] **S0.0.M2** (≈8 min) — Seed Part 1 of `Doc/DECISIONS.md` with the locked stack & architecture decisions (`D-01`–`D-23` from `01A §13`, keeping `D-16` marked *Proposed*) and the 22 resolved cross-document conflicts from `PRECEDENCE.md §2` [`01A §13`; `PRECEDENCE.md §2`]
- [x] **S0.0.M3** (≈8 min) — Seed Part 2 of `Doc/DECISIONS.md` with the Pending Decisions register (`P1`–`P25` from `PRECEDENCE.md §3` and `D1`–`D12` from `17-OPS §2`), recording each item's safe interim build default ("Until decided") and the exact sprint/gate it blocks [`PRECEDENCE.md §3`; `17-OPS §2`; `Doc 19 §3.3`]

### Task 0.1 — WP-00: Monorepo, Docker Compose, Caddy, Postgres+PostGIS, Cloudflare in front

- [x] **S0.1.M1** (≈8 min) — Create monorepo layout: `apps/backend`, `apps/farmer-app` (Flutter), `apps/admin` (React), `infra`, `tests` alongside existing `Doc/` and updated root `README.md` [`01A` §4–5]
- [x] **S0.1.M2** (≈8 min) — Docker Compose: API + Postgres+PostGIS + Caddy + Keycloak; local Cloudflare-in-front shape [`01A` §4–5]
- [x] **S0.1.M3** (≈8 min) — Caddy config: TLS termination, rate-limit headers, health routes to API [`01A` §5]
- [x] **S0.1.M4** (≈8 min) — `/health/ready` endpoint proving DB reachability [`01A` §4–5]
- [ ] **S0.1.M5** (≈8 min) — Smoke: deploy the shape to staging and hit `/health/ready` end to end [`01A` §4–5]

### Task 0.2 — WP-01: CI — seven non-skippable stages, Testcontainers `postgis/postgis`, genuine-parallel harness

- [x] **S0.2.M1** (≈8 min) — CI pipeline: build, lint, unit, contract, integration (Testcontainers `postgis/postgis`), security scan, deploy-to-staging gates [Doc 07 §3]
- [x] **S0.2.M2** (≈8 min) — Genuine-parallel test harness: barrier-synchronised concurrent execution for race tests [Doc 12 §4.1, §9]
- [x] **S0.2.M3** (≈6 min) — Mark the seven stages non-skippable; CI blocks merge on any red stage [Doc 07 §3]

### Task 0.3 — WP-02: Keycloak + JWT validation + `/health/ready`; Serilog field-redaction policy and its test

- [x] **S0.3.M1** (≈8 min) — Keycloak realm + client; API validates JWT, identity-only claims [`01A` §3]
- [x] **S0.3.M2** (≈8 min) — Serilog structured logging with field-redaction policy (NIN, OTP, PIN, password) [`01A` §10.3]
- [x] **S0.3.M3** (≈6 min) — Unit test proving the redaction policy masks secrets and passes [`01A` §10.3]

### Task 0.4 — WP-03: `deploy.sh` Blue/Green to staging; `wal-g` off-provider backup; one full restore drill into a fresh VM, timed

- [ ] **S0.4.M1** (≈8 min) — `deploy.sh` Blue/Green deployment to staging [Doc 09 §3]
- [ ] **S0.4.M2** (≈8 min) — `wal-g` backing up to a *different provider/region* from the VM [`01A` §5.3]
- [ ] **S0.4.M3** (≈8 min) — Full restore drill into a fresh VM, timed (first RTO measurement) [Doc 09 §7; `01A` §5.3]

### Task 0.5 — WP-04: Offline sync spike — Flutter + `drift` + `dio`; create offline, sync, replay twice, assert exactly one server row

- [x] **S0.5.M1** (≈8 min) — Flutter + `drift` local DB + `dio` client; create a record offline [Doc 01 §8.1, §6.1]
- [x] **S0.5.M2** (≈8 min) — Sync the record to the server; replay it twice; assert exactly one server row (idempotent replay) [Doc 01 §8.1, §6.1]
- [x] **S0.5.M3** (≈6 min) — Document the sync-spike outcome as the pattern for all later offline writes [Doc 01 §6.1]

### Task 0.6 — WP-05: React admin shell with login; Prometheus/Grafana up with one real metric

- [x] **S0.6.M1** (≈8 min) — React admin shell with login wired to Keycloak JWT [`01A` §2.2]
- [x] **S0.6.M2** (≈8 min) — Prometheus + Grafana running; one real metric (e.g., API request duration) dashboarded [`01A` §8]
- [ ] **S0.6.M3** (≈6 min) — Admin login demonstrated end to end on staging [`01A` §2.2]

### Task 0.7 — WP-D02: `Doc/01B-ENG-Offline-Sync-and-Conflict-Resolution-Spec.md` — Platform-Wide Offline Sync, Conflict Resolution & Fob Revocation Quarantine Spec

> **Task Card (`WP-D02`):** **Goal:** Specify a single authoritative offline sync contract covering the Flutter `drift` outbox, session lifetimes, revocation-first drain order (`P23`), and per-domain conflict/timestamp rules. · **Context:** `Doc 01 §6.1, §8.1`, `Doc 03 §1.2, §1.4, §3`, `Doc 04 §5`, `Doc 15 §3, §9, §18 gap 6`, `PRECEDENCE.md P2, P23`, `Followups-Ledger.md F-28(2)`. · **Hard Rules:** Rules 9, 10, 12, 20. · **Review:** **Level A** (sync conflict & fob revocation security). · **Stop and ask if:** choosing a concrete offline session token lifetime (`P2`) or implementing `P23` quarantine behavior in code without founder sign-off.

- [x] **S0.7.M1** (≈8 min) — Draft `Doc/01B-ENG-Offline-Sync-and-Conflict-Resolution-Spec.md` header and §1–§2: unified Flutter `drift` outbox schema (`client_guid`, `entity_type`, `operation`, `payload_json`, `client_recorded_at`, `agent_session_id`, `fob_id_used`, `idempotency_key`, `sync_status`, `retry_count`) and `workmanager` exponential-backoff state machine [Doc 01 §6.1, §8.1; `Followups-Ledger F-28(2)`]
- [x] **S0.7.M2** (≈8 min) — Specify §3 Offline Session & Cached Credential Policy: online-first initial login rule, configurable offline session lifetime parameter (*gated / Requires Clarification* per `P2` — never hard-coded infinite), bound-tablet fingerprint check on sync, and local SQLite encryption at rest (`sqlcipher` + platform keystore) [Doc 04 §5; Doc 15 §3, §18 gap 6; `PRECEDENCE.md P2`]
- [x] **S0.7.M3** (≈8 min) — Specify §4 Revocation-First Sync Drain & Offline Fob Quarantine (`P23`): mandatory pull of fob/tablet revocations *before* draining outbound queues; server rule routing any offline mutation authenticated by a fob with `revoked_at <= sync_at` into `quarantined_offline_action` for `IdentityTrustAdmin` review without mutating domain tables [`PRECEDENCE.md P23`; Doc 15 §9; Doc 16 §18 gap 1]
- [x] **S0.7.M4** (≈8 min) — Specify §5 Per-Domain Conflict & Timestamp Preservation Rules: `client_recorded_at` vs `server_received_at` dual-timestamp invariant (boundary points, `OperationLog` task dates, pest reports), `Idempotency-Key` replay deduplication, and deterministic conflict responses (`409 QUANTITY_NO_LONGER_AVAILABLE`, `409 ACTIVE_CYCLE_EXISTS`, stale master-data catalog versions) [Doc 03 §1.4, §3; Doc 12 §4.2; `AGENTS.md Rules 9, 10`]

---

## Sprint 1 — Identity & authorization foundation (M0)

> **Phase 0 · Focus:** Geography seeding pipeline; Identity/RoleProfile/Organization schema with `nin_verification_ref UNIQUE` and the phone fix (drop UNIQUE, keep index); RBAC middleware re-querying `AdminRoleAssignment` every request, fail-closed; partitioned audit log + partition-creation job; idempotency middleware, error envelope, per-endpoint rate limiting, and canonical plain-language error copy catalog (`WP-D03`). **Exit ticket:** all WP-10…WP-14 and WP-D03 done; M0 exit gate assessed after Sprint 4. **Primary:** Doc 02 §3.1, §9; Doc 03 §1.4–1.5, §1.7, §14, §15; Doc 06 §2.5; Doc 16 §16; `01A` §3, §3.2, §5.2, §6.2, §10.1. (Document 19 WP 10–14 + WP-D03)

### Task 1.1 — WP-10: Geography seeding pipeline with cross-source validation

- [x] **S1.1.M1** (≈8 min) — Geography model: State → Lga → Ward → Community with code constraints [Doc 06 §2.5]
- [x] **S1.1.M2** (≈8 min) — Seed Nigerian states + LGAs from the authoritative census listing [Doc 06 §2.5]
- [x] **S1.1.M3** (≈6 min) — Cross-source validation step flagging mismatches between sources [Doc 06 §2.5]

### Task 1.2 — WP-11: Identity/RoleProfile/Organization schema; `nin_verification_ref UNIQUE`; phone fix; JIT provisioning

- [x] **S1.2.M1** (≈8 min) — Identity, RoleProfile, Organization tables per Doc 02 §3.1 [Doc 02 §3.1]
- [x] **S1.2.M2** (≈8 min) — `nin_verification_ref UNIQUE`; drop phone `UNIQUE`, keep the index (shared household phones) [Doc 02 §3.1; Document 19 §3.1]
- [x] **S1.2.M3** (≈6 min) — JIT provisioning path for first-time users [Doc 02 §3.1]

### Task 1.3 — WP-12: RBAC tables; authorization middleware re-querying `AdminRoleAssignment` on every request; fail-closed

- [x] **S1.3.M1** (≈8 min) — RBAC tables + `AdminRoleAssignment` [Doc 03 §1.7; `01A` §3.2]
- [x] **S1.3.M2** (≈8 min) — Authorization middleware re-queries permissions on every request; fail-closed [Doc 03 §1.7]
- [x] **S1.3.M3** (≈6 min) — Test: a revoked admin loses access immediately [Doc 03 §1.7]

### Task 1.4 — WP-13: Partitioned audit log + automated partition-creation job; audit written in the same transaction

- [x] **S1.4.M1** (≈8 min) — Partitioned audit log table design per Doc 02 §9 [Doc 02 §9; `01A` §6.2]
- [x] **S1.4.M2** (≈8 min) — Automated partition-creation job so partitions never run out [Doc 02 §9]
- [x] **S1.4.M3** (≈6 min) — Audit writes in the same transaction as the business action [`01A` §6.2]

### Task 1.5 — WP-14: Idempotency middleware; error envelope; per-endpoint rate limiting

- [x] **S1.5.M1** (≈8 min) — `Idempotency-Key` middleware on POST/PATCH with storage table [Doc 03 §1.4–1.5]
- [x] **S1.5.M2** (≈8 min) — Uniform error envelope across endpoints [Doc 03 §1.5]
- [x] **S1.5.M3** (≈6 min) — Per-endpoint rate limiting via `Microsoft.AspNetCore.RateLimiting` [Doc 03 §1.8 → `01A` §5.2; Doc 03 §15]

### Task 1.6 — WP-D03: `Doc/16A-OPS-Error-Code-and-Plain-Language-Copy-Catalog.md` — Canonical Error Code to Plain-Language & Localization Copy Catalog

> **Task Card (`WP-D03`):** **Goal:** Produce the 1:1 canonical catalog mapping every `03-ENG §14` and `02B-ENG §4` machine error code to plain-language farmer UI copy, agent tablet guidance, Tier-1 support scripts, and `flutter_localizations` keys. · **Context:** `Doc 03 §1.5, §14`, `02B §2, §4.1–4.4`, `Doc 16 §4.3, §5, §16`, `Farm-Basics-Design-System-2.md §20`, `AGENTS.md Rule 17 & §5 DoD`. · **Hard Rules:** Rule 17 (plain language; never reveal whether another user holds a record). · **Review:** Level B.

- [x] **S1.6.M1** (≈8 min) — Scaffold `Doc/16A-OPS-Error-Code-and-Plain-Language-Copy-Catalog.md` with header, anti-enumeration rules (Hard Rule 17: neutral wording for duplicate NIN, phone, certificate, or plot collisions), and DS2 §20 content-voice rules (active voice, icon + word, clear next action) [Doc 03 §14; Doc 16 §4.3, §16; `AGENTS.md Rule 17`]
- [x] **S1.6.M2** (≈8 min) — Map all Identity, Auth, Fob, RBAC, and Rate-Limit error codes from `Doc 03 §14` to: HTTP status, farmer plain-language message, extension-agent tablet prompt, Tier-1 support spoken explanation, and ARB localization key [Doc 03 §2, §14; Doc 16 §5.1]
- [x] **S1.6.M3** (≈8 min) — Map all ProductionUnit, Geodesic Boundary, CEA (`02B §4.1–4.4`: `CAPTURE_SHAPE_INVALID`, `FOOTPRINT_NEEDS_CONFIRMATION`, `ADDRESS_NOT_RESOLVED`, `MEDIUM_CLASS_INCOMPATIBLE`, `GROWING_METHOD_NOT_SUPPORTED`, `BOUNDARY_REQUIRED`, `LAB_NOT_ACCREDITED_FOR_FAMILY`), CropCycle, Soil, Pest, and Knowledge error codes [Doc 03 §3–§8, §14; `02B §4.1–4.3`; Doc 16 §5.2–5.4]
- [x] **S1.6.M4** (≈8 min) — Map all Marketplace, Escrow/Reservation (`QUANTITY_NO_LONGER_AVAILABLE`, PayGuard attribution per `PRECEDENCE.md #19`), Certification (`SCOPE_UNIT_NOT_OWNED`), Vendor, and Ledger error codes, plus a CI check rule asserting every backend error code has a row in `16A-OPS` [Doc 03 §9–§14; `02B §2, §4.4`; Doc 16 §5.5, §16]

### Task 1.7 — WP-D11: `Doc/BRD-v2A-Subscription-Feasibility-and-Defensibility-Addendum.md` — Freemium Subscription Model, Unit-Economics Feasibility & Anti-Clone Defensibility

> **Task Card (`WP-D11`):** **Goal:** Author the commercial and strategic addendum superseding `PRD-Master §26` ("no subscription model exists") with the 3-tier Freemium (`FREE_CORE`) + Paid Advanced-Feature (`PRO_FARMER`, `ENTERPRISE_FARM`) + Institutional `SponsorVoucher` model, the 1,000-call/month unit-economics feasibility model, and the 5-layer anti-AI-clone defensibility moat. · **Context:** `BRD-v2 §1.3, §9.11, §11.6, §17`, `PRD-Master §18, §26, §30`, `PRD-Volume-C §16`. · **Hard Rules:** Zero modifications to existing `Doc/` files; safety-critical features never paywalled (`BR-SUB-05`); zero data loss on downgrade (`BR-SUB-01`). · **Review:** Level B.

- [x] **S1.7.M1** (≈8 min) — Draft §1–§2 of `Doc/BRD-v2A-Subscription-Feasibility-and-Defensibility-Addendum.md`: 3-tier subscription matrix (`FREE_CORE`, `PRO_FARMER`, `ENTERPRISE_FARM`), `SponsorVoucher` isolation rules (`BR-SUB-01`–`BR-SUB-06`), and mapping of `PRD-Master §18` Capability Modules to entitlements [`PRD-Master §18, §26, §30`; `PRD-Volume-C §16`; `PRECEDENCE.md #23`]
- [x] **S1.7.M2** (≈8 min) — Draft §3–§5 of `Doc/BRD-v2A-Subscription-Feasibility-and-Defensibility-Addendum.md`: Nigerian unit-economics feasibility table (per-call KYC/SMS/IVR/PostGIS burn beyond ~1,000 hobby calls/month vs. cohort revenue), 5-layer structural anti-AI-clone moat (`R-CLONE-01`), and anchored parent change log [`BRD-v2 §1.3, §11.6, §17`; `PRECEDENCE.md #24`]

### Task 1.8 — WP-D12: `Doc/02C-ENG-Commercial-Farm-RBAC-and-Subscription-Schema-Addendum.md` — Commercial Farm Multi-Staff RBAC & Subscription Entitlements Schema/API

> **Task Card (`WP-D12`):** **Goal:** Specify the PostgreSQL 15+ DDL, RBAC permission matrix, and API endpoints for multi-staff Commercial Farm organizations (`farm_staff_assignment`, `expense_voucher`) and server-enforced subscription/voucher entitlements (`subscription_plan`, `subscription_entitlement`, `sponsor_voucher`, `subscription_usage_counter`). · **Context:** `Doc 02 §3.1, §4.1, §6.4`, `02B-ENG`, `Doc 03 §1.7, §2, §13, §14`, `Volume A §2`, `BRD-v2A §1–§2`. · **Hard Rules:** Rule 7 (`journal_entry` stays 100% append-only; maker-checker uses pre-ledger `expense_voucher`), Rule 8 (re-query permissions and entitlements on every request, fail-closed). · **Review:** **Level A** (schema constraints & RBAC).

- [x] **S1.8.M1** (≈8 min) — Draft §1 of `Doc/02C-ENG-Commercial-Farm-RBAC-and-Subscription-Schema-Addendum.md`: extend `organization_type` with `'commercial_farm'`, define `identity.farm_staff_assignment` (7 roles from `farm_owner` down to `field_worker` with `scoped_production_unit_ids` filtering), link `field_tenancy` / `journal_entry`, and define pre-ledger `ledger.expense_voucher` + the 7-role permission matrix [Doc 02 §3.1, §4.1, §6.4; `Volume A §2`; `AGENTS.md Rules 7, 8`]
- [x] **S1.8.M2** (≈8 min) — Draft §2–§4 of `Doc/02C-ENG-Commercial-Farm-RBAC-and-Subscription-Schema-Addendum.md`: `identity.subscription_plan`, `identity.subscription_entitlement`, `identity.sponsor_voucher`, `identity.subscription_usage_counter` DDL, `/v1/farms/{orgId}/staff` and `/v1/subscriptions/*` endpoints, 4 error codes (`SUBSCRIPTION_TIER_REQUIRED`, `STAFF_SEAT_LIMIT_REACHED`, `UNIT_SCOPE_FORBIDDEN`, `FINANCIAL_SURFACE_RESTRICTED`), and anchored change log [Doc 03 §2, §13, §14; `BRD-v2A §1–§2`; `PRECEDENCE.md #25`]

---

## Sprint 2 — Self-service registration (M0)

> **Phase 0 · Focus:** NIN provider and SMS clients (typed `HttpClient`, resilience, sandbox + mock); self-service registration, OTP, lockout, Flutter flow with preserved form state; race test — two simultaneous registrations, one NIN → exactly one wins. **Exit ticket:** WP-20…WP-22 done; registration AC walk incl. provider-down fallback. **Primary:** Doc 05 §3–4; `01A` §10.1; Volume A FR-ID-001; Doc 12 §3.2. (Document 19 WP 20–22)

### Task 2.1 — WP-20: NIN provider and SMS clients: typed `HttpClient`, resilience, sandbox + mock

- [x] **S2.1.M1** (≈8 min) — Typed `HttpClient` for the NIN provider with resilience policies (retry/circuit) [Doc 05 §3–4; `01A` §10.1]
- [x] **S2.1.M2** (≈8 min) — Typed `HttpClient` for the SMS provider with resilience policies [Doc 05 §3–4]
- [x] **S2.1.M3** (≈6 min) — Sandbox mode + mocks for both providers so tests run offline [`01A` §10.1]

### Task 2.2 — WP-21: Self-service registration, OTP, lockout, Flutter flow with preserved form state

- [x] **S2.2.M1** (≈8 min) — Registration endpoint + OTP issue/verify with rate limiting and lockout [Volume A FR-ID-001]
- [x] **S2.2.M2** (≈8 min) — Flutter registration wizard with form state preserved across offline/reconnect [Volume A FR-ID-001]
- [ ] **S2.2.M3** (≈6 min) — Demo: a real farmer registers on a real device [Volume A FR-ID-001]

### Task 2.3 — WP-22: Race test — two simultaneous registrations, one NIN → exactly one wins

- [x] **S2.3.M1** (≈8 min) — Genuinely-parallel race test: two simultaneous registrations with one NIN via the barrier-synchronised harness [Doc 12 §3.2]
- [x] **S2.3.M2** (≈6 min) — Assert exactly one winner; BR-A-01 holds under real contention [Doc 12 §3.2]

---

## Sprint 3 — Agent-assisted registration and fobs (M0)

> **Phase 0 · Focus:** Tablet device binding; agent session; VoiceOTP; liveness compared then discarded (verified, not asserted); fob lifecycle with no reactivation path at any privilege level; registration attestation record + plain-language/audio summary and pilot consent sheet (`WP-D04`); organization verification gate before any agent can be added; printable paper fallback kit & NFC fob/tablet hardware spec (`WP-D05`). **Exit ticket:** WP-30…WP-33, WP-D04, WP-D05 done. **Primary:** Volume A FR-ID-002; Doc 02 §3.1; Doc 05 §3.3; Doc 11 §5.1; Doc 13 §10.1; Doc 15 §3, §4.3, §12, gap 5; Doc 17 §18 gaps 2–3; BR-A-02, BR-A-03. (Document 19 WP 30–33 + WP-D04, WP-D05)

### Task 3.1 — WP-30: Tablet device binding; agent session; VoiceOTP; liveness compared, then discarded

- [x] **S3.1.M1** (≈8 min) — Tablet device binding (device fingerprint + binding record) [Volume A FR-ID-002; Doc 05 §3.3]
- [x] **S3.1.M2** (≈8 min) — Agent session + VoiceOTP flow [Volume A FR-ID-002; Doc 05 §3.3]
- [x] **S3.1.M3** (≈6 min) — Liveness compared, then discarded — verified, not asserted [Doc 05 §3.3]

### Task 3.2 — WP-31: Fob lifecycle — no reactivation path exists at any privilege level

- [x] **S3.2.M1** (≈8 min) — Fob issuance and revocation states [Doc 02 §3.1]
- [x] **S3.2.M2** (≈8 min) — Enforce: no reactivation path at any privilege level; no remote reissue [BR-A-02; Doc 02 §3.1]
- [x] **S3.2.M3** (≈6 min) — Test proving revocation is permanent [Doc 02 §3.1]

### Task 3.3 — WP-32: Registration attestation record (explanation given, language, teach-back outcome)

- [x] **S3.3.M1** (≈8 min) — Registration attestation record: explanation given, language, teach-back outcome [Doc 15 §4.3, gap 5]
- [x] **S3.3.M2** (≈6 min) — Attestation record is auditable and append-only [Doc 15 §4.3]

### Task 3.4 — WP-33: Organization verification gate before any agent can be added

- [x] **S3.4.M1** (≈8 min) — Organization verification gate; agents creatable only after the org is verified [BR-A-03; Volume A]
- [x] **S3.4.M2** (≈6 min) — Test: agent add rejected before organization verification [BR-A-03]

### Task 3.5 — WP-D04: `Doc/11A-LEGAL-Plain-Language-Summary-and-Pilot-Consent.md` — Plain-Language & Audio Summary of Terms/Privacy + Pilot Consent Sheet

> **Task Card (`WP-D04`):** **Goal:** Draft the plain-language ToS/Privacy summary, IVR audio script, and decoupled Pilot Participation Consent sheet required by `Doc 11 §5.1`, `Doc 13 §10.1`, and `WP-32`. · **Context:** `Doc 11 §5.1, §5.2`, `Doc 10 §4.5, LEG-03`, `Doc 13 §10.1, §10.5`, `Doc 15 §4.3`, `Doc 17 §18 gap 2`, `PRECEDENCE.md P7, P11`. · **Hard Rules:** Rule 1 (no raw NIN), Rule 7 (append-only attestation), `P7` (no audio capture), `P11` (don't hard-wire "consent" into schema names). · **Review:** **Level A** (legal/consent surface; requires counsel & DPO sign-off before field use).

- [x] **S3.5.M1** (≈8 min) — Draft Part A of `Doc/11A-LEGAL-Plain-Language-Summary-and-Pilot-Consent.md`: one-page plain-language summary of the Terms of Service and Privacy Policy (zero legal jargon, mapped 1:1 to `Doc 15 §4.3`'s eight messages and `Doc 11 §3–§4`), marked *Draft for Counsel & DPO Review* [Doc 11 §5.1 item 1; Doc 15 §4.3]
- [x] **S3.5.M2** (≈8 min) — Draft Part B: IVR / Audio Script & Spoken Prompts (<90 seconds per language, structured into short numbered blocks with comprehension pauses and translation tables for Hausa, Yoruba, Igbo, and Nigerian Pidgin) [Doc 11 §5.1 item 2, §5.2; Doc 14 §4]
- [x] **S3.5.M3** (≈8 min) — Draft Part C: Pilot Participation Informed Consent & Partner Decoupling Notice (`Doc 13 §10.1`), separating pilot research participation from platform data processing, stating the partner-benefit decoupling guarantee, withdrawal rights (`Doc 13 §10.5`), and honest deletion/anonymization limits (`Doc 11 §4.9`) [Doc 13 §10.1, §10.5; Doc 17 §6.3, §18 gap 2]
- [x] **S3.5.M4** (≈6 min) — Define Part D: Attestation Schema Mapping (`RegistrationAttestation` fields: `summary_version`, `language_code`, `delivery_mode`, `teach_back_passed`, `agent_identity_id`, `recorded_at` — no audio capture per `P7`) so `WP-32` binds to a versioned text [Doc 11 §5.1 item 4; Doc 15 §18 gap 5; `PRECEDENCE.md P7, P11`]

### Task 3.6 — WP-D05: `Doc/15A-OPS-Paper-Fallback-Kit-and-Hardware-Spec.md` — Printable Paper Fallback Kit & NFC Fob/Tablet Hardware Specification

> **Task Card (`WP-D05`):** **Goal:** Produce the printable outage paper kit (`Doc 15 §3, §12`) and the NFC fob + extension-agent tablet hardware specification (`Doc 17 §18 gap 3`). · **Context:** `Doc 15 §2 Rule 3, §3, §5, §11, §12, §20`, `Doc 09 §10`, `Doc 17 §8, §16 G-11, §18 gap 3`, `Farm-Basics-Design-System-2.md §21`. · **Hard Rules:** Rule 1 / `Doc 15` Rule 3 (never write a NIN, OTP, or PIN on any paper form or store PII on a fob). · **Review:** Level B.

- [x] **S3.6.M1** (≈8 min) — Draft Part A of `Doc/15A-OPS-Paper-Fallback-Kit-and-Hardware-Spec.md`: Printable Outage Paper Kit templates (Form 1: Pre-Registration Visit Sheet; Form 2: Field Activity Log Sheet; Form 3: Fob Custody & Issue Log; Form 4: Safety/Security Incident Sheet) with bold **NO-NIN / NO-PIN / NO-OTP** compliance banners and `Doc 15 §12.3` 30-day destruction custody tracking [Doc 15 §2 Rule 3, §3, §12, §20; Doc 09 §10]
- [x] **S3.6.M2** (≈8 min) — Draft Part B: NFC Fob Hardware Specification — tag IC standard (ISO/IEC 14443 Type A / NTAG213 or NTAG215), UID/serial read-only rules (zero PII on tag), IP67/IP68 weatherproof epoxy/ABS keychain housing for tropical fieldwork, laser-etched human-readable fallback serial, and batch pre-encoding QA checklist [Doc 15 §5, §11; Doc 17 §18 gap 3]
- [x] **S3.6.M3** (≈8 min) — Draft Part C: Extension Agent Tablet & Farmer Low-End Device Specification — minimum Android OS/RAM/storage floor (Android 8+ for agent NFC tablets; Android 5.0 / 1 GB RAM floor for farmer phones per DS2), integrated NFC reader placement, multi-constellation GNSS accuracy requirements for boundary walks, battery capacity for 10-hour field days, and procurement lead-time checklist [Doc 17 §16 G-11, §18 gap 3; `Farm-Basics-Design-System-2.md §21`]

---

## Sprint 4 — Review engine, trust console, platform services (M0)

> **Phase 0 · Focus:** Review engine (categories, locking assignment with COI exclusion, SLA job, verdict write-back registry); NIN-mismatch review end to end + admin console queue + time-boxed trust override; `MasterDataChangeRequest` with DB-enforced no-self-approval; notification layer + Capability Modules infrastructure; content governance workflow spec (`WP-D06`), reviewer gold-case library (`WP-D07`), and NDPA DPIA & RoPA (`WP-D08`). **Exit ticket:** WP-40…WP-43 and WP-D06…WP-D08 done; then assert the M0 exit gate. **Primary:** Doc 01 §9; Doc 02 §3.2, §7; Doc 06 §3, §5; Doc 10 §4; Doc 18 §4, §8.3, §9.8; Volume D FR-IT-001/002; Master §18. (Document 19 WP 40–43 + WP-D06, WP-D07, WP-D08)

### Task 4.1 — WP-40: Review engine — categories, locking assignment with COI exclusion, SLA job, verdict write-back registry

- [x] **S4.1.M1** (≈8 min) — Review categories + `ReviewQueueItem` lifecycle [Doc 01 §9; Doc 02 §3.2]
- [x] **S4.1.M2** (≈8 min) — Locking assignment with COI exclusion; concurrency-safe [Doc 02 §3.2]
- [x] **S4.1.M3** (≈6 min) — SLA job + verdict write-back registry [Doc 01 §9]

### Task 4.2 — WP-41: NIN-mismatch review end to end, plus admin console queue and time-boxed trust override

- [x] **S4.2.M1** (≈8 min) — NIN-mismatch review flow end to end [Volume D FR-IT-001/002]
- [x] **S4.2.M2** (≈8 min) — Admin console review queue + time-boxed trust override [Volume D FR-IT-001/002]

### Task 4.3 — WP-42: `MasterDataChangeRequest` with DB-enforced no-self-approval

- [x] **S4.3.M1** (≈8 min) — `MasterDataChangeRequest` entity + workflow [Doc 02 §7]
- [x] **S4.3.M2** (≈6 min) — DB-enforced no-self-approval constraint [Doc 02 §7]

### Task 4.4 — WP-43: Notification layer; Capability Modules infrastructure

- [x] **S4.4.M1** (≈8 min) — Notification layer: templates, dispatch, dedup [Master §18]
- [x] **S4.4.M2** (≈6 min) — Capability Modules infrastructure scaffold [Master §18]

### Task 4.5 — WP-D06: `Doc/06A-DATA-Content-Governance-and-Lifecycle-Spec.md` — Knowledge Bank & Master-Data Content Governance Workflow Specification

> **Task Card (`WP-D06`):** **Goal:** Define the single authoritative authoring, review, dual-agronomist approval (`BR-B-06`), 12-month re-verification, and emergency-withdrawal lifecycle for all governed content and master data (closing `F-13`, `F-25(7)`, and `F-28(7)`). · **Context:** `Doc 06 §3, §5`, `Doc 02 §5.3, §7`, `02B §3.4, §3.7, §3.8`, `Part A §9`, `Part B §4, §11`, `Doc 18 §4.4, §4.5, §10 gap 5`, `PRECEDENCE.md P17`, `Followups-Ledger.md F-13, F-25(7), F-28(7)`. · **Hard Rules:** Rule 11 (regulated products reference catalog IDs only), no self-approval (`chk_no_self_approval`). · **Review:** **Level A** (safety-critical chemical/nutrient governance). · **Stop and ask if:** enabling emergency withdrawal in code before `P17` is decided.

- [x] **S4.5.M1** (≈8 min) — Draft `Doc/06A-DATA-Content-Governance-and-Lifecycle-Spec.md` header and §1–§2: unified state machine (`Draft` → `InReview` → `Approved` → `Published` → `ReverificationDue` → `Withdrawn` / `Archived`) across `KnowledgeArticle`, `CropProfile`, `GrowingMethodProfile`, `ChemicalProduct`, `FertilizerProduct`, and `NutrientSolutionProduct` [`Followups-Ledger F-13`; `Part A §9`; `Part B §4, §11`; `02B §3.8`]
- [x] **S4.5.M2** (≈8 min) — Specify §3 Dual-Agronomist Gate (`BR-B-06`) & No-Self-Approval (`chk_no_self_approval`): authoring role constraints (`chk_regulated_content_author`), routing to `KB_CHEMICAL_PUBLISH` with mandatory blind secondary review (`FR-RV-003`), and required provenance columns (`source`, `reviewed_by`, `version`, `last_verified_at` per `F-25(7)`) [Doc 02 §5.3, §7; `02B §3.7`; `Followups-Ledger F-25(7)`]
- [x] **S4.5.M3** (≈8 min) — Specify §4 12-Month Re-Verification Sweep & `Banned` Status Cascade: Hangfire job specification (`01A §6.2`) querying `last_verified_at < now() - interval '12 months'`, plus immediate cascade rules when a chemical/nutrient product transitions to `Banned` (`F-28(7)` — delist active vendor listings, flag dependent articles, notify affected users) [`01A §6.2`; Doc 02 §5.3; `Followups-Ledger F-28(7)`]
- [x] **S4.5.M4** (≈8 min) — Specify §5 Emergency Withdrawal Protocol for `KB_CHEMICAL_FLAGGED` (`Doc 18 §10 gap 5` / `P17`): 24-hour SLA queue rules, provisional safety-warning banner vs. emergency withdrawal authority (flagged behind decision `P17`), and offline client cache invalidation on next sync [Doc 18 §4.5, §10 gap 5; `PRECEDENCE.md P17`]

### Task 4.6 — WP-D07: `Doc/18A-OPS-Reviewer-Gold-Case-Library-and-Calibration-Pack.md` — Reviewer Gold-Case Library & Calibration Test Pack

> **Task Card (`WP-D07`):** **Goal:** Create the synthetic gold-case test pack across all 11 seeded review categories required for reviewer assessment, blind QA calibration, and the blocking timed dry-run (`Doc 18 §8.3, §9.8, §11`). · **Context:** `Doc 18 §4.1–4.11, §5, §8.3, §9.8, §10, §11`, `Part A §2.4`, `02B §3.6`, `PRECEDENCE.md #18, P16–P18`. · **Hard Rules:** Rule 1 (synthetic data only; no real NINs or PII), `P16` (load verdict codes as configuration). · **Review:** Level B (each category card marked *Scaffold for Domain Expert Sign-Off* per `Doc 18 §0 Caution 1`).

- [x] **S4.6.M1** (≈8 min) — Scaffold `Doc/18A-OPS-Reviewer-Gold-Case-Library-and-Calibration-Pack.md` with header, timed dry-run protocol (`Doc 18 §9.8`), blind QA re-review scoring rubric (`Doc 18 §5`), and the standard verdict vocabulary configuration table (`P16`) [Doc 18 §5, §8.3, §9.8; `PRECEDENCE.md P16`]
- [x] **S4.6.M2** (≈8 min) — Author Gold Cases for Phase 0–1 categories (`IDENTITY_NIN_MISMATCH` and `ORG_VERIFICATION`): 6 synthetic cases each covering benign name variations (transliteration/compound surname with DOB match), request-correction, clear reject, COI self-recusal trigger, and coordinated agent-fraud escalation [Doc 18 §4.1, §4.2, §9.9]
- [x] **S4.6.M3** (≈8 min) — Author Gold Cases for Phase 2 categories (`FIELD_OVERLAP_DISPUTE`, `LAB_ACCREDITATION` with per-family `soil`/`solution` scope per `02B §3.6`, and `VENDOR_TRUST_TIER`): 4 synthetic cases each with expected `Found / Because / Therefore` rationale and expected write-back assertion [Doc 18 §4.6, §4.7, §4.10, §9.9; `02B §3.6`]
- [x] **S4.6.M4** (≈8 min) — Author Gold Cases for Phase 3 categories (`PEST_DISEASE_REPORT`, `KB_CHEMICAL_PUBLISH`, `KB_CHEMICAL_FLAGGED`, `CERTIFICATION_VERIFY`, `CERTIFICATION_FRAUD_FLAG` including cooperative group-certificate edge case `P18`, `FORUM_PROMOTION`), marked *Scaffold for Domain Expert Sign-Off* [Doc 18 §0, §4.3–4.5, §4.8, §4.9, §4.11, §10 gap 6; `PRECEDENCE.md P18`]

### Task 4.7 — WP-D08: `Doc/10A-LEGAL-NDPA-DPIA-and-RoPA.md` — NDPA 2023 Data Protection Impact Assessment (DPIA) & Record of Processing Activities (RoPA)

> **Task Card (`WP-D08`):** **Goal:** Prepare the formal NDPA 2023 DPIA and Record of Processing Activities (RoPA) required to clear the M0 exit gate (`Doc 10 LEG-02/03` and `F-27`). · **Context:** `Doc 10 §4, LEG-02, LEG-03`, `Doc 11 §4.3, §4.9, §4.10, §4.12`, `01A §5.1, §5.3, §12`, `PRECEDENCE.md P7, P10, P11, P12`, `Followups-Ledger.md F-27`. · **Hard Rules:** `P10` (do not build NDPA erasure endpoint until counsel sign-off), `P11` (don't hard-wire "consent" into schema names), `P12` (synthetic data only on staging). · **Review:** **Level A** (blocking legal/compliance gate for M0 exit).

- [x] **S4.7.M1** (≈8 min) — Draft Part A of `Doc/10A-LEGAL-NDPA-DPIA-and-RoPA.md`: Record of Processing Activities (RoPA) enumerating every personal data category (NIN verification ref, ephemeral liveness photo, phone/VoiceOTP, geodesic boundary/centroid, ledger entries, support tickets), data subject class, retention schedule (`Doc 11 §4.10`), and mapped NDPA 2023 lawful basis (`P11`) [Doc 10 §4.5; Doc 11 §4.3, §4.10; `PRECEDENCE.md P11`]
- [x] **S4.7.M2** (≈8 min) — Draft Part B: High-Risk Processing DPIA — biometric liveness comparison & immediate deletion (`WP-30`), vulnerable/low-literate data subjects registered via extension agents (`WP-32`), and AI-assisted classification safeguards [Doc 10 §4.2–4.5; Doc 05 §3.3]
- [x] **S4.7.M3** (≈8 min) — Draft Part C: Cross-Border Transfer & Residency Assessment — evaluation matrix for production VM hosting region (`P12`), `wal-g` off-region encrypted backup target (`D-12` / `F-26(1)`), and the Cloudflare API-path WAF toggle (`D-10b` / `D-19`) for licensed counsel sign-off [Doc 10 §4.6; `01A §5.1, §5.3, §12`; `PRECEDENCE.md P12`]
- [x] **S4.7.M4** (≈8 min) — Draft Part D: Data Subject Rights & Anonymization-vs-Erasure Specification (`P10`) — technical specification of how an erasure request severs identity PII while preserving immutable `journal_entry`, `audit_log_entry`, and `field_tenancy` provenance records (marked *Do Not Build Endpoint Until Counsel Signs Off* per `P10`), plus the 72-hour NDPC breach notification runbook [Doc 10 §4.7–4.9; Doc 11 §4.9, §4.12; `PRECEDENCE.md P10`; `Followups-Ledger F-27`]

### Task 4.8 — WP-D13: `Doc/05A-ENG-PayGuard-Bank-Escrow-Ledger-Addendum.md` — PayGuard Non-Custodial Commercial Bank Escrow Ledger Architecture

> **Task Card (`WP-D13`):** **Goal:** Specify the 3-tier non-custodial escrow integration (`Farm Basics` → `PayGuard Non-Custodial Orchestrator` → `Licensed Nigerian Commercial Bank Escrow Ledger`), webhook timestamp replay hardening (`F-26(3)`), and split-release support (`F-25(5)`). · **Context:** `Doc 05 §2`, `Doc 02 §6.3–6.4`, `Part A §12.5`, `10B-LEGAL §1`. · **Hard Rules:** Rule 13 (constant-time HMAC + timestamp window), `BR-C-06` dual sign-off. · **Review:** **Level A**.

- [x] **S4.8.M1** (≈8 min) — Draft §1 of `Doc/05A-ENG-PayGuard-Bank-Escrow-Ledger-Addendum.md`: 3-tier sequence topology (`Farm Basics` → `PayGuard` → `Licensed Nigerian Commercial Bank Escrow Ledger`), non-custodial invariants (`BR-ESC-01`–`BR-ESC-05`), and NIBSS NIP virtual escrow account flow [Doc 05 §2.1; `Part A §12.5`; `PRECEDENCE.md #26`]
- [x] **S4.8.M2** (≈8 min) — Draft §2–§3 of `Doc/05A-ENG-PayGuard-Bank-Escrow-Ledger-Addendum.md`: `bankEscrowLedger` payload contract, 5-minute `X-PayGuard-Timestamp` HMAC replay window (`F-26(3)`), `escrow.split_released` partial settlement handling (`F-25(5)`), and anchored change log [Doc 05 §2.2–2.5; `Followups-Ledger F-25(5), F-26(3)`]

### Task 4.9 — WP-D14: `Doc/10B-LEGAL-PayGuard-Escrow-and-WhatsApp-NDPA-Addendum.md` — PayGuard Commercial Bank Escrow Posture & NDPA 2023 WhatsApp / Unconsented Data-Sharing Prohibitions

> **Task Card (`WP-D14`):** **Goal:** Author the legal addendum superseding `Doc 10 §3.2` ("Possibility A/B/C") with the locked 3-tier Commercial Bank Escrow Ledger structure (~₦5B CBN license avoidance) and codifying the NDPA 2023 unconsented data-sharing precedent (First Bank / Access Bank `₦50M` case) and the 5 WhatsApp Group Compliance Traps. · **Context:** `Doc 10 §3.1–3.3, §4`, `10A-LEGAL`, `Doc 11 §3.5.2, §4.4, §4.6`. · **Hard Rules:** Zero modifications to existing `Doc/` files. · **Review:** **Level A**.

- [x] **S4.9.M1** (≈8 min) — Draft Part 1 of `Doc/10B-LEGAL-PayGuard-Escrow-and-WhatsApp-NDPA-Addendum.md`: supersede `Doc 10 §3.2` speculative possibilities with the locked `Farm Basics → PayGuard → Licensed Nigerian Commercial Bank Escrow Ledger` structure, document the ~₦5B CBN license/security deposit barrier, and provide the amended `Doc 11 §3.5.2` disclosure copy [Doc 10 §3.1–3.3; Doc 11 §3.5.2; `PRECEDENCE.md #26`]
- [x] **S4.9.M2** (≈8 min) — Draft Part 2 & §3–§4 of `Doc/10B-LEGAL-PayGuard-Escrow-and-WhatsApp-NDPA-Addendum.md`: document the First Bank / Access Bank `₦50M` unconsented data-sharing precedent, the 5 NDPA 2023 WhatsApp Group Compliance Traps (Add Without Consent, Admin as Data Controller, Bulk Mining/Scraping Breach, Screenshot Forwarding, Loss of Household Exemption), `DPIA-R09`, and anchored change log [Doc 10 §4; `10A-LEGAL Part B`; Doc 11 §4.4; `PRECEDENCE.md #27`]

---

## M0 exit gate (Phase 0)

All must hold before Sprint 5 begins (Document 19 §4):

- [ ] Document 12 §8's Phase 0 list passing: Identity/RBAC Tier 1–3 tests; NIN-uniqueness race; Geography validated
- [ ] A **second** restore drill passes (in addition to the Sprint 0 drill)
- [ ] DPO designated and the consent-basis question decided (`WP-D08` / Doc 10 LEG-02/03)
- [ ] Production hosting region decided (`WP-D08` / Doc 10 §4.6)

---

## Sprints 5–6 — Production unit, walked boundary (M1)

> **Phase 1 · Focus:** built to Doc 02B's generalized schema, but only `gps_walked_boundary` is enabled — the other capture methods exist in the schema and are flagged off (`cea.*` off). `production_unit` schema exactly per 02B §3.2 (all four capture shapes + `chk_capture_shape` day one, derive trigger, raw SQL migration reviewed against 02B); tracking session + idempotent batch point sync; geodesic area server-side via PostGIS (client number ignored; self-intersection repair <3% or reject; raw points retained); Flutter walk UI (GPS states, pause/resume, "almost complete" non-text cue, fully offline); adversarial geometry suite (±5%); offline suite (days offline, mid-walk loss, duplicate GUID, real timestamps); consumer-audit tests for null `boundary`/`centroid`. **Deliberately two sprints — the highest technical risk in Phase 1.** **Exit ticket:** WP-50…WP-56 done. **Primary:** 02B §3.1–3.2, §5, §8; `01A` §6.1; Volume A FR-FB-001, F-FB-04/05, §15.1; Doc 03 §3; Doc 12 §4.2, §4.4; Doc 14 §3; BR-E-04. (Document 19 WP 50–56)

### Task 5.1 — WP-50: `production_unit` schema exactly per 02B §3.2

- [x] **S5.1.M1** (≈8 min) — `production_unit` table with all four capture-shape enum values [02B §3.1–3.2]
- [x] **S5.1.M2** (≈8 min) — `chk_capture_shape` and `chk_medium_class` CHECK constraints present from day one [02B §3.2]
- [x] **S5.1.M3** (≈8 min) — Derive trigger: area server-derived from geography, never client-supplied [02B §3.2]
- [x] **S5.1.M4** (≈8 min) — Raw SQL migration reviewed line-by-line against 02B DDL; flag `cea.*` off so only `gps_walked_boundary` is enabled [02B §3.2; `01A` §6.1; Document 19 §6]

### Task 5.2 — WP-51: Tracking session and idempotent batch point sync

- [x] **S5.2.M1** (≈8 min) — Tracking session entity; client-recorded timestamps preserved [Volume A FR-FB-001; Doc 03 §3]
- [x] **S5.2.M2** (≈8 min) — Idempotent batch point sync with `ClientGuid` + sequence ordering [Volume A FR-FB-001]
- [x] **S5.2.M3** (≈8 min) — Server pipeline: accuracy filter, outlier removal, simplification, ring closure [Volume A FR-FB-001]

### Task 5.3 — WP-52: Geodesic area server-side via PostGIS; self-intersection repair <3% or reject; raw points retained

- [x] **S5.3.M1** (≈8 min) — Geodesic area via PostGIS `geography`; client-supplied area ignored [Volume A F-FB-04/05; Doc 12 §4.4]
- [x] **S5.3.M2** (≈8 min) — Self-intersection: repair if <3%, else reject [Doc 12 §4.4]
- [x] **S5.3.M3** (≈6 min) — Raw points retained alongside derived geometry [Volume A F-FB-05]

### Task 5.4 — WP-53: Flutter walk UI — GPS states, pause/resume, "almost complete" non-text cue, fully offline

- [x] **S5.4.M1** (≈8 min) — Flutter walk UI: GPS states (acquiring/active/paused) [Volume A §15.1; Doc 14 §3]
- [x] **S5.4.M2** (≈8 min) — Pause/resume; "almost complete" cue via tone/vibration, not text only [Doc 14 §3]
- [x] **S5.4.M3** (≈6 min) — Fully offline walk with local storage [Volume A §15.1]

### Task 5.5 — WP-54: Adversarial geometry suite — large, high-latitude, self-crossing, tiny; ±5% tolerance

- [x] **S5.5.M1** (≈8 min) — Area accuracy tests: large and high-latitude polygons within ±5% [Doc 12 §4.4]
- [x] **S5.5.M2** (≈6 min) — Figure-eights and near-zero-area polygons: repair or reject [Doc 12 §4.4]

### Task 5.6 — WP-55: Offline suite — sync after days offline, mid-walk connectivity loss, duplicate GUID, real timestamps preserved

- [x] **S5.6.M1** (≈8 min) — Sync after days offline works [Doc 12 §4.2]
- [x] **S5.6.M2** (≈8 min) — Mid-walk connectivity loss and resume [Doc 12 §4.2]
- [x] **S5.6.M3** (≈6 min) — Duplicate GUID rejected as replay; real timestamps preserved [Doc 12 §4.2]

### Task 5.7 — WP-56: Consumer-audit tests for null `boundary`/`centroid`

- [x] **S5.7.M1** (≈8 min) — Consumer-audit test per row of 02B §5 against units with null `boundary` and null `centroid` [02B §5, §8]
- [x] **S5.7.M2** (≈6 min) — Tests committed before any consumer depends on null geometry [BR-E-04; 02B §5]

---

## Sprint 7 — Crop lifecycle (M1)

> **Phase 1 · Focus:** `CropProfile` governed master data + pilot-crop seed (agronomist-validated); `CropCycle` state machine with 100% of valid and invalid transitions tested and the one-active-cycle-per-open-field partial unique index; task catalog, `OperationLog` validation (OK/Warning/Flag), stage gates, override that never erases the original flag; Flutter task list with flag/override UX and real task date preserved. **Exit ticket:** WP-70…WP-73 done. **Primary:** Volume A §4.2, §4.3, §15.2; Doc 02B §3.3; Doc 06 §3; Doc 12 §4.3; BR-A-07. (Document 19 WP 70–73)

### Task 7.1 — WP-70: `CropProfile` governed master data + pilot-crop seed

- [x] **S7.1.M1** (≈8 min) — `CropProfile` master data governed via `MasterDataChangeRequest` (no self-approval) [Volume A §4.2; Doc 02 §7]
- [x] **S7.1.M2** (≈8 min) — Pilot-crop seeding with agronomist-validated content [Doc 06 §3]

### Task 7.2 — WP-71: `CropCycle` state machine; one-active-cycle-per-open-field index

- [x] **S7.2.M1** (≈8 min) — `CropCycle` state machine, forward-only stage transitions [Doc 12 §4.3; 02B §3.3]
- [x] **S7.2.M2** (≈8 min) — 100% of valid and invalid transitions tested [Doc 12 §4.3]
- [x] **S7.2.M3** (≈6 min) — Partial unique index: one active cycle per open field [BR-A-07; 02B §3.3]
- [x] **S7.2.M4** (≈6 min) — Race test proving BR-A-07 under real contention [02B §3.3]

### Task 7.3 — WP-72: Task catalog, `OperationLog` validation (OK/Warning/Flag), override never erases the original flag

- [x] **S7.3.M1** (≈8 min) — Task catalog seeded from the stage model [Volume A §4.3]
- [x] **S7.3.M2** (≈8 min) — `OperationLog` validation engine: OK / Warning / Flag [Volume A §4.3]
- [x] **S7.3.M3** (≈6 min) — Override that never overwrites the original flag [Volume A §4.3]

### Task 7.4 — WP-73: Flutter task list, flag/override UX, real task date preserved

- [x] **S7.4.M1** (≈8 min) — Flutter task list with validation states [Volume A §15.2]
- [x] **S7.4.M2** (≈8 min) — Plain-language flag explanation + override UX [Volume A §15.2]
- [x] **S7.4.M3** (≈6 min) — Real task date preserved offline [Volume A §15.2]

---

## Sprint 8 — The ledger (M1)

> **Phase 1 · Focus:** Chart of Accounts (accountant sign-off first), `JournalEntry`/lines with the deferred balance trigger, `UPDATE`/`DELETE` denied at the DB privilege level; posting rules as config, outbox + Hangfire worker, exactly-once posting, cost-centre dimension; `AccountBalance` cache with nightly reconciliation that alerts rather than silently fixes; computed P&L/balance sheet/trial balance. **Exit ticket:** WP-80…WP-82 done. **Primary:** Doc 02 §5.4–5.5, §6.4; Doc 06 §4; Doc 08 §10; Doc 12 §3.1; `01A` §6.3. (Document 19 WP 80–82)

### Task 8.1 — WP-80: Chart of Accounts; `JournalEntry`; deferred balance trigger; UPDATE/DELETE denied at DB privilege level

- [x] **S8.1.M1** (≈8 min) — Chart of Accounts with accountant sign-off first [Doc 02 §6.4; Doc 06 §4]
- [x] **S8.1.M2** (≈8 min) — `JournalEntry`/lines with double-entry enforcement [Doc 02 §6.4]
- [x] **S8.1.M3** (≈8 min) — Deferred balance trigger; `UPDATE`/`DELETE` denied at the DB privilege level [Doc 02 §6.4; Doc 12 §3.1]

### Task 8.2 — WP-81: Posting rules as config; outbox + Hangfire; cost-centre dimension

- [x] **S8.2.M1** (≈8 min) — Config-driven posting rules [`01A` §6.3]
- [x] **S8.2.M2** (≈8 min) — Outbox + Hangfire worker, exactly-once posting, survives a restart [`01A` §6.3]
- [x] **S8.2.M3** (≈6 min) — Cost-centre dimension on postings [`01A` §6.3]

### Task 8.3 — WP-82: `AccountBalance` cache; nightly reconciliation that alerts, never silently fixes; computed statements

- [x] **S8.3.M1** (≈8 min) — `AccountBalance` cache [Doc 02 §5.4–5.5]
- [x] **S8.3.M2** (≈8 min) — Nightly reconciliation that alerts on drift rather than silently fixing [Doc 02 §5.5; Doc 08 §10]
- [x] **S8.3.M3** (≈6 min) — Computed P&L / balance sheet / trial balance [Doc 02 §5.4–5.5]

---

## Sprint 9 — Harvest, operations console, and support readiness (M1)

> **Phase 1 · Focus:** Harvest batches and disposition; Field Operations console (agents, tablets, quality signals, small-sample-aware); analytics baseline scope-respecting (a Region admin sees only their region); support enablement (CustomerSupportAgent read-view, `support_case_ref` on audited actions, decision-recorded fob-revocation route); in-person, agent-verified phone change flow. **Exit ticket:** WP-90…WP-94 done; then the M2/pilot-readiness gate. **Primary:** Volume A; Volume E FR-GM-002; Volume D FR-FO-001–003; Doc 03 §13; Doc 16 §9.2, gaps 1/3/4/6. (Document 19 WP 90–94)

### Task 9.1 — WP-90: Harvest batches and disposition

- [x] **S9.1.M1** (≈8 min) — Harvest batch entity + disposition workflow [Volume A; Volume E FR-GM-002]

### Task 9.2 — WP-91: Field Operations console — agents, tablets, quality signals (small-sample-aware)

- [x] **S9.2.M1** (≈8 min) — Field Ops console: agent roster, tablet fleet [Volume D FR-FO-001–003]
- [x] **S9.2.M2** (≈6 min) — Quality signals, small-sample-aware [Volume D FR-FO]

### Task 9.3 — WP-92: Analytics baseline, scope-respecting

- [x] **S9.3.M1** (≈8 min) — Analytics baseline dashboard [Doc 03 §13]
- [x] **S9.3.M2** (≈6 min) — Scope enforced on reads: a Region admin sees only their region [Doc 03 §13]

### Task 9.4 — WP-93: Support enablement — read-view, `support_case_ref`, fob revocation route (once decided)

- [x] **S9.4.M1** (≈8 min) — `CustomerSupportAgent` read-view + `support_case_ref` on audited actions [Doc 16 §9.2, gaps 1/3/4]
- [x] **S9.4.M2** (≈6 min) — Decision-recorded route for support-initiated fob revocation [Doc 16 gap 1]

### Task 9.5 — WP-94: In-person, agent-verified phone change flow

- [x] **S9.5.M1** (≈8 min) — In-person, agent-verified phone change flow (lost-phone path) [Doc 16 gap 6]

---

## Sprint 10 — Operability (M2)

> **Phase 1 Hardening · Focus:** Dashboards, CRITICAL invariant canaries, Alertmanager routing off the VM, and SLO tracking; k6 baseline load test + full concurrency race suite + SAST/DAST/dependency/secret scans; full DR rehearsal (`wal-g` restore to a fresh VM, redeploy, repoint, timed against the 4-hour RTO); accessibility audit and low-literacy icon-comprehension prep. **Exit ticket:** WP-A0…WP-A3 done. **Primary:** Doc 04 §14; Doc 08 §5.1, §8; Doc 09 §7; Doc 12 §4.1; Doc 14 §3–4. (Document 19 WP A0–A3)

### Task 10.1 — WP-A0: Dashboards; CRITICAL invariant canaries; Alertmanager routing off the VM; SLO tracking

- [ ] **S10.1.M1** (≈8 min) — Grafana operational dashboards + SLO tracking panels [Doc 08 §5.1, §8]
- [ ] **S10.1.M2** (≈8 min) — Wire CRITICAL invariant-violation canaries (unbalanced journal entry, partition missing, reconciliation drift) [Doc 08 §5.1]
- [ ] **S10.1.M3** (≈6 min) — Verify Alertmanager routes outbound alerts through external off-VM channels [`01A` §8.2; Doc 08 §8]

### Task 10.2 — WP-A1: k6 baseline load test; SAST/DAST pass; dependency and secret scan clean

- [ ] **S10.2.M1** (≈8 min) — k6 baseline load test + rerun concurrency race suites at full iteration counts [Doc 12 §4.1]
- [ ] **S10.2.M2** (≈8 min) — SAST/DAST security pass + authorization review [Doc 04 §14]
- [ ] **S10.2.M3** (≈6 min) — Dependency audit (14-day cooldown rule) and secret/redaction scan clean [Doc 04 §14; `01A` §7.1]

### Task 10.3 — WP-A2: Full DR rehearsal — restore from `wal-g` to a fresh VM, redeploy, repoint, timed against 4-hour RTO

- [ ] **S10.3.M1** (≈8 min) — Provision fresh VM, restore PostgreSQL+PostGIS from off-provider `wal-g` archive to PITR target [Doc 09 §7; `01A` §5.3]
- [ ] **S10.3.M2** (≈8 min) — Redeploy application containers via `deploy.sh`, repoint DNS, verify data integrity and record measured RTO against the 4-hour target [Doc 09 §7]

### Task 10.4 — WP-A3: Accessibility audit and icon-comprehension prep

- [ ] **S10.4.M1** (≈8 min) — WCAG AA / DS2 contrast (13.1:1 outdoor tokens) and touch-target (48dp/56dp) audit [Doc 14 §3; `Farm-Basics-Design-System-2.md` §19]
- [ ] **S10.4.M2** (≈8 min) — Low-literacy icon-comprehension test protocol and non-reader test pack preparation [Doc 14 §4]

---

## Sprint 11 — Pilot instrumentation, release & pilot collateral (M2)

> **Phase 1 Hardening · Focus:** Pilot telemetry instrumentation (boundary success rate, sustained logging, sync failures); pilot-language localization, Codemagic internal track, TestFlight, staged rollout, and certificate-pinning rotation checklist; break-glass credential provisioned and drilled; runbook and support-script verification; pilot agronomic baseline & participant remediation reserve protocol (`WP-D09`); partner organization MoU template, data-sharing covenant & external pilot brief (`WP-D10`). **Exit ticket:** WP-B0…WP-B3, WP-D09, WP-D10 done; then assert the M2 pilot-readiness gate. **Primary:** `01A` §2.1, §3.3, §7.2; Doc 09; Doc 13 §6–7, §10.2, §12; Doc 15; Doc 16; Doc 17 §5, §14, §18, §20.1. (Document 19 WP B0–B3 + WP-D09, WP-D10)

### Task 11.1 — WP-B0: Pilot telemetry (boundary success rate, sustained logging, sync failures)

- [ ] **S11.1.M1** (≈8 min) — Telemetry hooks for boundary walk first-attempt success rate, sustained task logging, and offline sync failure rates [Doc 13 §6–7]
- [ ] **S11.1.M2** (≈6 min) — Pilot cohort observation dashboard wired without exposing raw farmer PII [Doc 13 §6]

### Task 11.2 — WP-B1: Pilot-language localization; Codemagic internal track, TestFlight, staged rollout; certificate-pinning rotation checklist

- [ ] **S11.2.M1** (≈8 min) — Pilot-language localization bundle wired via `flutter_localizations` + native-speaker verification [`01A` §2.1; Doc 16 §16]
- [ ] **S11.2.M2** (≈8 min) — Codemagic internal track, TestFlight, and staged-rollout release pipeline verified [`01A` §7.2; Doc 07 §8.2]
- [ ] **S11.2.M3** (≈6 min) — TLS certificate-pinning rotation checklist verified in release runbook [`01A` §2.1]

### Task 11.3 — WP-B2: Break-glass credential provisioned and drilled

- [ ] **S11.3.M1** (≈8 min) — Provision sealed offline Type-1 break-glass credential (`04A` §6) and execute end-to-end Keycloak-down recovery drill with retroactive `BreakGlassAction` audit logging [`01A` §3.3; `04A` §6]

### Task 11.4 — WP-B3: Runbook and support-script verification

- [ ] **S11.4.M1** (≈8 min) — Verify Documents 09, 15, and 16 procedures and support scripts against actual shipped Phase 1 behavior [Doc 09; Doc 15; Doc 16]

### Task 11.5 — WP-D09: `Doc/13A-QA-Pilot-Baseline-and-Remediation-Protocol.md` — Pilot Agronomic Baseline Instrument & Participant Remediation Reserve Protocol

> **Task Card (`WP-D09`):** **Goal:** Author the pre-planting agronomic baseline interview instrument (`T−8 weeks`, `Doc 17 §18 gap 4`) and the Participant Remediation Reserve SOP (`Doc 13 §10.2`, `Doc 17 §18 gap 7`). · **Context:** `Doc 13 §6, §7.3, §9.2, §10.2, §12`, `Doc 16 §7.3, §12, §14`, `Doc 17 §16 G-05, §18 gaps 4, 7`. · **Hard Rules:** No written submission required for farmer remediation claims (`Doc 16 §14`); courtesy "yes" responses excluded (`Doc 16 §12`). · **Review:** **Level A** (financial remediation reserve & research ethics gate).

- [x] **S11.5.M1** (≈8 min) — Draft Part A of `Doc/13A-QA-Pilot-Baseline-and-Remediation-Protocol.md`: Pre-Planting Baseline Survey Instrument (`T−8 weeks` per `Doc 17 §18 gap 4`) — structured oral interview guide for the Field Research Coordinator capturing prior-season plot size estimate, crop planted, seed/fertilizer/chemical spend, harvest volume, sale channel, and record-keeping baseline [Doc 13 §7.3, §12; Doc 17 §18 gap 4]
- [x] **S11.5.M2** (≈8 min) — Draft Part B: Mid-Season & End-of-Season Observation Rubric — structured field observation sheets for boundary-walk comprehension, task-list adherence, flag/override reasons, and independent voice-call satisfaction checks (`Doc 16 §12` anti-metric compliance) [Doc 13 §6, §7; Doc 16 §12]
- [x] **S11.5.M3** (≈8 min) — Draft Part C: Participant Remediation Reserve Protocol (`Doc 13 §10.2`, `Doc 17 §18 gap 7`) — per-participant reserve sizing model, ring-fenced custody, oral/agent-mediated claim intake (`Doc 16 §14`), same-day escalation to Founder + Agronomist Advisor + Counsel (`Doc 16 §7.3`), payout mechanics, and automatic pilot-halt trigger (`Doc 13 §9.2`) [Doc 13 §9.2, §10.2; Doc 16 §7.3, §14; Doc 17 §18 gap 7]

### Task 11.6 — WP-D10: `Doc/17A-OPS-Partner-Agreement-and-External-Pilot-Brief.md` — Partner Organization MoU Template, Data-Sharing Covenant & External Pilot Summary

> **Task Card (`WP-D10`):** **Goal:** Produce the Partner Organization MoU template, the aggregated-only data-sharing covenant, the anti-quota agent compensation schedule, and the shareable external partner/funder pilot brief (`Doc 17 §18 gaps 5–6, §20.1`). · **Context:** `Doc 17 §1.1, §5, §6.3, §14, §18 gaps 5, 6, §20.1`, `Doc 10 §6.1`, `Doc 11 §4.4`, `Doc 13 §7.4, §10.1`, `Doc 15 §0 Caution 2, §18 gap 8`. · **Hard Rules:** Never include BRD national planning figures (`Doc 17 §1.1`); never permit beneficiary-level PII sharing (`Doc 11 §4.4`); never reward per-registration speed (`Doc 15 §18 gap 8`). · **Review:** Level B (MoU clauses flagged for counsel review).

- [x] **S11.6.M1** (≈8 min) — Draft Part A of `Doc/17A-OPS-Partner-Agreement-and-External-Pilot-Brief.md`: Partner Organization MoU / Agreement Template (`Doc 17 §5, §18 gap 5`) — pilot scope, independent partner-employer structure preserving worker-classification boundaries (`Doc 10 §6.1`, `Doc 15 §0 Caution 2`), tablet/fob custody accountability, and non-exclusivity [Doc 17 §5, §18 gap 5; Doc 10 §6.1; Doc 15 §0]
- [x] **S11.6.M2** (≈8 min) — Draft Part B: Agent Compensation & Anti-Quota Covenant (`Doc 15 §18 gap 8`, `Doc 13 §7.4`) + Mandatory Benefit-Decoupling Statement (`Doc 13 §10.1`) — prohibiting per-registration piece-rate pay and requiring the partner to state aloud to communities that declining the pilot never affects cooperative/NGO/ADP benefits [Doc 15 §18 gap 8; Doc 13 §7.4, §10.1; Doc 17 §6.3]
- [x] **S11.6.M3** (≈8 min) — Draft Part C: Aggregated-Only Data-Sharing & Funder Reporting Covenant (`Doc 17 §14 Caution 2, §18 gap 6`) — binding schedule restricting partner and donor reports to cohort-level aggregates and prohibiting individual farmer PII, land polygons, or ledger exports (`Doc 11 §4.4`) [Doc 17 §14, §18 gap 6; Doc 11 §4.4]
- [x] **S11.6.M4** (≈8 min) — Draft Part D: External Partner & Funder Pilot Brief (`Doc 17 §20.1`) — standalone shareable summary carrying the GTM thesis, pilot methodology, ethical safeguards, and launch gates while omitting internal decision leans (`Doc 17 §2`), internal risk ratings (`§16`), fallback revenue vulnerabilities (`§13.2`), and BRD national planning figures (`§1.1`) [Doc 17 §1.1, §14, §20.1]

### Task 11.7 — WP-D15: `Doc/17B-OPS-Unit-Economics-and-WhatsApp-Channel-Policy-Addendum.md` — Subscription GTM Economics & Mandatory WhatsApp / Messaging Channel Compliance Policy

> **Task Card (`WP-D15`):** **Goal:** Author the operational addendum superseding `Doc 17 §13.1–13.2` ("no subscription model exists") with the `FREE_CORE` + `PRO_FARMER` + `ENTERPRISE_FARM` + `SponsorVoucher` GTM rollout (`GTM-D11` resolved) and enforcing mandatory rules `OPS-WA-01`–`OPS-WA-06` forbidding staff, Extension Agents, Support Agents, and Partner Coordinators from creating farmer/supplier WhatsApp groups. · **Context:** `Doc 17 §2 GTM-D11, §5–§7, §13`, `17A-OPS Part C`, `Doc 15 §2, §14`, `Doc 16 §3`, `BRD-v2A`, `10B-LEGAL`. · **Review:** Level B.

- [x] **S11.7.M1** (≈8 min) — Draft Part 1 of `Doc/17B-OPS-Unit-Economics-and-WhatsApp-Channel-Policy-Addendum.md`: supersede `Doc 17 §13.1–13.2` with the Freemium smallholder pilot baseline (`FREE_CORE`), Partner `SponsorVoucher` batch redemption workflow, and `PRO_FARMER` / `ENTERPRISE_FARM` revenue continuity resolving `GTM-D11` [Doc 17 §2, §13; `BRD-v2A §1–§3`; `PRECEDENCE.md #23`]
- [x] **S11.7.M2** (≈8 min) — Draft Part 2 & §3 of `Doc/17B-OPS-Unit-Economics-and-WhatsApp-Channel-Policy-Addendum.md`: binding operational rules `OPS-WA-01`–`OPS-WA-06` across Extension Agents (`15-OPS`), Support (`16-OPS`), and Partners (`17-OPS`/`17A-OPS`), plain-language field FAQ script explaining why Farm Basics uses in-app proximity discovery instead of WhatsApp groups, and anchored change log [Doc 15 §2; Doc 16 §3; Doc 17 §5–§7; `10B-LEGAL §2.2`; `PRECEDENCE.md #27`]

---

## M2 exit gate — pilot readiness

The M2 gate **is** the pilot-readiness gate (Document 19 §4). All must hold before recruiting for the pilot:

- [ ] Document 12 §8's Phase 1 list: geodesic suite, crop-cycle state machine 100%, ledger-balance suite, full offline suite
- [ ] Document 15 §19 (agents trained, `WP-D05` paper & hardware kit ready) and Document 16 §19 (support ready, `WP-D03` error copy catalog live) satisfied
- [ ] Document 17's launch gates (§11 there) satisfied, including `WP-D04` (plain-language/audio summary & pilot consent), `WP-D09` (baseline survey & remediation reserve), and `WP-D10` (partner agreement & aggregated-data covenant)
- [ ] Before recruiting: Document 15 gaps 1–2 (NIN-less, phone-less farmers) resolved or consciously accepted with a stated workaround; Document 16 gaps 1, 2, 5, 6 resolved or consciously accepted with a stated workaround

**During the pilot:** the pilot runs a full crop cycle (months), not weeks. Engineering runs in pilot-support mode — reserve roughly half of engineering capacity for pilot-surfaced defects and specification changes (Doc 13 §8 — spec changes are the expected output). Phase 2 groundwork behind flags may proceed with the remaining capacity, but **never the escrow code path** until Document 10 §3's licensing question is resolved.

---

## Phase 2 and Phase 3 — epics only (rolling wave)

Sprint-level planning for these is deliberately *not* done now: the pilot will change the specification (Doc 13 §8) and the CBN determination can reshape Phase 2. Plan them at the Phase 1 pilot-readiness gate, using measured velocity (Document 19 §8). (Document 19 §5)

| Phase | Epics, in build order | Gated by |
|---|---|---|
| **Phase 2** | **E2.1** Soil advice and labs (reuses the boundary pipeline; per-family `lab_accreditation`) → **E2.2** Land History and polygon-only overlap (disclaimer-gated export) → **E2.3** Vendors (catalog-linked listings; machinery booking `EXCLUDE` constraint) → **E2.4** **Marketplace** (listings; offers with `FOR UPDATE`; reservations; dual sign-off; ledger postings; PayGuard or the non-escrow fallback) → **E2.5** Marketplace Ops and Land-Cert consoles | CBN determination (Doc 10 §3); pen test (Doc 04 §14); Doc 12 §8's Phase 2 list incl. the **50+-iteration oversell race suite**; the **outage-declared clock suspension** (Doc 15 gap 4); anchor buyers (Doc 17 D8) |
| **Phase 3** | **E3.1** Pest & Disease (consensus, **the corroboration-gate combinatorial matrix**; AI-vendor decision) → **E3.2** Knowledge Bank (+ dual-agronomist chemical gate) → **E3.3** Certifications → **E3.4** Full Review Ops, Trust & Safety, Compliance (DSR, legal hold) | Reviewer capacity (Doc 18); **NDPA erasure legal sign-off**; chemical-catalog sourcing and legal review (Doc 06 §5) |

---

## CEA within this plan

Per Doc 02B §6 — **schema now, features later** (Document 19 §6):

- **Built from Sprint 5**: the generalized `production_unit`, all enums, the capture-shape constraint, and the consumer-audit tests (WP-56). Cost: close to nothing, since the work is the same shape either way.
- **Behind flags, after the Phase 1 pilot**: walked greenhouses (`cea.walked_units`); non-walked capture, `GrowingMethodProfile` and soilless cycles (`cea.soilless`, **needs agronomist-authored content**); solution advice and CEA vendor categories (`cea.solution_advice`).
- **Not built until a CEA pilot cohort exists**: nothing in the build should wait for it. No CEA feature ships until its flag is on — the pilot stays a pure open-field pilot while CEA becomes a content-and-UI task later rather than a migration.

---

## Closing notes

- **Verification:** every micro-task's AC is met by completing its description; sprint-level gates run the checklists in `Doc/12-QA-Master-Test-Plan.md` and `Doc/13-QA-UAT-and-Pilot-Protocol.md`; Document 12 §4.1 and §9 govern genuinely-parallel concurrency tests against real PostgreSQL+PostGIS.
- **Sizing discipline:** Document 19 sizes are S ≈ a day or two of agent work plus review; M ≈ most of a sprint; L ≈ more than a sprint (split if possible). Re-slice any micro-task that grows beyond 10 minutes into two smaller ones; record the split in the Sprint-Journal. Post-launch fixes and change requests enter new sprints through the backlog process in `BRD §7.1` — this document stays the frozen Sprint 0–11 baseline.
- **Source of truth:** `Doc/19-ENG-Implementation-Sprint-Plan.md` governs sequence and content; `Plan/Farm-Basics-Sprint-Plan.md` governs indicative effort/resourcing. No calendar dates are set until velocity is measured after Sprints 0–2 (Document 19 §8). Plan backwards from the planting window (Doc 17 D1): if the M2 gate cannot pass before the chosen planting window, move the pilot to the next season rather than compress the gate.
- **Decisions:** record any decision in `DECISIONS.md` (Document 19 §3.2, §7 DoD). Agent sessions start cold — decisions that live only in chat are lost.
