# Farm Basics — Progressive Implementation Sprints

> **How to use this document.** Every sprint in the Farm Basics implementation plan is decomposed here into **tasks → micro-tasks**. Each micro-task is an atomic, verifiable activity sized at **10 minutes or less** of focused work. Work through them progressively (top to bottom); completion checkboxes persist in your browser, so you can resume exactly where you stopped. Where a micro-task must wait on a real-world dependency (procurement, third-party contract, stakeholder decision), it is marked **gated** and the dependent step records that it *Requires Clarification* — proceed by implementing the surrounding logic with a documented interface and revisit once the gate clears.
>
> - **Companion plan:** `Plan\Farm-Basics-Sprint-Plan.md` (sprints S1–S24, phases 0–3 + hardening; durations/effort indicative, *Requires Clarification*).
> - **Operating rules:** the `Preamble` below is binding for every session — session-start ritual, reading/citation protocol, DoR/DoD, requirements traceability, guardrail injection, skills directive, change-control boundary, and the Sprint-Journal.
> - **Guardrails:** `Doc\02-Engineering\10-ENG-AI-Agent-Security-Guardrails.md` (v1.1) is injected per the Preamble's Guardrail Injection Map and enforced per its Section 7.
> - **Skills:** installed skills per `skills\skills-lock.json` and the install locations in P0.6; research catalogue and install notes at `Plan\Recommended-Skills.md`.
> - **Sources:** all references point into `Doc\` (BRD, PRD, Engineering 01–09, Design System 1 & 2, QA Master Test Plan, Parts A/B, Legal 10–11, Data). Short cites, e.g. `[UPS §2.4]` = `Doc\06-Operations\Part A — Platform Operations.md`.
> - **Exit ticket:** each sprint ends with its acceptance gate (`[BRD §18.0]`, module AC straddles, or QA package pass). Leave a sprint only when its ticket is satisfied.
> - **Effort note:** the focused micro-task inventory below is the *deterministic* implementation surface (hours). Full calendar sprint effort additionally includes orientation, environment access, integration, reviews, and capacity variance — those remain *Requires Clarification* per `BRD §7.1`.

---

## Preamble — Implementation Instructions (read first)

> **Status:** binding operating rules for this implementation programme. **Applies to:** AI coding agents (opencode/Claude), and every human implementer. **Controls:** the sprint task lists below are executed *through* the rules in this section. Every shall below is enforceable: an instruction that cannot be traced to a rule here, a source citation, or the guardrails (`Doc\02-Engineering\10-ENG-AI-Agent-Security-Guardrails.md`) has no authority. **Audience note:** the splash into Sprint 1 waits on this section being digested, not skipped.

---

### P0.1 — Session-start ritual

At the start of every working session (new or resumed), the executing agent **shall**:

1. Read this Preamble in full before touching any task below.
2. Read the current sprint section of this document (tasks → micro-tasks) and its sprint-plan companion entry.
3. Load the applicable skills per P0.6 and the guardrail injection per P0.5 before beginning hands-on work.
4. Open a **Sprint-Journal** entry (per P0.8) recording date, session goal, and start micro-task reference.
5. Confirm the session's first task remains un-ticked in this document; if the last session halted mid-sprint, resume exactly where the checkboxes + journal indicate.

### P0.2 — Reading & citation protocol

1. All references in this document use the short form declared in the header (e.g. `[UPS §2.4]`, `[ENG 07 §3]`). The agent **shall** open and read the cited source section before implementing a micro-task whose check expects it; a short cite is a pointer, not a summary.
2. The `Doc\*` set (BRD, PRDs, Engineering 01–09, QA, Design System 1 & 2, Parts A/B, Legal, Data), the sprint plan, and this runbook are the **source of truth**. The agent **shall not** silently override them; corrective decisions **shall** be recorded in the Sprint-Journal and, where user-scoped, flagged as *Requires Clarification*.
3. Micro-task checkboxes persist in-browser. A ticked checkbox **shall** only record completed, verified work in accordance with P0.3.

### P0.3 — Definition of Ready / Definition of Done (DoR / DoD)

A sprint is **Ready** when: its exit ticket and all task-level acceptance checks are legible; gated/`*Requires Clarification*` items are identified and unblocked; the P0.5 guardrail injection and P0.6 skills for that sprint are loaded.

A task is **Done** when, per deliverable type:

| Deliverable | Definition of Done |
|---|---|
| Code (feature/fix) | Meets its micro-task AC, unit-tested, lint/format clean, CI green, no TODOs left undocumented, security guardrails C-1…C-5 checked |
| Infra/IaC | Declared in IaC, secret policy honoured, least-privilege verified, guardrails I-1…I-4 checked |
| Schema/migration | Matches the tagged ENG schema doc, migration + rollback exercised, audit/log columns verified |
| Doc update | Changes trace to a source request, version note added, no obsolete sections left |
| Test package | Automated where possible; manual steps recorded; evidence (output files/screenshots) linked in the journal |

**Gate evidence standard:** every exit ticket (`[BRD §18.0]`, module AC straddle, or QA package) **shall** be bumped only with artifacts — test output, CI link, restore drill log, or UAT sign-off — recorded in the Sprint-Journal (P0.8). A checkbox or gate **shall not** be closed on assertion alone.

### P0.4 — Requirements-traceability rule

1. Every micro-task **shall** trace to the requirement it serves: the bracketed source cite on the task line is the trace anchor.
2. The agent **shall not** implement capability that has no trace anchor in this document or the source set; if the runbook lacks an anchor for a required behaviour, it **shall** add the anchor by extending this document (approved update) rather than drifting.
3. Every sprint exit ticket **shall** be linked to its AC source and the artifact that satisfies it, forming a chain: requirement → micro-task → evidence.

### P0.5 — Guardrail Injection Map

`Doc\02-Engineering\10-ENG-AI-Agent-Security-Guardrails.md` (v1.1) is injected into execution as follows, using its guardrail IDs (T-1…T-5, C-1…C-5, V-1…V-5, I-1…I-4, L-1…L-2) and Section 7 enforcement:

| Sprint range | Primary focus | Guardrails in effect with intent |
|---|---|---|
| S1–S4 (Phase 0) | Foundations: CI/CD, observability, DR, identity/RBAC, security baseline | **T-1/T-2** (Architecture & threat-mapping from the first commit), **C-1…C-5** (secure generation from line 1), **I-1…I-4** (hardened free-tier IaC), **V-1/V-5** (SAST in CI, dependency gates), **L-2** (append-only audit seeded) |
| S5–S13 (Phase 1) | Farmer app, registration, NIN verification, records/fields | **T-3/T-4** (crypto + zero-trust identity), **C-1/C-2** (input handling incl. NIN flows), **V-2** (taint analysis on user inputs), **L-1** (NDPA for NIN/PII) |
| S14–S19 (Phase 2) | Marketplace, escrow (PayGuard), admin consoles | **T-5** + **C-4/C-5** (API-surface + admin authz), **V-4** (fuzzing escrow endpoints), **L-1** (CBN regime once licensing determination lands) |
| S20–S22 (Phase 3) | Finances, disbursement triggers, reconciliation | **T-3** (crypto), **V-3** (priority triage KEV>EPSS>CVSS), **L-2** (audit trail for every settlement action) |
| S23–S24 (Hardening) | Resilience, security validation, compliance, UAT | Full **Sections 2–6** sweep + **Section 7** enforcement evidence for every guardrail |

Intra-sprint, at every phase/section boundary the agent **shall** re-apply **T-2** (incremental threat-model delta) before writing new surfaces.

### P0.6 — Skills Directive

1. The agent **shall** discover skills from the authoritative install locations (`.opencode/skills/`, `~/.config/opencode/skills/`, `~/.agents/skills/`, `~/.claude/skills/`; per https://opencode.ai/docs/skills) and the lock-file `skills\skills-lock.json`.
2. For **every** micro-task the agent **shall** load and apply the relevant skill(s) — document/artifact, web/design/testing, workflow/engineering, devsecops, or mobile-verification skills that serve the task — and record the skill used in the Sprint-Journal. Skills are part of the operating layer, not optional decoration.
3. `Plan\Recommended-Skills.md` is the **research catalogue** (nothing there awaited approval to install). A skill that is not present in an install location or the lock-file **shall not** be assumed available; installing one is an approved change (P0.7).
4. Where two skills overlap the same task, prefer the one already installed/locked; otherwise the *Fit* column and the reviewed caveats in the catalogue decide, with the choice journaled.

### P0.7 — Change-control boundary & autonomy

1. **Autonomous scope:** within a sprint the agent **may** proceed task-to-task without further sign-off, provided DoR holds and guardrails are applied. Re-slicing an over-10-minute micro-task into two and recording the split is mandated by `Closing notes` and **does not** require approval.
2. **Stop-and-ask (Requires Clarification):** the agent **shall** halt and surface a decision when any of the following occurs: a gated dependency (procurement, third-party contract, stakeholder decision) is required; a `*Requires Clarification*` block is reached; the runbook conflicts with a source document; a budget, capacity, or licensing question arises; a change would alter an engineering document's decision beyond a journaled note. While halted, surrounding logic **shall** be implemented with a documented interface so work is not double-blocked.
3. **Approved-change path:** extending this runbook (new micro-task, revised cite, gate wording) is an approved update; editing BRD/PRD/ENG/QA/Legal sources is not — those accept correction through change requests, not inline edits by the implementer.

### P0.8 — Resumption protocol & Sprint-Journal

1. The **Sprint-Journal** (`Plan\sessions\Sprint-Journal.md`) is the session ledger. Each session **shall** append: date, session goal, micro-task IDs touched (start→end), skills applied (per P0.6), guardrails checked (per P0.5), gate evidence (per P0.3), decisions and *Requires Clarification* items, and the exact next un-ticked micro-task.
2. Suggested followups — debt, improvement ideas, deferred work, unanswered review notes — are recorded add-only in the **Followups Ledger** (`Plan\sessions\Followups-Ledger.md`), each as an `F-##` entry traced to the source session entry.
3. On resume the agent **shall** run P0.1, read the journal tail to locate the exact continuation point, and verify it against the un-ticked checkboxes in this document before proceeding.
4. If a resume reveals drift between the journal and the unchecked boxes, the journal entry **shall** be reconciled and the discrepancy noted before continuing.

---

## Sprint 1 — Foundations 1: scaffold, debt-free core patterns

> **Phase 0 · Focus:** repo & CI/CD, observability, DR baseline, idempotency, concurrency, audit, geography seed, reconciliation skeleton. **Exit ticket:** Phase-0 pattern AC smoke, CI green, restorable backup. **Primary:** ENG 07/08/09, UPS §2.2/§2.6/§2.7/§2.8/§2.9, BRD §11.5, §18.0.

### Task 1.1 — Repository, project scaffolding, CI/CD

- [ ] **S1.1.M1** (≈8 min) — Create monorepo layout: `src/api`, `src/app`, `src/admin`, `infra`, `tests`, `docs` with root README [ENG 07 §2].
- [ ] **S1.1.M2** (≈6 min) — Add `.gitignore`, `.editorconfig`, lint config, and pre-commit hooks (format + lint) [ENG 07 §3].
- [ ] **S1.1.M3** (≈8 min) — Scaffold web API project with `/health` and structured error middleware [UPS §0.1].
- [ ] **S1.1.M4** (≈8 min) — Scaffold farmer mobile app against dev API endpoint [DS §10].
- [ ] **S1.1.M5** (≈8 min) — Scaffold admin console app shell (dark surface, brand mark) [AOC; DS2 §3.7].
- [ ] **S1.1.M6** (≈8 min) — CI pipeline: build + unit tests on every commit [ENG 07 §4].
- [ ] **S1.1.M7** (≈6 min) — Define dev / test / staging / prod IaC folders and secret policy [ENG 07 §5].

### Task 1.2 — Observability, alerting, disaster-recovery baseline

- [ ] **S1.2.M8** (≈8 min) — Wire structured logging with contextual fields (user, tenant, correlation id) [ENG 08].
- [ ] **S1.2.M9** (≈8 min) — Add metrics endpoint + baseline HTTP/DB gauge exporters [ENG 08].
- [ ] **S1.2.M10** (≈6 min) — Connect alert channel (email/SMS) and fire a test alert [UPS §2.5].
- [ ] **S1.2.M11** (≈8 min) — Scheduled DB backup job + restore-drill runbook [ENG 09].
- [ ] **S1.2.M12** (≈4 min) — Record RPO/RTO targets and backup retention in DR doc [BRD §11.5].

### Task 1.3 — Idempotency, optimistic concurrency, immutable audit

- [ ] **S1.3.M13** (≈8 min) — `AppendOnlyAuditLog` table + write path (before/after JSON snapshots) [UPS §2.6].
- [ ] **S1.3.M14** (≈8 min) — `Idempotency-Key` middleware on POST/PATCH with storage table [UPS §2.7].
- [ ] **S1.3.M15** (≈6 min) — Optimistic-concurrency version column pattern on core aggregates [UPS §2.7].

### Task 1.4 — Geography, config rules, reconciliation skeleton

- [ ] **S1.4.M16** (≈8 min) — Create `Geography` tables (State → Lga → Ward → Community) with code constraints [UPS §2.2].
- [ ] **S1.4.M17** (≈8 min) — Seed Nigerian states + LGAs from authoritative census listing [UPS §2.2].
- [ ] **S1.4.M18** (≈6 min) — Config/rule-table pattern: settings key/value + review-category reference seed [UPS §2.9].
- [ ] **S1.4.M19** (≈8 min) — Scheduled reconciliation job scaffold (run, log, flag drift) [UPS §2.8].

### Task 1.5 — Phase-0 gate

- [ ] **S1.5.M20** (≈6 min) — Update ENG 02/03 schema docs to match S1 deliverables [ENG 02/03].
- [ ] **S1.5.M21** (≈6 min) — Run Phase-0 cross-cutting acceptance smoke (idempotency, audit, geog seed) [BRD §18.0] → **exit pass**.

---

## Sprint 2 — Foundations 2: identity, roles, governance foundation

> **Phase 0 · Focus:** account + roles, capability model, RBAC enforcement, review-driving config, trusted status, API conventions. **Exit ticket:** role/authorization AC pass. **Primary:** BRD §4.4, §14.4; UPS §2.1/§2.6/§2.9; PartB §3.1/§3.2.

### Task 2.1 — Account & role foundation

- [ ] **S2.1.M1** (≈10 min) — User/account entity with status lifecycle (PENDING/ACTIVE/SUSPENDED/CLOSED) [UPS §2.1].
- [ ] **S2.1.M2** (≈8 min) — 12 admin roles modeled as capabilities (data, permissions, sync, review, finances, advocacy, P&L, users, mod, content, T&S, audit) [PartB §3.1].
- [ ] **S2.1.M3** (≈8 min) — Capability→action mapping table (who may do what on each module) [PartB §3.2; BRD §14.4].
- [ ] **S2.1.M4** (≈8 min) — RBAC middleware enforcing capability check on protected actions [PartB §3.2].

### Task 2.2 — Review-driving configuration, status and trust base

- [ ] **S2.2.M5** (≈8 min) — Module entities record status + review flags on writes ([UPS §2.6]).
- [ ] **S2.2.M6** (≈8 min) — Auto-review markers: risk-level, geodata, financial signals config [UPS §4.2].
- [ ] **S2.2.M7** (≈10 min) — Brand-safety and denied-content signal list baked into review scoring config [UPS §4.2].
- [ ] **S2.2.M8** (≈6 min) — Declared-phone dedupe rule (shape) implemented and unit-tested [PartB §3.3, CBN gate].

### Task 2.3 — API conventions & partner gateway

- [ ] **S2.3.M9** (≈8 min) — API versioning, error envelope, rate-limit headers in a convention doc/module [ENG 03].
- [ ] **S2.3.M10** (≈8 min) — Partner gateway seam (external provider call + timeout) used later by Prembly/PayGuard [UPS §13].
- [ ] **S2.3.M11** (≈6 min) — Emergency kill-switch flag for a partner integration (effective immediately) [PartB §3.3].

### Task 2.4 — Governance + Phase-0 exit for S2

- [ ] **S2.4.M12** (≈8 min) — Audit log grows: exporter for audit trails (search/filter) [UPS §2.6].
- [ ] **S2.4.M13** (≈6 min) — Run RBAC + review-flag AC wipe tests [BRD §18.0].
- [ ] **S2.4.M14** (≈4 min) — Record permission-matrix rows as seed data for all 12 roles [PartB §3.1] → **exit pass**.

---

## Sprint 3 — Foundations 3: reviews, alerting, notifications, IVR

> **Phase 0 · Focus:** review engine (categories), alert taxonomy + delivery, notifications, IVR scripting, channels. **Exit ticket:** review queue AC, alert delivery AC, notification AC. **Primary:** UPS §2.9, §2.5; PartB §3.4; BRD §12.0.

### Task 3.1 — Review engine

- [ ] **S3.1.M1** (≈8 min) — Review category model (≥11 categories incl. verified consumer, operator, brand fit, improper behavior) [UPS §2.9].
- [ ] **S3.1.M2** (≈8 min) — Review queue view: assign, prioritize, comment, decision, appeal trail [UPS §2.9].
- [ ] **S3.1.M3** (≈8 min) — Review decision applies status change + audit snapshot [UPS §2.6].
- [ ] **S3.1.M4** (≈6 min) — Escalation path to discretion / legal for edge cases [BRD §4.4; PRD Vol B].

### Task 3.2 — Alert taxonomy & delivery

- [ ] **S3.2.M5** (≈8 min) — Alert taxonomy enum (geo, txn, security, fraud, system, data-quality) [UPS §2.5].
- [ ] **S3.2.M6** (≈8 min) — Rule-to-alert wiring: geo-off-grant, missing-mse-status, anomaly triggers [UPS §2.5, §4.3].
- [ ] **S3.2.M7** (≈6 min) — Delivery channels (SMS/email/push/in-app) with preference profile [BRD §12.0].
- [ ] **S3.2.M8** (≈6 min) — Alert dedupe + throttling guard [UPS §2.5].

### Task 3.3 — Notifications & IVR

- [ ] **S3.3.M9** (≈8 min) — Notification center: inbox, read-state, deep links [BRD §12.0].
- [ ] **S3.3.M10** (≈10 min) — IVR script framework (initial whitelist of flows) + channel seam [BRD §12.2].
- [ ] **S3.3.M11** (≈8 min) — SMS provider staging credential + template set [BRD §12.0].
- [ ] **S3.3.M12** (≈6 min) — Preference toggle: quiet hours, channel per category [BRD §12.3].

### Task 3.4 — S3 gate

- [ ] **S3.4.M13** (≈6 min) — Review-queue AC walk (queue, assign, decide, appeal) [BRD §18.0].
- [ ] **S3.4.M14** (≈6 min) — Alert + notification AC wipes (dedupe, delivery, IVR smoke) [BRD §18.0] → **exit pass**.

---

## Sprint 4 — Foundations 4: phase-0 sign-off and legal pack

> **Phase 0 · Focus:** identity/trust console, audit search, alerts wiring, break-glass, phase-0 tests, legal artefacts. **Exit ticket:** Phase-0 approved by governance; legal pack filed. **Primary:** PartB §3.3; AOC; UPS §2.4/§2.6/§2.8; Legal 10/11.

### Task 4.1 — Identity & trust console

- [ ] **S4.1.M1** (≈8 min) — User + role management screens (list, search, suspend, role change) [PartB §3.1].
- [ ] **S4.1.M2** (≈8 min) — Break-glass / delegated emergency access flow with audit trail [PartB §3.3].
- [ ] **S4.1.M3** (≈6 min) — Partner status board (Prembly/PayGuard seams green/red/kill-switched) [PartB §3.3].

### Task 4.2 — Audit, alerts, reconciliation on console

- [ ] **S4.2.M4** (≈8 min) — Audit search UI: event, actor, entity, date range [UPS §2.6].
- [ ] **S4.2.M5** (≈8 min) — Alert center page (open, assigned, resolved) [UPS §2.5].
- [ ] **S4.2.M6** (≈8 min) — Reconciliation run log + drift report UI scaffold [UPS §2.8].

### Task 4.3 — Phase-0 acceptance & hardening

- [ ] **S4.3.M7** (≈8 min) — Run S1–S4 combined regression + concurrency race checks [ENG 05].
- [ ] **S4.3.M8** (≈8 min) — Offline disconnect matrix for Phase-0 surfaces (idempotent retry) [ENG 05 ℹ 12].
- [ ] **S4.3.M9** (≈6 min) — Permission-matrix smoke against every console action [PartB §3.2].

### Task 4.4 — Legal pack

- [ ] **S4.4.M10** (≈10 min) — NDPR compliance checklist populated for live services [Doc 10].
- [ ] **S4.4.M11** (≈8 min) — Digital lending law compatibility note per CBN gate [BRD §7.2; Legal].
- [ ] **S4.4.M12** (≈6 min) — Data-sharing authorizations + consent templates drafted [Legal 11].
- [ ] **S4.4.M13** (≈8 min) — Vendor / partner agreements summary checklist (Prembly, PayGuard, telecom) [PartB §3.4].
- [ ] **S4.4.M14** (≈4 min) — Phase-0 governance sign-off record in project log [BRD §4.3] → **exit pass**.

---
## Sprint 5 — Phase 1: Registration core (NIN, verification, OTP)

> **Phase 1 · Focus:** NIN + liveness verification seams, farmer registration flow, CMS skeleton. **Exit ticket:** registration AC walk incl. provider fallback. **Primary:** UPS §4.4, §15; PRD Vol B; Legal 11; ENG 01 §12.

### Task 5.1 — NIN, OTP, liveness baseline

- [ ] **S5.1.M1** (≈8 min) — NIN format validation + declared-phone dedupe on registration [UPS §4.4; PartB §3.3].
- [ ] **S5.1.M2** (≈8 min) — NIN verification provider seam (Prembly) with sandbox mock mode [UPS §15].
- [ ] **S5.1.M3** (≈8 min) — OTP issue + verify endpoints with rate limiting [UPS §4.4].
- [ ] **S5.1.M4** (≈8 min) — Liveness capture integration (selfie) + result flag [UPS §4.4].

### Task 5.2 — Farmer registration flow

- [ ] **S5.2.M5** (≈10 min) — Wizard: personal → farm detail → NIN/liveness → OTP, resumable draft [PRD Vol B; UPS §2.6].
- [ ] **S5.2.M6** (≈6 min) — Registration lands `PENDING` with full audit snapshot [UPS §2.6].
- [ ] **S5.2.M7** (≈6 min) — Consent + data-sharing authorization captured and audited [Legal 11].
- [ ] **S5.2.M8** (≈8 min) — Multi-account / duplicate identity guard with flagging [PartB §3.3].

### Task 5.3 — CMS skeleton

- [ ] **S5.3.M9** (≈8 min) — Content type model with variance support (multi-variant content) [ENG 01 §12].
- [ ] **S5.3.M10** (≈8 min) — CMS editor screen (draft → review → publish) [ENG 01 §12].
- [ ] **S5.3.M11** (≈6 min) — Publish requires approval + writes audit trail [UPS §4.2; §2.6].
- [ ] **S5.3.M12** (≈6 min) — Contributor role + moderation queue wiring [PartB §3.2].

### Task 5.4 — S5 gate

- [ ] **S5.4.M13** (≈6 min) — Registration AC walk incl. provider-down fallback path [BRD §18.0].
- [ ] **S5.4.M14** (≈6 min) — OTP + liveness negative tests (bad code, spoofed selfie) [ENG 05] → **exit pass**.

---

## Sprint 6 — Phase 1: registration journeys (fob, agent, tablet, org)

> **Phase 1 · Focus:** no-phone fob journey, agent/tablet/organization registration, verification & trust console. **Exit ticket:** fob/agent/tablet AC walk. **Primary:** UPS §4.4, §2.9; DS2; PartB §3.1/§3.4.

### Task 6.1 — No-phone "fob" journey

- [ ] **S6.1.M1** (≈8 min) — Fob entry capture with geodata recorded on registration [UPS §4.4; §2.2].
- [ ] **S6.1.M2** (≈6 min) — Fob downgrade flag + message variance for limited-connectivity farmers [UPS §4.4].
- [ ] **S6.1.M3** (≈6 min) — Fob registrations routed to review + escalation rules [UPS §2.6/§2.9].

### Task 6.2 — Agent, tablet, organisation journeys

- [ ] **S6.2.M4** (≈8 min) — Agent onboarding flow + commission profile stub [PartB §3.4].
- [ ] **S6.2.M5** (≈8 min) — Dedicated tablet (kiosk) mode UI for field registration [DS2].
- [ ] **S6.2.M6** (≈8 min) — Organization (association/cooperative) registration model [ENG 01 §12].
- [ ] **S6.2.M7** (≈6 min) — Org admin + member invite flow with role defaulting [PartB §3.1].

### Task 6.3 — Verification & trust console

- [ ] **S6.3.M8** (≈8 min) — Registration queue + document review screens [UPS §4.4].
- [ ] **S6.3.M9** (≈6 min) — Status transitions, reject reasons, appeal flow [UPS §2.9].
- [ ] **S6.3.M10** (≈8 min) — Trust-status compute + display (farmer/agent/org) [UPS §2.9].
- [ ] **S6.3.M11** (≈6 min) — Verification history + audit trail view [UPS §2.6].

### Task 6.4 — S6 gate

- [ ] **S6.4.M12** (≈8 min) — Fob + agent + tablet + org registration AC walk [BRD §18.0].
- [ ] **S6.4.M13** (≈6 min) — Verification queue + appeal round-trip wipe [ENG 05] → **exit pass**.

---

## Sprint 7 — Phase 1: field boundary capture & geodesy

> **Phase 1 · Focus:** boundary capture, PostGIS geodesic storage, geometry checks, field console. **Exit ticket:** geometry/area AC walk. **Primary:** UPS §4.1, §2.2; ENG 06; DS2 §10.

### Task 7.1 — Boundary capture

- [ ] **S7.1.M1** (≈8 min) — Field entity + owner relation + address/plot reference [UPS §4.1].
- [ ] **S7.1.M2** (≈10 min) — Boundary capture in GPS app + tablet mode, stored coordinates [UPS §4.1; DS2].
- [ ] **S7.1.M3** (≈8 min) — Boundary import/export (GeoJSON/shapefile) with validation [ENG 06].
- [ ] **S7.1.M4** (≈6 min) — Captured boundary snapshot appended to immutable audit [UPS §2.6].

### Task 7.2 — Geodesy & geometry checks

- [ ] **S7.2.M5** (≈8 min) — Use PostGIS `geography` type end-to-end (store, index, query) [UPS §4.1].
- [ ] **S7.2.M6** (≈8 min) — Area computation in hectares from geodesic ring [UPS §4.1].
- [ ] **S7.2.M7** (≈6 min) — Geometry validity checks (self-intersection, pole, min-area) [UPS §4.1].
- [ ] **S7.2.M8** (≈8 min) — Overlap/flood-report run across nearby fields [UPS §4.1].

### Task 7.3 — Field console & filters

- [ ] **S7.3.M9** (≈8 min) — Field filters API (location, size, status, crop) [ENG 01].
- [ ] **S7.3.M10** (≈8 min) — Field list/map console screen [AOC].
- [ ] **S7.3.M11** (≈8 min) — Boundary edit → re-verify workflow with review routing [UPS §2.9].
- [ ] **S7.3.M12** (≈6 min) — Geography index performance check on filtered queries [ENG 05].

### Task 7.4 — S7 gate

- [ ] **S7.4.M13** (≈6 min) — Geometry validity + area-accuracy sample tests AC [BRD §18.0].
- [ ] **S7.4.M14** (≈4 min) — Document PostGIS geography conventions in ENG 02 [ENG 02] → **exit pass**.

---

## Sprint 8 — Phase 1: boundary state, offline capture, surveys

> **Phase 1 · Focus:** boundary state machine + geo alerts, offline capture & sync, survey/exceptions. **Exit ticket:** state-machine + offline sync regression. **Primary:** UPS §4.1, §2.5; ENG 05 (12); PartB §3.2/§3.4.

### Task 8.1 — Boundary state machine & geo alerts

- [ ] **S8.1.M1** (≈8 min) — Boundary status flow (pending / verified / rejected / flagged) [UPS §4.1].
- [ ] **S8.1.M2** (≈8 min) — Geofence rules firing on granted boundary alerts [UPS §2.5].
- [ ] **S8.1.M3** (≈6 min) — Missing MSE-status escalation into review queue [UPS §2.9].

### Task 8.2 — Offline capture & sync

- [ ] **S8.2.M4** (≈8 min) — Offline boundary capture queue with batch sync [ENG 05 ℹ12].
- [ ] **S8.2.M5** (≈8 min) — Sync conflict resolution (documented winners) + audit record [ENG 05].
- [ ] **S8.2.M6** (≈6 min) — Resilient UI states (offline pending count, reconnect) [DS2].

### Task 8.3 — Surveys, exceptions, disputes

- [ ] **S8.3.M7** (≈8 min) — Survey-team task assignment + field verification workflow [PartB §3.4].
- [ ] **S8.3.M8** (≈6 min) — Exception/override approvals with required reason + audit [PartB §3.2].
- [ ] **S8.3.M9** (≈6 min) — Boundary dispute flag + review routing [UPS §2.9].

### Task 8.4 — S8 gate

- [ ] **S8.4.M10** (≈8 min) — Boundary state-machine AC wipe [BRD §18.0].
- [ ] **S8.4.M11** (≈8 min) — Offline→online sync regression incl. race/dup submission [ENG 05] → **exit pass**.

---

## Sprint 9 — Phase 1: crop lifecycle core

> **Phase 1 · Focus:** crop registry, CropProfile, growth stages, task catalog, MSE-status, validation flags. **Exit ticket:** stage-advance + alert trigger passes. **Primary:** ENG 01 §9; UPS §4.3, §4.5.

### Task 9.1 — Crop entities & profiles

- [ ] **S9.1.M1** (≈8 min) — Crop registry (catalog of supported crops) [ENG 01 §9].
- [ ] **S9.1.M2** (≈10 min) — CropProfile: input requirements, water needs, disease flags [ENG 01 §9; UPS §4.3].
- [ ] **S9.1.M3** (≈8 min) — Configurable growth-stage framework per crop [ENG 01 §9].
- [ ] **S9.1.M4** (≈6 min) — Season/cycle entity linking field + crop + dates [ENG 01 §9].

### Task 9.2 — Seed, planting, MSE-status

- [ ] **S9.2.M5** (≈8 min) — Seed registry tracking quality-compliant seed sources [UPS §4.5].
- [ ] **S9.2.M6** (≈8 min) — Task catalog generation from stage model (planting, inputs, harvesting) [ENG 01 §9].
- [ ] **S9.2.M7** (≈8 min) — MSE-status recorded per field per season [UPS §4.3].

### Task 9.3 — Validation & alerts

- [ ] **S9.3.M8** (≈8 min) — Growth-stage validation rules + early/late flags [ENG 01 §9].
- [ ] **S9.3.M9** (≈6 min) — Missing MSE / sample-mandate alerts wired [UPS §4.3; §2.5].
- [ ] **S9.3.M10** (≈6 min) — Quality-issue flags (non-compliant seed, off-plan inputs) [ENG 01 §9].

### Task 9.4 — S9 gate

- [ ] **S9.4.M11** (≈8 min) — Stage-advance AC walk + flag triggers [BRD §18.0].
- [ ] **S9.4.M12** (≈6 min) — MSE + sample alert negative/positive tests [ENG 05] → **exit pass**.

---

## Sprint 10 — Phase 1: accounting & ledgers core

> **Phase 1 · Focus:** chart of accounts, double-entry posting, statements, reconciliation, treasury safety. **Exit ticket:** posting-integrity + matching pass. **Primary:** PartB §3.1/§3.3; ENG 01 §11; UPS §2.8.

### Task 10.1 — Chart of accounts & double-entry

- [ ] **S10.1.M1** (≈8 min) — CoA entity + starter Nigerian-format accounts seed [PartB §3.1].
- [ ] **S10.1.M2** (≈10 min) — Double-entry posting engine (balanced dr/cr, atomic) [PartB §3.3].
- [ ] **S10.1.M3** (≈8 min) — Business actions emit journal entries via hooks [PartB §3.3].

### Task 10.2 — Ledger & statements

- [ ] **S10.2.M4** (≈8 min) — Account balances + statement generator [ENG 01 §11].
- [ ] **S10.2.M5** (≈8 min) — Ledger immutability; adjustments require approval + audit [PartB §3.3].
- [ ] **S10.2.M6** (≈6 min) — Transaction categorization for aggregation [UPS §2.8].

### Task 10.3 — Reconciliation & treasury safety

- [ ] **S10.3.M7** (≈8 min) — External statement import + automated matching [UPS §2.8].
- [ ] **S10.3.M8** (≈8 min) — Discrepancy flagging + resolution workflow [UPS §2.8].
- [ ] **S10.3.M9** (≈6 min) — Rounding/anti-scaling policy enforced on postings [PartB §3.3].

### Task 10.4 — S10 gate

- [ ] **S10.4.M10** (≈8 min) — Double-entry integrity tests (balance invariants, atomicity) [ENG 05].
- [ ] **S10.4.M11** (≈6 min) — Matching + discrepancy-resolution wipe [BRD §18.0] → **exit pass**.

---

## Sprint 11 — Phase 1: fixed assets, period close, computed reports

> **Phase 1 · Focus:** asset register & depreciation, finance period lifecycle, computed P&L/reports. **Exit ticket:** period-close regression + depreciation sample pass. **Primary:** ENG 01 §11; PartB §3.3.

### Task 11.1 — Fixed assets

- [ ] **S11.1.M1** (≈8 min) — Asset register + depreciation schedule engine [ENG 01 §11].
- [ ] **S11.1.M2** (≈6 min) — Asset tag/QR identification concept integrated [ENG 01 §11].
- [ ] **S11.1.M3** (≈6 min) — Asset disposal/write-off flow with approval [PartB §3.3].

### Task 11.2 — Finance period close

- [ ] **S11.2.M4** (≈8 min) — Period open/close lifecycle with lockdown [PartB §3.3].
- [ ] **S11.2.M5** (≈6 min) — Month-end closing checklist + period audit snapshot [PartB §3.3].
- [ ] **S11.2.M6** (≈6 min) — Year-end rollover + opening balance posting [ENG 01 §11].

### Task 11.3 — Computed reports

- [ ] **S11.3.M7** (≈8 min) — Computed P&L from ledgers (incl. NPAU-affecting views) [ENG 01 §11].
- [ ] **S11.3.M8** (≈6 min) — Inter-account movement/aggregation reports [UPS §2.8].
- [ ] **S11.3.M9** (≈6 min) — Currency formatting + XLSX/PDF export [ENG 01].

### Task 11.4 — S11 gate

- [ ] **S11.4.M10** (≈8 min) — Period-close + rollover regression [ENG 05].
- [ ] **S11.4.M11** (≈6 min) — Depreciation + computed-report sample accuracy tests [BRD §18.0] → **exit pass**.

---

## Sprint 12 — Phase 1: master data, field ops console, analytics baseline

> **Phase 1 · Focus:** master-data/config consoles, field-ops dashboard, analytics schema + baseline dashboards. **Exit ticket:** dashboards render + CRUD AC. **Primary:** AOC; UPS §2.9, §4.3; ENG 06.

### Task 12.1 — Master data & config consoles

- [ ] **S12.1.M1** (≈8 min) — Catalog screens (crops, varieties, inputs) with approval-on-change [PartB §3.2].
- [ ] **S12.1.M2** (≈8 min) — Config/rule editor UI (settings key/value, review categories) [UPS §2.9].
- [ ] **S12.1.M3** (≈6 min) — Reference-data versioning + change log [UPS §2.9].

### Task 12.2 — Field ops console

- [ ] **S12.2.M4** (≈8 min) — Ops dashboard: fields in stage, open alerts, queue depth [AOC].
- [ ] **S12.2.M5** (≈8 min) — MSE / NEAR-revenue monitoring widgets [UPS §4.3].
- [ ] **S12.2.M6** (≈6 min) — Task-dispatch queue for field officers [PartB §3.4].

### Task 12.3 — Analytics baseline

- [ ] **S12.3.M7** (≈8 min) — Analytics schema + ETL baseline job [ENG 06].
- [ ] **S12.3.M8** (≈6 min) — Cohort + region dimension tables populated [ENG 06].
- [ ] **S12.3.M9** (≈8 min) — Baseline KPI dashboards (fields, MSE, alerts) [ENG 06].

### Task 12.4 — S12 gate

- [ ] **S12.4.M10** (≈8 min) — Dashboard render + master-data CRUD AC walk [BRD §18.0].
- [ ] **S12.4.M11** (≈6 min) — Config-change audit trail verified [UPS §2.6] → **exit pass**.

---

## Sprint 13 — Phase 1 exit: pilot metrics, regional RBAC, acceptance

> **Phase 1 · Focus:** pilot KPI tracking, NPAU populations, region-scoped RBAC, Phase-1 acceptance. **Exit ticket:** pilot sign-off + P1 readiness. **Primary:** BRD §2.3, §4.3, §18.0; PartB §3.1/§3.2; ENG 05.

### Task 13.1 — Pilot metrics & analytics

- [ ] **S13.1.M1** (≈8 min) — Pilot KPI definitions + tracking dashboard [BRD §2.3].
- [ ] **S13.1.M2** (≈6 min) — NPAU-affected population views [ENG 01].
- [ ] **S13.1.M3** (≈8 min) — NEAR-revenue gap anomaly analysis [UPS §4.3].

### Task 13.2 — Region-scoped RBAC

- [ ] **S13.2.M4** (≈8 min) — Region/scope dimension added to roles & capabilities [PartB §3.2].
- [ ] **S13.2.M5** (≈8 min) — Query-layer data isolation filters by scope [PartB §3.1].
- [ ] **S13.2.M6** (≈8 min) — Region-scope authorization AC tests [BRD §18.0].

### Task 13.3 — Phase-1 acceptance

- [ ] **S13.3.M7** (≈8 min) — Full Phase-1 regression suite run + report [ENG 05].
- [ ] **S13.3.M8** (≈8 min) — Offline / disconnect / race wipe across P1 modules [ENG 05 ℹ12].
- [ ] **S13.3.M9** (≈6 min) — Training vignettes for field staff prepared [PRD Vol B].

### Task 13.4 — S13 gate

- [ ] **S13.4.M10** (≈6 min) — Pilot sign-off record + P2 readiness checklist [BRD §4.3] → **exit pass**.

---

## Sprint 14 — Phase 2: land history, tenancy, exports

> **Phase 2 · Focus:** ownership/tenancy history, timeline, overlap, certified-land exports with disclaimer. **Exit ticket:** overlap + export wipe. **Primary:** UPS §4.1; Legal 10/11; AOC.

### Task 14.1 — Ownership & tenancy history

- [ ] **S14.1.M1** (≈8 min) — Land ownership/tenancy records + relationships [UPS §4.1].
- [ ] **S14.1.M2** (≈8 min) — Per-land history timeline (ownership chain, changes) [UPS §4.1].
- [ ] **S14.1.M3** (≈8 min) — Land title/document upload + verification workflow [Legal 11].

### Task 14.2 — Overlap detection & exports

- [ ] **S14.2.M4** (≈10 min) — Overlap detection extended across history versions [UPS §4.1].
- [ ] **S14.2.M5** (≈8 min) — Certified-land summary export with legal disclaimer [Legal 10].
- [ ] **S14.2.M6** (≈6 min) — Export accuracy assertion + disclaimer component tests [Legal 10].

### Task 14.3 — Land consoles

- [ ] **S14.3.M7** (≈8 min) — Land admin console (list, map, detail, history) [AOC].
- [ ] **S14.3.M8** (≈8 min) — Land-part certification UI with review routing [AOC; UPS §2.9].

### Task 14.4 — S14 gate

- [ ] **S14.4.M9** (≈6 min) — Overlap + history AC wipe [BRD §18.0].
- [ ] **S14.4.M10** (≈6 min) — Export + disclaimer round-trip tests [ENG 05] → **exit pass**.
## Sprint 15 — Phase 2: soil testing & recommendations

> **Phase 2 · Focus:** sample lifecycle, protocol + GPS enforcement, soil-driven crop recommendations, lab registry. **Exit ticket:** recommendation + lab AC. **Primary:** UPS §4.3; PartB §3.4.

### Task 15.1 — Soil sample lifecycle

- [ ] **S15.1.M1** (≈8 min) — Sample entity + status flow (requested, collected, in-lab, reported) [UPS §4.3].
- [ ] **S15.1.M2** (≈6 min) — Sample collection protocol checklist enforced in flow [UPS §4.3].
- [ ] **S15.1.M3** (≈8 min) — GPS tolerance enforcement on sample location capture [UPS §4.3].

### Task 15.2 — Recommended crops

- [ ] **S15.2.M4** (≈10 min) — Soil-test-driven crop recommendation engine [UPS §4.3].
- [ ] **S15.2.M5** (≈6 min) — Recommendation adjustment gate (override requires approval) [UPS §4.3].
- [ ] **S15.2.M6** (≈6 min) — Recommendation history + audit trail [UPS §2.6].

### Task 15.3 — Lab registry

- [ ] **S15.3.M7** (≈8 min) — Lab registry + accreditation tracker [PartB §3.4].
- [ ] **S15.3.M8** (≈8 min) — Sample-to-lab assignment + status sync [UPS §4.3].
- [ ] **S15.3.M9** (≈6 min) — Seed/lab compliance flags on reports [UPS §4.5].

### Task 15.4 — S15 gate

- [ ] **S15.4.M10** (≈8 min) — GPS-enforcement + recommendation-engine AC [BRD §18.0].
- [ ] **S15.4.M11** (≈6 min) — Lab assignment + sync round-trip tests [ENG 05] → **exit pass**.

---

## Sprint 16 — Phase 2: marketplace

> **Phase 2 · Focus:** listings, offers, pooling, reservation semantics, trust & anti-fraud. **Exit ticket:** offer + reserve-lock wipe. **Primary:** ENG 01 §10; UPS §2.9; PartB §3.3.

### Task 16.1 — Listings & feed

- [ ] **S16.1.M1** (≈8 min) — Listing entity + categories + media [ENG 01 §10].
- [ ] **S16.1.M2** (≈8 min) — Listing lifecycle (draft, live, paused, sold) [ENG 01 §10].
- [ ] **S16.1.M3** (≈8 min) — Geo/market filters + feed API [ENG 01 §10].

### Task 16.2 — Offers & pooling

- [ ] **S16.2.M4** (≈8 min) — Counterparty offer / negotiation flow [ENG 01 §10].
- [ ] **S16.2.M5** (≈8 min) — Produce pooling / collection-point flow [ENG 01 §10; BRD §2.2].
- [ ] **S16.2.M6** (≈6 min) — Reserve/lock semantics + expiry release [ENG 01 §10].

### Task 16.3 — Trust & compliance

- [ ] **S16.3.M7** (≈6 min) — Listing verification markers + review routing [UPS §2.9].
- [ ] **S16.3.M8** (≈8 min) — Anti-fraud vetting on high-value listings [PartB §3.3].
- [ ] **S16.3.M9** (≈6 min) — Brand-safety phrase scan applied to ads [UPS §4.2].

### Task 16.4 — S16 gate

- [ ] **S16.4.M10** (≈8 min) — Offer + negotiation AC walk [BRD §18.0].
- [ ] **S16.4.M11** (≈6 min) — Reserve-lock + expiry concurrency tests [ENG 05] → **exit pass**.

---

## Sprint 17 — Phase 2: escrow & payments

> **Phase 2 · Focus:** EscrowStatus model, escrow-ledger integration, dual sign-off, PayGuard seam + fallback, disputes. **Exit ticket:** dual sign-off + dispute wipe. **Primary:** PartB §3.3; UPS §13, §2.8; BRD §7.2.

### Task 17.1 — Escrow core

- [ ] **S17.1.M1** (≈8 min) — EscrowStatus model (pending, held, released, disputed, refunded) [PartB §3.3].
- [ ] **S17.1.M2** (≈8 min) — Escrow account ↔ ledger double-entry integration [PartB §3.3].
- [ ] **S17.1.M3** (≈8 min) — Dual sign-off release flow (buyer + seller) [PartB §3.3].
- [ ] **S17.1.M4** (≈6 min) — Automatic release on acceptance/delivery confirmation [PartB §3.3].

### Task 17.2 — PayGuard integration & fallback

- [ ] **S17.2.M5** (≈8 min) — PayGuard provider adapter behind partner gateway [UPS §13].
- [ ] **S17.2.M6** (≈8 min) — Fallback discrete/manual option when provider unavailable [PartB §3.3; BRD §7.2].

### Task 17.3 — Disputes & reconciliation

- [ ] **S17.3.M7** (≈8 min) — Dispute lifecycle + evidence upload [PartB §3.3].
- [ ] **S17.3.M8** (≈8 min) — Dispute → review decision → release/refund [UPS §2.9].
- [ ] **S17.3.M9** (≈6 min) — Nightly escrow balance reconciliation [UPS §2.8].

### Task 17.4 — S17 gate

- [ ] **S17.4.M10** (≈8 min) — Dual sign-off + fallback-path AC [BRD §18.0].
- [ ] **S17.4.M11** (≈8 min) — Dispute + reconciliation negative scenarios [ENG 05] → **exit pass**.

---

## Sprint 18 — Phase 2: vendor management

> **Phase 2 · Focus:** vendor identity/tiers/categories, safety-ranked recommendations, anti-abuse, vendor console. **Exit ticket:** tier + recommendation wipe. **Primary:** PartB §3.4; ENG 01 §10.

### Task 18.1 — Vendor identity & tiers

- [ ] **S18.1.M1** (≈8 min) — Vendor registration + document verification flow [PartB §3.4].
- [ ] **S18.1.M2** (≈6 min) — Vendor tier model (verification/rating-based) [PartB §3.4].
- [ ] **S18.1.M3** (≈8 min) — Vendor categories + service/product catalogs [ENG 01 §10].

### Task 18.2 — Recommendations & anti-abuse

- [ ] **S18.2.M4** (≈8 min) — Safety-ranked recommendations (geo + rating + compliance) [PartB §3.4].
- [ ] **S18.2.M5** (≈8 min) — Anti-abuse detection (fake listings, rating manipulation) [PartB §3.4].
- [ ] **S18.2.M6** (≈6 min) — Vendor quality metrics (complaints, fulfillment) [PartB §3.4].

### Task 18.3 — Vendor console

- [ ] **S18.3.M7** (≈8 min) — Vendor console: verify, tier change, suspend, quality view [AOC].
- [ ] **S18.3.M8** (≈6 min) — Vendor review-routing + audit of tier changes [UPS §2.9/§2.6].

### Task 18.4 — S18 gate

- [ ] **S18.4.M9** (≈6 min) — Tier + recommendation AC walk [BRD §18.0].
- [ ] **S18.4.M10** (≈6 min) — Anti-abuse detection unit tests [ENG 05] → **exit pass**.

---

## Sprint 19 — Phase 2: consoles & exit

> **Phase 2 · Focus:** marketplace/vendor/land-cert/escrow ops consoles, Phase-2 regression + sign-off. **Exit ticket:** P2 accepted. **Primary:** AOC; ENG 05/07; BRD §4.3.

### Task 19.1 — Marketplace & vendor ops consoles

- [ ] **S19.1.M1** (≈8 min) — Marketplace ops console (listings, flags, takedown) [AOC].
- [ ] **S19.1.M2** (≈8 min) — Vendor ops console completion (bulk ops, reports) [AOC].

### Task 19.2 — Land & finance consoles

- [ ] **S19.2.M3** (≈8 min) — Land-part certification admin screens [AOC].
- [ ] **S19.2.M4** (≈10 min) — Escrow ops console (hold, release, dispute, refund) [AOC].

### Task 19.3 — Phase-2 acceptance

- [ ] **S19.3.M5** (≈8 min) — Phase-2 regression + integration suite [ENG 05/07].
- [ ] **S19.3.M6** (≈8 min) — Escrow-adjacent negative scenarios (double release, orphan hold) [ENG 05].
- [ ] **S19.3.M7** (≈6 min) — Phase-2 sign-off checklist + P3 readiness [BRD §4.3] → **exit pass**.
## Sprint 20 — Phase 3: pest & disease reporting

> **Phase 3 · Focus:** crop-report intake, detection pipeline, regional aggregation, alert tiers, anti-panic gating, anti-abuse. **Exit ticket:** alert-tier wipe. **Primary:** UPS §4.3; PartB §3.4.

### Task 20.1 — Report intake

- [ ] **S20.1.M1** (≈8 min) — Crop report intake (type, severity, location, image) [UPS §4.3].
- [ ] **S20.1.M2** (≈8 min) — Report pipeline routing to detection watch [UPS §4.3].
- [ ] **S20.1.M3** (≈6 min) — Regional/crop aggregation for visibility [UPS §4.3].

### Task 20.2 — Detection & response

- [ ] **S20.2.M4** (≈8 min) — Detection thresholds + regional/national alert tiers [UPS §4.3].
- [ ] **S20.2.M5** (≈8 min) — Anti-panic gating (verification before mass alert) [UPS §4.3].
- [ ] **S20.2.M6** (≈6 min) — Official response status + bulletin publishing [UPS §4.3].
- [ ] **S20.2.M7** (≈6 min) — Anti-abuse on reports (spam/prank suppression) [PartB §3.4].

### Task 20.3 — S20 gate

- [ ] **S20.3.M8** (≈8 min) — Detection-to-alert tier AC walk [BRD §18.0].
- [ ] **S20.3.M9** (≈6 min) — Anti-panic + anti-abuse negative tests [ENG 05] → **exit pass**.

---

## Sprint 21 — Phase 3: knowledge bank & community

> **Phase 3 · Focus:** knowledge content engine, search, quick-info, farmer forum, moderation, governing rules on chemicals. **Exit ticket:** search + moderation wipe. **Primary:** ENG 01 §12; PartB §3.2/§3.3; Legal 10.

### Task 21.1 — Knowledge content engine

- [ ] **S21.1.M1** (≈8 min) — Knowledge-article model + taxonomy [ENG 01 §12].
- [ ] **S21.1.M2** (≈8 min) — Full-text search + autocomplete (incl. local languages) [ENG 01 §12].
- [ ] **S21.1.M3** (≈6 min) — Quick-info module (crop, input, pest snippets) [ENG 01 §12].

### Task 21.2 — Community & moderation

- [ ] **S21.2.M4** (≈8 min) — Farmer forum (topics, posts, replies) + reputation [ENG 01 §12].
- [ ] **S21.2.M5** (≈8 min) — Moderation queue + denial rules [PartB §3.2/§3.3].
- [ ] **S21.2.M6** (≈6 min) — Chemical/product governing rules enforced on content [Legal 10].

### Task 21.3 — S21 gate

- [ ] **S21.3.M7** (≈6 min) — Search + autocomplete AC walk [BRD §18.0].
- [ ] **S21.3.M8** (≈6 min) — Moderation + chemical-rule wipe [ENG 05] → **exit pass**.

---

## Sprint 22 — Phase 3: certifications, consoles, staffing

> **Phase 3 · Focus:** certification lifecycle with verification research, fraud checks, badges, remaining consoles, staffing & training. **Exit ticket:** cert + console wipe. **Primary:** UPS §4.6, §2.9; AOC; Legal 10; BRD §2.4.

### Task 22.1 — Certifications

- [ ] **S22.1.M1** (≈8 min) — Certification types + scope configuration [UPS §4.6].
- [ ] **S22.1.M2** (≈8 min) — Verification-research workflow (documents, site visit) [UPS §4.6].
- [ ] **S22.1.M3** (≈8 min) — Certification lifecycle (request → verify → granted → expiry/revoke) [UPS §4.6].
- [ ] **S22.1.M4** (≈6 min) — Fraud detection on certification claims [UPS §4.6].
- [ ] **S22.1.M5** (≈6 min) — Badge computation for profiles + marketplace [UPS §4.6; DS2].
- [ ] **S22.1.M6** (≈6 min) — Cert reviews routed to review engine [UPS §2.9].

### Task 22.2 — Phase-3 consoles

- [ ] **S22.2.M7** (≈8 min) — Content-governance console (approve, publish, deny) [AOC].
- [ ] **S22.2.M8** (≈6 min) — Content-review ops with evidence trail [AOC; UPS §4.2].
- [ ] **S22.2.M9** (≈8 min) — Analytics console (all KPIs + exports) [AOC; ENG 06].
- [ ] **S22.2.M10** (≈8 min) — Compliance / NDPR console (consents, exports, erasure) [Legal 10].
- [ ] **S22.2.M11** (≈6 min) — Feature-flag / config console (all toggles) [UPS §2.9].

### Task 22.3 — Staffing & training

- [ ] **S22.3.M12** (≈6 min) — Support-staff role model + handoff runbook [BRD §2.4].
- [ ] **S22.3.M13** (≈8 min) — Training + onboarding kit for content/moderation teams [BRD §2.4].

### Task 22.4 — S22 gate

- [ ] **S22.4.M14** (≈8 min) — Certification AC walk incl. revoke/expiry [BRD §18.0].
- [ ] **S22.4.M15** (≈6 min) — Console governance + NDPR wipe [ENG 05] → **exit pass**.

---

## Sprint 23 — Hardening 1: performance, offline, accessibility, design conformance

> **Hardening · Focus:** load tests, indexing/caching, full offline matrix, WCAG + DS2 design conformance. **Exit ticket:** load + a11y pass. **Primary:** ENG 05; DS2; BRD §18.0.

### Task 23.1 — Performance & scale

- [ ] **S23.1.M1** (≈10 min) — Load tests (auth, listings, geo queries, alerts) [ENG 05].
- [ ] **S23.1.M2** (≈8 min) — Index review + query plans on hot paths [ENG 02/05].
- [ ] **S23.1.M3** (≈8 min) — Caching strategy for read models [ENG 02].

### Task 23.2 — Offline & resilience

- [ ] **S23.2.M4** (≈8 min) — Full offline-matrix regression across all modules [ENG 05 ℹ12].
- [ ] **S23.2.M5** (≈6 min) — Retry/backoff + queue-safety audit [ENG 05].

### Task 23.3 — Accessibility & design conformance

- [ ] **S23.3.M6** (≈8 min) — WCAG conformance pass on app + consoles [DS2].
- [ ] **S23.3.M7** (≈6 min) — DS2 token/spacing/typography conformance both modes [DS2].
- [ ] **S23.3.M8** (≈6 min) — Color-contrast + touch-target audit [DS2].

### Task 23.4 — S23 gate

- [ ] **S23.4.M9** (≈8 min) — Load + resilience sign-off record [BRD §18.0].
- [ ] **S23.4.M10** (≈6 min) — Accessibility + design-conformance reviews closed [DS2] → **exit pass**.

---

## Sprint 24 — Hardening 2: security, business continuity, UAT, go-live

> **Hardening · Focus:** SSO/MFA + security scan, DR drill vs RPO/RTO, data freeze, UAT, training, go/no-go. **Exit ticket:** final readiness sign-off → LAUNCH. **Primary:** PartB §3; ENG 07/09; QA 13/14; BRD §4.3; ENG 06.

### Task 24.1 — Security hardening

- [ ] **S24.1.M1** (≈8 min) — SSO/MFA + session hardening review [PartB §3].
- [ ] **S24.1.M2** (≈8 min) — Permission-matrix security smoke across consoles [PartB §3.2].
- [ ] **S24.1.M3** (≈6 min) — Dependency + header/OWASP-oriented scan [ENG 07].
- [ ] **S24.1.M4** (≈8 min) — Breach/incident runbook + response drill [PartB §3].

### Task 24.2 — Business continuity & data freeze

- [ ] **S24.2.M5** (≈8 min) — DR drill (restore + failover) vs declared RPO/RTO [ENG 09; BRD §11.5].
- [ ] **S24.2.M6** (≈6 min) — Reference data freeze + snapshot of seeds [ENG 06].

### Task 24.3 — UAT, training, go-live

- [ ] **S24.3.M7** (≈8 min) — UAT with stakeholders across all four phases [QA 14].
- [ ] **S24.3.M8** (≈6 min) — Train-the-trainer + runbook handoff [BRD §2.4].
- [ ] **S24.3.M9** (≈8 min) — Go/no-go checklist + launch baseline snapshot [BRD §4.3].
- [ ] **S24.3.M10** (≈6 min) — Cutover, freeze list, rollback plan rehearsed [ENG 09].

### Task 24.4 — Final gate

- [ ] **S24.4.M11** (≈6 min) — Final readiness sign-off + launch decision record [BRD §4.3] → **LAUNCH**.

---

## Closing notes

- **Verification:** every micro-task's AC is met by completing its description; sprint-level gates run the checklists in `Doc\04-QA\13-QA-Release-Checklists.md` and `14-UAT-Protocol.md`.
- **Sizing discipline:** re-slice any micro-task that grows beyond 10 minutes into two smaller ones; record the split in the project log. Post-launch fixes and change requests enter new sprints through the backlog process in `BRD §7.1` — this document stays the frozen S1–S24 baseline.
- **Source of truth:** where this document and the sprint plan (`Plan\Farm-Basics-Sprint-Plan.md`) disagree, the sprint plan governs effort/resourcing; this document governs *work breakdown and order*.