# Farm Basics — Implementation Sprint Plan

**Unified System & Admin Operations Console** | Version 1.0 | Status: Draft for Review

**Scope:** A dependency-aware, implementation-ready sprint plan for the Farm Basics Nigerian smallholder farm platform, derived exclusively from the specification set in `Doc/`.

> **Status notes**
> - All sprint durations, team-loading, and calendar windows are **indicative planning estimates** and require confirmation against live team capacity (see §2 & §13 — *Requires Clarification*).
> - No requirement in this plan is invented: every feature, rule, alert, and acceptance criterion traces to a source section cited inline. Where a decision is still open in the documents, it is explicitly marked **[DEFAULT — confirm]** or *Requires Clarification* and must not be silently decided by the implementing team.
> - The source filenames `Doc/06-Operations/Part A — Platform Operations.md` and `Doc/06-Operations/Part B — Administration & Governance.md` contain an em-dash (`U+2014`) in their names, not a hyphen. They are referred to here as **UPS** (*Part A*) and **AOC** (*Part B*).

---

## 1. Executive Summary

Farm Basics is a thirteen-domain platform plus a full RBAC-governed administration console: identity, field boundary recording, crop production lifecycle, land history, soil advice & test labs, pest & disease outbreak reporting, knowledge bank, certifications, partners & vendors, marketplace, and accounts — operated through one admin console with twelve roles and thirteen consoles.

The BRD provides the phasing backbone but deliberately does **not** sequence delivery into work units. This plan turns BRD §4.3's four phases into a single dependency-respecting work breakdown of **~24 two-week sprints** (≈48 weeks, indicative), each ending in a potentially shippable increment and passing the corresponding acceptance criteria from BRD §18:

| Phase | Sprints | Focus | Hard gate |
|---|---|---|---|
| **Phase 0 — Foundation** (BRD §4.3) | S1–S3 | Cross-cutting plumbing only: Identity/Geography/TrustProfile, review engine, notifications, audit, concurrency/idempotency, RBAC + admin basement | NDPA/NDPC data-handling sign-off |
| **Phase 1 — Pilot** | S4–S10 | Register → record field → guided crop cycle → automatic bookkeeping; plus the admin consoles needed to operate it | Zero unbalanced `JournalEntry`; §3 launch metrics met in pilot region |
| **Phase 2 — Market Linkage & Land Provenance** | S11–S16 | Land History, Soil Advice & Labs, Marketplace (+PayGuard escrow or §12.4.4 contact-only fallback), Vendors & Partners | **CBN escrow-licensing posture (BRD §7.2)**; Land-history export disclaimer (§7.4) |
| **Phase 3 — Trust, Safety & Full Governance** | S17–S21 | Pest & Disease, Knowledge Bank, Certifications; full review operations, Trust & Safety, Compliance/NDPR | Reviewer staffing entry gate; NDPA erasure sign-off |
| **Hardening & Launch** | S22–S24 | Performance/offline/accessibility/security hardening; QA/UAT/DR/permission-matrix seed verification; launch | Full production-readiness checklist (§18) |

The three decisions with the highest consequence are, in order: (1) the **CBN escrow-licensing question** (BRD §7.2) that gates the escrow mode of the marketplace; (2) **PayGuard production readiness** (UPS §12.0/§12.4.4) which controls whether Phase 2 ships escrow or the contact-only fallback; and (3) **Phase 3 reviewer staffing** (BRD §4.3 note) — a launch-blocking failure mode, not a tunable alert.

## 2. Documentation Analysis

The planning inputs and their role, plus the discrepancies the plan works around.

| Document | Role in this plan | Notes |
|---|---|---|
| `Doc/01-Business/BRD-v2-Nigeria-Farm-Platform.md` (**BRD**) | **Authoritative business authority.** Phasing (§4.3), business objectives OBJ-01…14 (§2), launch success criteria (§3), regulatory landscape (§7), cross-cutting principles F-XC (§8), functional requirements F-*/A-* (§9/§10), NFRs (§11), traceability matrix (§13), risk register (§17), acceptance criteria (§18), open decisions (§19), master alert registry (§20). | v2.0 (2026-08-25); supersedes incomplete v1.0. Requirement IDs use `F-<domain>-##` / `A-<console>-##` deliberately distinct to prevent v1.0's ID collisions. |
| `Doc/06-Operations/Part A — Platform Operations.md` (**UPS**) | **Engineering authority** for every platform domain: §2 cross-cutting (11 patterns), §3–§13 domains, §14 Master Alert Registry, §15 Master Open-Decisions Register, geodesic area rule (§4.12), PayGuard escrow (§12.4) and contact-only fallback (§12.4.4), review-category table (§2.4). | Filename contains an em-dash. Sections §5–§9 also carry C# skeletons usable as implementation seeds. |
| `Doc/06-Operations/Part B — Administration & Governance.md` (**AOC**) | **Admin console authority:** RBAC model (§1–§3), 12-role catalog (§2), permission matrix (§3), 13 consoles (§4–§16), admin auth & break-glass (§17), master admin alert registry ALT-AD01–08 (§18), acceptance criteria (§20), open decisions (§21). | `Auditor` is the only read-only role and cannot dual-hold mutation rights without a logged `SuperAdmin` exception. |
| `Doc/01-Business/PRD-Master-Volume.md` + `PRD-Volume-A-Foundation.md`, `-B-Knowledge-and-Risk.md`, `-C-Trade-and-Trust.md`, `-D-Admin-Operations.md` | Product-view companion to the BRD, one volume per capability group. Used to enrich user stories and acceptance detail; treated as consistent with UPS/AOC unless it conflicts (none found that affect ordering). | *Requires Clarification* if any PRD volume conflicts with Part A/B on behavior — checked during each sprint's DoD. |
| `Doc/02-Engineering/01-ENG-System-Architecture-Document.md` … `09-ENG-Disaster-Recovery-Runbook.md`, `06-DATA-Master-Data-and-Content-Sourcing-Strategy.md` | Architecture, schema/ERD, API, security & threat model, third-party integrations, CI/CD & environments, observability, DR runbook, master-data sourcing. | Allocated mostly to Phase 0 scaffolding and the S22–S24 hardening window; ERD is the DB-change baseline. |
| `Doc/03-Legal/10-LEGAL-Regulatory-Compliance-and-Corporate-Strategy-Memo.md`, `11-LEGAL-Terms-of-Service-and-Privacy-Policy-DRAFT.md` | Legal/ToS scaffolding. | Gated by BRD §7 sign-offs per phase; ToS is DRAFT — finalization scheduled with the §7.6 gates. |
| `Doc/04-QA/12-QA-Master-Test-Plan.md`, `13-QA-UAT-and-Pilot-Protocol.md`, `14-QA-Accessibility-Conformance-Plan.md` | Test strategy, UAT/pilot protocol, accessibility conformance. | Doc 12 flags three test categories (concurrency races, offline disconnects, constraint violations) as under-specified — *Requires Clarification*; concretized here in §11. |
| `Doc/05-Design/Farm-Basics-Design-System-2.md`, `farm-basics-design-system.md`, `farm-basics-styleguide-2.html`, `farm-basics-styleguide.html` | Two experience modes (farmer app + admin console), semantic token system, concentric-arc progress system, offline-first guidance, performance budgets. | **v2 (Design System 2) is the operating standard used in every DoD below.** |
| `Doc/Archived/` | v1-era per-domain specs (including the `unified-platform-spec.md` BRD §0.3 cites). | Cross-reference only; superseded by Part A/B. Not used as authority. |

**Documentation discrepancies the plan works around (non-blocking, logged to §13):**

1. **Review-category count**: UPS §2.4's prose says "ten review categories," but its own table lists **eleven** codes (`IDENTITY_NIN_MISMATCH` … `FORUM_PROMOTION`). Plan assumes the table (11 categories) is authoritative; *Requires Clarification*.
2. **Stale cross-reference**: BRD v2 cites "§5.3" for the phased delivery plan in several places (§7.2, §22 sign-off note) while phasing actually lives at **§4.3** (in v1.0 phasing may have been §5.3). Plan uses §4.3.
3. **Part A/B section glyphs**: internal cross-references render as `A§12.4` / `A§5.8` style (a mangled section glyph); all resolve to the same-section numbering used here ([§] refers to the indicated heading regardless of glyph).
4. **Estimation vacuum**: neither BRD nor either Part spec sizes effort, staffing, or cost. All sprint durations here are indicative (*Requires Clarification*).

## 3. Unified Application / Architecture Overview

One platform, two surfaces, one substrate.

```
                         ┌──────────────────────────────────────────────┐
                         │  FARMER-FACING APP  &  ADMIN CONSOLE (AOC)   │
                         │  Design System 2 · two experience modes      │
                         └───────────────────┬──────────────────────────┘
                                             │ RBAC (AOC §1–3) · 12 roles
┌────────────────────────────────────────────▼───────────────────────────────┐
│ CROSS-CUTTING FOUNDATION (UPS §2 / BRD §8) — built first, governs all else │
│ Identity & Roles · Geography · TrustProfile · Review engine (11 categories)│
│ Notification/IVR · Append-only Audit · Concurrency & Idempotency           │
│ Computed-not-stored + nightly reconciliation · Config-driven rules         │
│ Polymorphic reference · Unified Field entity                              │
└────────────────────────────────────────────┬───────────────────────────────┘
                   13 DOMAIN MODULES        │           13 ADMIN CONSOLES
   Registration·Field·Crop·Land·Soil·Pest·  │   MasterData · IdentityTrust ·
   Knowledge·Certifications·Vendors·        │   ReviewOps · VendorPartner ·
   Marketplace·Accounts                     │   MarketplaceOps · FieldOps ·
                                            │   Finance · ContentGov · LandCert ·
                                            │   Trust&Safety · Analytics ·
                                            │   Config&Flags · Compliance/NDPR
┌────────────────────────────────────────────▼───────────────────────────────┐
│ DATA · PostGIS geodesic geometry (UPS §4.12) · double-entry ledger (UPS §13)│
│ append-only audit · nightly reconciliation jobs · seed data (geo, labs,     │
│ soil catalog, CropProfile, review categories, permission matrix)            │
└────────────────────────────────────────────┬───────────────────────────────┘
                                             │ integration contracts
        ┌───────────────┬────────────────────┼───────────────────┬──────────────┐
   PayGuard (escrow,   Prembly (NIN       Registry APIs      IVR/SMS/OTP    DR/Backup
   NIN+liveness,       lookup)            (GlobalGAP portal,  delivery      (Doc 09)
   split-PIN IVR)                          NAFDAC/NASC/NAQS)  (UPS §2.5)
```

**Operating principles that shape every sprint** (see BRD §8 / UPS §2):
- **Cross-cutting first.** No domain is built on an ad hoc identity/notification/review stand-in (UPS §2's entire reason for existing). Phase 0 exists precisely to prevent this rework.
- **Computed, never stored.** Trust tiers, badges, reports, land-history timeline, financial statements, reputation signals are computed views over source records (§2.8), with a nightly reconciliation job and zero independently-persisted aggregates.
- **Config-driven rules.** A new review need, alert, certificate type, or fee is a new **row** in a governed table — never scattered `if/else`.
- **Idempotent + offline-first** for all mobile/field interactions (UPS §2.7, §4.16); low-literate farmer access rides the **fob + agent** path only (UPS §3.2).
- **Geodesically correct geometry** (PostGIS `geography`, UPS §4.12) — naive planar area computation is a *defect* (OBJ-03).
- **Governed, audited escalation.** Every "route to human" terminates in a console with role + permission + audit (BRD §1.3 property 3).

## 4. Feature & Requirement Inventory

Requirement families from BRD §9/§10/§11, mapped to source and to the sprint group that delivers them. (Per-row ID counts for `F-*`/`A-*` are maintained in BRD §13's traceability matrix; sprint-level accounting is *Requires Clarification* until the matrix is digitized.)

| Domain / Family | Requirement IDs (BRD) | Source authority | Delivered in |
|---|---|---|---|
| Cross-cutting foundation | `F-XC-*` | BRD §8; UPS §2 | S1–S3 |
| Registration & Identity | `F-ID-*` | UPS §3; BRD §9.1 | S4 |
| Field Boundary Recording | `F-FB-*` | UPS §4; BRD §9.2 | S5 |
| Crop Production Lifecycle | `F-CY-*` | UPS §5; BRD §9.3 | S6–S7 |
| Land History | `F-LH-*` | UPS §6; BRD §9.4 | S11 |
| Soil Advice & Soil Test Labs | `F-SL-*` | UPS §7; BRD §9.5 | S12 |
| Pest & Disease Outbreak | `F-PD-*` | UPS §8; BRD §9.6 | S17 |
| Knowledge Bank | `F-KB-*` | UPS §9; BRD §9.7 | S18 |
| Certifications | `F-CT-*` | UPS §10; BRD §9.8 | S19 |
| Partners & Vendors | `F-VN-*` | UPS §11; BRD §9.9 | S15 |
| Marketplace | `F-MP-*` | UPS §12; BRD §9.10 | S13–S14 |
| Accounts (ledger) | `F-AC-*` | UPS §13; BRD §9.11 | S8 |
| RBAC foundation | `A-RB-*` | AOC §1–§3; BRD §10.1 | S3 |
| Master Data Governance console | `A-MD-*` | AOC §4; BRD §10.2 | S9 |
| Identity & Org Trust console | `A-IT-*` | AOC §5; BRD §10.3 | S3 |
| Review Operations console | `A-RV-*` | AOC §6; BRD §10.4 | S3 (baseline) / S20 (full) |
| Vendor & Partner console | `A-VP-*` | AOC §7; BRD §10.5 | S15 |
| Marketplace Operations console | `A-MO-*` | AOC §8; BRD §10.6 | S13–S14 |
| Field Operations & Extension console | `A-FO-*` | AOC §9; BRD §10.7 | S9 |
| Financial Administration console | `A-FN-*` | AOC §10; BRD §10.8 | S8–S9 (baseline) / S14 (escrow) |
| Content & Knowledge Governance console | `A-CG-*` | AOC §11; BRD §10.9 | S18–S19 |
| Land & Certification Oversight console | `A-LC-*` | AOC §12; BRD §10.10 | S11 (land) / S19 (cert) |
| Trust & Safety console | `A-TS-*` | AOC §13; BRD §10.11 | S20 |
| Analytics & Reporting dashboard | `A-AR-*` | AOC §14; BRD §10.12 | S9 (baseline) / S21 (full) |
| System Config & Feature Flags | `A-CF-*` | AOC §15; BRD §10.13 | S3 (baseline) / S21 (full) |
| Audit, Compliance & NDPR console | `A-CP-*` | AOC §16; BRD §10.14 | S3 (audit search) / S21 (NDPR) |
| Admin Authentication & Security | `A-SC-*` | AOC §17; BRD §10.15 | S3 (baseline) / S23 (hardening) |
| Non-functional requirements | `NFR-*` | BRD §11 | S1–S24 (S22–S24 enforcement) |
| Alert registries | BRD §20 `ALT-*`; UPS §14; AOC §18 `ALT-AD01–08` | S1–S21 (wired in-domain) |

**Master-data seed inventory** (06-DATA / UPS appendices): canonical Nigerian Geography (States→LGAs→Wards→Communities), Soil Test Lab seed directory & Soil Test Catalog tiers, `CropProfile` catalog, `ReviewCategory`/`ReviewerQualification` seeds, full permission-matrix seed, `NotificationTemplate` seeds. Authored in parallel in Phase 0 (§14), enforced by the Master Data Governance console (AOC §4.0 Draft→Review→Published).

## 5. System / Module Breakdown

Buildable modules, grouped by phase, with their owning console and source.

**Phase 0 — Foundation modules (S1–S3)**
- Identity, `RoleProfile`, Organization core (UPS §2.1)
- Geography service + Nigerian seed data (UPS §2.2)
- Trust & Verification Tiers / `TrustProfile` (UPS §2.3)
- Review engine: `ReviewCategory` (11 rows), `ReviewQueueItem` lifecycle, `ReviewerProfile/Qualification`, COI exclusion, SLA/escalation, QA sampling, appeals (UPS §2.4)
- Notification & Alerting incl. shared `IvrConfirmationFlow` / VoiceOTP (UPS §2.5)
- Append-only audit trail; concurrency & idempotency framework; computed-not-stored + nightly reconciliation jobs; config-driven rule tables; polymorphic reference (UPS §2.6–§2.10)
- Unified `Field` entity (UPS §2.11)
- RBAC foundation + 12-role catalog + scope resolution + precedence rules (AOC §1–§3)
- Admin auth & security: least privilege, `SuperAdmin` override, break-glass (AOC §17)
- Identity & Organization Trust console (AOC §5)
- Unified audit search (AOC §16.1) and master-data/console alert wiring to UPS §14 / AOC §18
- Dev/test/staging/prod environments + CI/CD + observability + DR baseline (ENG 01, 07, 08, 09)

**Phase 1 — Pilot loop modules (S4–S10)**
- Registration & Identity full: NIN/Prembly, phone+email OTP, liveness [confirm], fob + agent-assisted low-literate path, tablet binding, organizations (UPS §3)
- Field Boundary Recording: GPS capture, geodesic area (PostGIS `geography`), filters, raw-vs-final geometry, state machine, offline idempotent API (UPS §4)
- Crop Production Lifecycle: `CropProfile`, stage state machine, operation task catalog, validation rule engine, flags/overrides, alerts (UPS §5)
- Accounts: double-entry ledger, Chart of Accounts + posting rules, fixed assets & depreciation, period close, computed reports (UPS §13)
- Master Data Governance console (AOC §4); Field Operations & Extension console (AOC §9); Analytics baseline (AOC §14)

**Phase 2 — Market linkage & provenance modules (S11–S16)**
- Land History: `FieldTenancy`, computed timeline, overlap detection & resolution, privacy, export + §7.4 disclaimer (UPS §6)
- Soil Advice & Labs: sample lifecycle, GPS-enforced collection, recommended crops, adjustment prerequisite gate, lab registration/accreditation (UPS §7)
- Marketplace core: listings, offers/RFQ, buyer requests + pooling, concurrency-safe reservations, audit trail (UPS §12.1–12.3)
- Marketplace escrow: PayGuard payment links, `EscrowStatus` lifecycle, split-PIN/IVR release, dispute flow — or §12.4.4 contact-only fallback if PayGuard not ready (UPS §12.4)
- Vendors & Partners: vendor identity, category mechanics, tiered verification, safety-ranked recommendations (never commercially overridden for regulated inputs), anti-abuse (UPS §11)
- Vendor & Partner console (AOC §7); Marketplace Operations console (AOC §8); Financial Admin escrow extension (AOC §10); Land & Certification Oversight — land-overlap portion (AOC §12)

**Phase 3 — Trust, safety & full governance modules (S17–S21)**
- Pest & Disease: reports, analysis pipeline, regional aggregation + anti-panic corroboration gate, alert tiers, anti-abuse (UPS §8)
- Knowledge Bank: content model, search, forum, quick-info-anywhere, chemical/fertilizer safety governance (UPS §9)
- Certifications: types & scope, per-type verification, lifecycle, fraud prevention, computed badge display (UPS §10)
- Content & Knowledge Governance console (AOC §11); full Review Operations console (AOC §6); Trust & Safety console (AOC §13); Compliance/NDPR console (AOC §16); remaining Analytics + System Config & Feature Flags (AOC §14/§15)

**Hardening modules (S22–S24)**
- Performance/offline/accessibility conformance (Design System 2), load & DR drills, security hardening (SSO/MFA, threat-model tests), permission-matrix seed verification, UAT/pilot protocol execution (Doc 13), final legal sign-offs, launch runbooks.

## 6. Dependency Map

**Module dependency graph (build order):**

```
Identity+Org core ──► Registration & Identity ──► Field Boundary ──► Crop Lifecycle ──► Accounts
        │                        │                    │                 │                  │
        │             TrustProfile/Review/Notif       │          (tasks→journal)   (tasks→journal)
        ▼                        ▼                    ▼                 ▼                  ▼
   RBAC + Admin basement ──►  MasterData console  ──► Field Ops console ◄── Analytics baseline
                                        │
                ┌───────────────────────┼───────────────────────┐
                ▼                       ▼                       ▼
           Land History          Soil Advice & Labs     Marketplace core (§12.1–12.3)
                │                       │                       │
                └──────► [Phase 1 data exists]  ◄───────────────┤
                                                ▼               ▼
                                       [CBN §7.2 gate] ──► Marketplace escrow (PayGuard §12.4)
                                                │        └── or §12.4.4 contact-only fallback
                                                ▼
                                       Vendors & Partners (recommendations fired from Phase 1 crop tasks)
                                                │
                ┌───────────────────────────────┼───────────────────────────────┐
                ▼                               ▼                               ▼
     Pest & Disease (needs review engine     Knowledge Bank (needs KB        Certifications (registry
     + corroboration-gated alerting)         chemical governance gate)       research in Phase 2)
                                                │
                                                ▼
                          Trust & Safety · Review Ops (full) · Compliance/NDPR ──► Hardening/Launch
```

**Hard gates in the dependency map:**
1. **Phase 0 exit** before any real farmer workflow (BRD §4.3 Phase 0 exit criteria).
2. **CBN escrow-licensing posture** (BRD §7.2) resolved before escrow mode payload; otherwise ship §12.4.4 contact-only fallback — identical reservation logic either way, so no re-architecture later (UP §12.4.4).
3. **Land History export disclaimer + counsel review** (BRD §7.4) before the export feature ships.
4. **Phase 3 reviewer staffing** (BRD §4.3 entry gate) — headcount per `ReviewCategory` vs. projected volume before go-live; `ALT-RV02` is launch-blocking.
5. **NDPA erasure sign-off** (BRD §7.1/§7.6) before the Compliance console's Data Subject Request workflow goes live.

**External dependencies:** PayGuard (escrow, NIN+liveness, split-PIN IVR; UPS §12.0), Prembly (NIN lookup), registry APIs (GlobalGAP Supply Chain Portal confirmed; NAFDAC/NASC/NAQS — re-confirm before hardcoding, UPS §10.2), IVR/SMS/OTP carriers, PostGIS/exact Postgres, object storage, observability stack (ENG 08), DR target (ENG 09).

**Data dependencies:** canonical Geography seed must precede all regional matching/reviewer scope; `CropProfile` and Chart of Accounts seeds must be Governed before Phase 1; full permission-matrix seed required before Phase 3 consoles expose mutating actions; lab seed & soil-test catalog before Phase 2 soil flows (UPS appendices).

## 7. Implementation Strategy

**Sequencing policy** — derived from BRD §4.3, not invented:
- A phase is not "in scope" until its dependencies (previous phases) are live and stable.
- Each sprint ends potentially shippable against BRD §18 acceptance criteria for the features it touches.
- Cross-cutting modules ship in Phase 0 with **zero** domain shortcuts; the Phase 0 exit criteria are the gate into Phase 1.

**Delivery mechanics:**
- Two-week sprints; one feature thread per sprint (or two parallel threads in Phase 2 under capacity — §14).
- Every domain's alert set is wired to a `NotificationTemplate` + `ReviewCategory`/`AlertRegistry` row at the time the domain ships, never retrofitted (§2.5/§14).
- Derived data (trust tiers, badges, reports, timeline, financials) is always computed views + nightly reconciliation — never second stores.
- Mobile/field flows ship offline-first + idempotent from day one (upsert patterns, `Idempotency-Key`), not parity with web.
- The Master Data Governance console owns every governed table (`CropProfile`, Chart of Accounts, `ReviewCategory`, Geography, catalogs) with Draft→Review→Applied dual sign-off before that table is writable by anyone (AOC §4.0).
- Design System 2 (tokens, concentric-arc progress, both experience modes, offline/perf budgets) is enforced as implementation-time DoD across all sprints, verified in S22.

**Governance gates per phase** (BRD §7.6): Phase 0 — NDPA/NDPC data-handling design; Phase 1 — none beyond Phase 0; Phase 2 — **CBN escrow posture (hard)** + land-history export disclaimer + FCCPC review if vendor volume material; Phase 3 — NDPA erasure-mechanism sign-off + NAFDAC/NASC/NAQS registry-accuracy review.

**Estimation posture:** every sprint below is sized in *relative* terms (scope defined by requirements cited); absolute person-weeks, calendar dates, and cost are *Requires Clarification* (no authority in the doc set sizes them). The sprint table should therefore be read as a dependency-correct sequence, not a commitment.

## 8. Detailed Sprint Plan

> Legend — **F0/F1/F2/F3**: BRD §4.3 phase; **HL**: hardening/launch. Durations: 2 weeks each, indicative.

| Sprint | Phase | Focus | Primary requirements | Exit ticket (Definition of Done pointer) |
|---|---|---|---|---|
| **S1** | F0 | Foundations I — scaffolding, geography, audit, idempotency | BRD §8 (§11 NFRs); UPS §2.2/§2.6/§2.7/§2.8/§2.9/§2.10 | CI/CD green on all envs; audit trail end-to-end; geo seed loaded; reconciliation jobs runnable |
| **S2** | F0 | Foundations II — identity, trust, notifications, review engine | UPS §2.1/§2.3/§2.4/§2.5/§2.11; BRD §8 | `Identity` create/verify; `ReviewQueueItem` created/assigned/resolved for `IDENTITY_NIN_MISMATCH`; notification + VoiceOTP flow |
| **S3** | F0 | Admin basement — RBAC, identity/org trust console, security | AOC §1/§2/§3/§5/§16.1/§17; BRD §10.1/§10.3/§10.13/§10.15 | **Phase 0 exit**: `SuperAdmin` + `IdentityTrustAdmin` operational; break-glass actioned+audited; NDPA design sign-off input ready |
| **S4** | F1 | Registration & Identity (full) | UPS §3; BRD §9.1/§18.1 | NIN+OTP verification; fob+agent low-literate path live; org onboarding; `ALT-*` §3.6 wired |
| **S5** | F1 | Field Boundary Recording | UPS §4; BRD §9.2/§18.2 | Field saved offline+idempotently; geodesic area within ±5% of survey sample; boundary repair flow |
| **S6** | F1 | Crop Lifecycle I — CropProfile, stages, task catalog | UPS §5.1–§5.3; BRD §9.3 | Stage-gated state machine driving a full example crop cycle |
| **S7** | F1 | Crop Lifecycle II — validation engine, flags, alerts | UPS §5.4–§5.6; BRD §9.3/§18.3 | `OperationLog` validation + flag/override audit; offline conflict strategy fixed; crop alerts wired |
| **S8** | F1 | Accounts — double-entry ledger | UPS §13; BRD §9.11/§18.11 | Ledger + posting rules; fixed assets; period close; computed statements; **zero unbalanced entries** |
| **S9** | F1 | Pilot admin — Master Data Gov, Field Ops, Analytics baseline | AOC §4/§9/§14; BRD §10.2/§10.7/§10.12 | Governed `CropProfile`/CoA edits via dual sign-off; extension console; analytics dashboard v1 |
| **S10** | F1 | Pilot integration & exit | BRD §4.3 (Phase 1), §3, §18.0/§18.1–3/§18.11/§18.12 | **Phase 1 exit**: launch metrics instrumented (2,000 IDs / 1,500 fields / 800 CC-02 in-window targets); Region-scope RBAC verified; Phase 2 legal pack submitted |
| **S11** | F2 | Land History | UPS §6; BRD §9.4/§18.4 | `FieldTenancy` + timeline computed from source records; overlap detection; console resolution; export + §7.4 disclaimer |
| **S12** | F2 | Soil Advice & Soil Test Labs | UPS §7; BRD §9.5/§18.5 | GPS-enforced collection; soil-adjustment prerequisite gate; lab accreditation; zero out-of-radius `Collected` accepted |
| **S13** | F2 | Marketplace core | UPS §12.1–12.3; BRD §9.10/§18.10 | Listings/offers/pooling + concurrency-safe reservations (zero negative `RemainingQuantity`) |
| **S14** | F2 | Marketplace escrow (or contact-only fallback) + dispute ops | UPS §12.4 (+§12.4.4); AOC §8/§10; BRD §7.2 | **CBN gate**: escrow E2E via PayGuard *or* §12.4.4 fallback; EscrowStatus lifecycle; disputes require MarketplaceOps + Finance dual sign-off (ALT-AD08) |
| **S15** | F2 | Vendors & Partners + consoles | UPS §11; AOC §7; BRD §9.9/§18.9 | Tiered vendors; safety-ranked recommendations (never overridable for regulated inputs); vendor console live |
| **S16** | F2 | Phase 2 exit — marketplace ops, land/cert oversight (land), legal | BRD §4.3 (Phase 2); AOC §12 (land portion) | **Phase 2 exit**: zero oversell; escrow dispute exercised once; ≥1 `FieldOverlapEvent` resolved end-to-end; §7.2/§7.4 findings filed |
| **S17** | F3 | Pest & Disease Outbreak | UPS §8; BRD §9.6/§18.6 | Zero High/Critical alerts without corroboration gate (§8.4); anti-abuse thresholds |
| **S18** | F3 | Knowledge Bank + content governance | UPS §9; AOC §11; BRD §9.7/§18.7 | Zero chemical content reaches Published without agronomist authorship + review clearance; quick-info icon contract shipped |
| **S19** | F3 | Certifications + land/cert oversight | UPS §10; AOC §12 (cert portion); BRD §9.8/§18.8 | Per-type verification re-confirmed; badge = computed-not-stored within `Field`/`CropCycle` scope; zero out-of-scope badge renders |
| **S20** | F3 | Full Review Ops + Trust & Safety | AOC §6/§13; BRD §10.4/§10.11 | Reviewer roster with staffing plan executed; SLA dashboard live; unified risk feed + suspend/ban cascade; QA sampling rate confirmed |
| **S21** | F3 | Compliance/NDPR + config/flags + analytics completion | AOC §16/§15/§14.2; BRD §10.14 | DSAR workflow (post NDPA erasure sign-off); legal hold; **Phase 3 exit** incl. reviewer capacity + corroboration record (Requires Clarification re season window) |
| **S22** | HL | Performance, offline, accessibility, design conformance | BRD §11; Design System 2; Docs 12/14 | Load+perf budgets met; offline sync matrix green; accessibility conformance (WCAG) conformance report |
| **S23** | HL | Security & compliance hardening; seed verification | ENG 04; AOC §3; BRD §7 | Threat-model tests pass; SSO/MFA; full permission-matrix seed verified via role-based smoke suite; DR drill complete |
| **S24** | HL | Launch prep & go-live | BRD §3, §22; Doc 13; production-readiness checklist (§18 here) | UAT/pilot sign-off; runbooks + training; go/no-go; **launch** |

## 9. Sprint-by-Sprint Tasks

> Each task is a work package; acceptance is the BRD §18 section cited in the table above (cross-referenced in §10). Effort split is indicative.

**S1 — Foundations I**
- Provision dev/test/staging/prod + IaC; pipeline with gates (ENG 07); baseline monitoring/alerting + dashboards (ENG 08); DR target & backups (ENG 09).
- Append-only audit service; `Idempotency-Key` middleware; concurrency control (pessimistic where required, optimistic elsewhere) (UPS §2.6/§2.7).
- Canonical Geography model + Nigeria States/LGAs/Wards/Community seed (UPS §2.2); rule-table / config service (UPS §2.9); polymorphic-ref helpers (UPS §2.10).
- Nightly reconciliation job harness with failure alerting (UPS §2.8).

**S2 — Foundations II**
- Single `Identity` + `RoleProfile` + `Organization` (UPS §2.1); `TrustProfile` tiers (UPS §2.3).
- Review engine: `ReviewCategory` (11 rows + SLA), `ReviewQueueItem` state machine, reviewer roster/qualification, COI exclusion, escalation, QA sampling, appeals (UPS §2.4).
- Notification service + template model + dispatch audit + dedup; shared `IvrConfirmationFlow` incl. split-PIN delivery (UPS §2.5).
- Unified `Field` entity shape (UPS §2.11) as a schema baseline (with weather eye on the intercropping open decision, §13).

**S3 — Admin basement**
- RBAC engine: roles, scopes (Global/Region/Org), precedence rules, enforcement at action (AOC §1–§3); 12-role seed; `SuperAdmin` override + reason audit.
- Identity & Organization Trust console (search, verification queues, fob/tablet lifecycle, suspension cascade) (AOC §5).
- Admin auth: device step-up, least privilege, break-glass with 48h review escalation (AOC §17); unified audit search (AOC §16.1).
- Phase 0 exit-criteria assertion tests; assemble the NDPA/NDPC legal-review pack (BRD §7.1/§7.6).

**S4 — Registration & Identity**
- NIN verification via Prembly (reuse PayGuard's identity pattern); phone/email OTP; liveness **[DEFAULT — confirm]** (UPS §3.1).
- Low-literate path: fob issuance + agent-assist UI + VoiceOTP confirmation (UPS §3.2/§3.3); tablet binding (UPS §3.4).
- Organizations: types, CAC registration verification vs `ORG_VERIFICATION` review path (UPS §3.5); alerts §3.6; suspension/unlock console actions.

**S5 — Field Boundary**
- GPS capture flow + walk-track UX + filters (UPS §4.2–§4.3); geodesic area via PostGIS `geography` (UPS §4.12); raw-vs-final geometry handling (UPS §4.15).
- Validation/repair + state machine (UPS §4.18/§4.22); offline-first idempotent API (UPS §4.16); out-of-scope features guarded (interior rings reserved in schema, UPS §4.24).

**S6 — Crop Lifecycle I**
- Core entities + `CropProfile` model with per-stage expectations (UPS §5.1/§5.2); stage state machine + transitions (UPS §5.3); operation task catalog seed (UPS §5.4).

**S7 — Crop Lifecycle II**
- Validation rule engine (UPS §5.5); `OperationLog` flag + `OverrideReason` audit (UPS §5.5/§5.6); alerts; offline conflict strategy fixed (uses §2.7 patterns); mandatory-photo scope decision [open — §13]; stage-gate override permission granted to the role that owns it [open — §13].

**S8 — Accounts**
- Ledger with double-entry enforcement (never unbalanced — OBJ-12); Chart of Accounts + posting rules (UPS §13.1/§13.2); fixed assets & depreciation (UPS §13.3); period close (UPS §13.4); computed reports washed daily (UPS §13.5/§2.8); alerts §13.6; CoA as a governed table (AOC §4 precedence: dual sign-off before edits apply).

**S9 — Pilot admin surfaces**
- Master Data Governance console: governed tables, Draft→Review→Applied, dual sign-off, version history, `ALT-AD01`/`ALT-AD06` enforcement (AOC §4).
- Field Operations & Extension console: agents/tablets/fobs, org performance (AOC §9); Analytics baseline dashboard (AOC §14); permission-matrix subset seeded for pilot roles.

**S10 — Pilot integration & exit**
- End-to-end farmer walkthrough (register → field → crop cycle → bookkeeping) automated as acceptance tests (BRD §18.0/18.1–3/18.11/18.12).
- Verify `AdminRoleAssignment` scoping at `Region` for the pilot state specifically; instrument §3 success metrics; **Phase 1 go/no-go**, then pilot launch and CBN/PayGuard decision dossier (BRD §7.2) handed to legal.

**S11 — Land History**
- `FieldTenancy` + verification approach [open — §13]; timeline computed from source records only (UPS §6.1/§6.2/§6.4); overlap detection + resolution flow (UPS §6.3).
- Privacy/access controls (UPS §6.5); export artifact with mandatory Land-Use-Act disclaimer + counsel review (BRD §7.4); Land & Certification Oversight console land-overlap portion (AOC §12); alerts (UPS §6.6).

**S12 — Soil Advice & Test Labs**
- Sample request lifecycle + sampling protocol (UPS §7.1/§7.2); proposed/recommended crops (UPS §7.3/§7.6); GPS-enforced collection — server rejects outside `ArrivalRadiusMeters` (UPS §7.5).
- Soil adjustment prerequisite gate (UPS §7.7); lab registration/accreditation + integration (UPS §7.8/§7.9); lab/lab-member console surfaces; alerts; seed labs + soil-test catalog (UPS appendices).

**S13 — Marketplace core**
- Listings, offers/RFQ, buyer requests + pooling, `Listing.AskingPrice` hybrid (UPS §12.1–§12.3); reservation concurrency with zero oversell (UPS §12.2.3); audit trail (UPS §12.5); Marketplace Ops console basics (AOC §8).

**S14 — Marketplace escrow + dispute ops**
- **Gate check**: CBN posture (BRD §7.2) + PayGuard production readiness (UPS §12.0). If passed: PayGuard payment links, `EscrowStatus` lifecycle, split-PIN/IVR release, 14-day dispute flow (UPS §12.4). If not: ship §12.4.4 contact-only fallback (identical reservation logic — no re-architecture). Disputes require MarketplaceOps + Finance dual sign-off (ALT-AD08); Financial Admin escrow extension (AOC §10).

**S15 — Vendors & Partners**
- Vendor identity + category mechanics (UPS §11.1/§11.2); tiered verification by category (UPS §11.3); recommendations safety-ranked, **never** overridable for chemical/fertilizer/seed (UPS §11.4); anti-abuse (UPS §11.5); Vendor & Partner console (AOC §7); alerts.

**S16 — Phase 2 exit**
- Oversell regression suite; escrow dispute dry-run; ≥1 `FieldOverlapEvent` resolved end-to-end in console; Land-history export disclaimer legally signed; FCCPC review if vendor volume material; **Phase 2 go/no-go**.

**S17 — Pest & Disease**
- Report submission + entities (UPS §8.1/§8.2); analysis pipeline incl. `AgronomistReview` (UPS §8.3/§2.4); regional aggregation + anti-panic corroboration gate (UPS §8.4); alert tiers + re-send suppression (UPS §8.5); anti-abuse (UPS §8.7).

**S18 — Knowledge Bank**
- Content model + search + forum (UPS §9.1–§9.3); quick-info-anywhere contract (UPS §9.4); chemical/fertilizer safety governance — `KB_CHEMICAL_PUBLISH`/`KB_CHEMICAL_FLAGGED` gates, agronomist authorship (UPS §9.5); moderation + anti-abuse (UPS §9.7); Content & Knowledge Governance console (AOC §11). Content sourcing decision (curated vs licensed registry) resolved before mass-authoring [open — §13].

**S19 — Certifications**
- Types & scope incl. shipment-scoped (non-standing) NAQS badge (UPS §10.1); per-type verification — re-confirm registry APIs (GlobalGAP portal vs manual) before hardcoding (UPS §10.2); lifecycle + fraud prevention (UPS §10.3/§10.4); review routing (`CERTIFICATION_VERIFY`/`CERTIFICATION_FRAUD_FLAG`) (UPS §10.5); computed badge display (UPS §10.6); Land & Certification Oversight console cert portion (AOC §12).

**S20 — Full Review Ops + Trust & Safety**
- Reviewer roster, SLA dashboard, QA sampling rate (confirm default 5%), appeals escalation, category health (AOC §6); **staffing plan executed** (BRD §4.3 entry gate; `ALT-RV02` launch-blocking).
- Trust & Safety console: unified risk signal feed, suspend/ban cascade, investigation case files (AOC §13); cross-domain fraud alert tuning (ALT-AD01, ALT-AD07).

**S21 — Compliance/NDPR + Phase 3 exit**
- DSAR workflow + legal hold + audit export (AOC §16) — **after** NDPA erasure-mechanism sign-off (BRD §7.1); System Config & Feature Flags completion (AOC §15); remaining Analytics (AOC §14); ast Phase 3 exit-criteria report incl. reviewer capacity + zero-ungated-alert record (Requires Clarification re the one-season observation window).

**S22 — Performance, offline, accessibility, design conformance**
- Load/perf against BRD §11.1 + Design System 2 budgets; offline sync + conflict matrix (Doc 12 concurrency/offline categories — ensures specified before execution); accessibility conformance per Doc 14; design-token + concentric-arc-progress conformance sweep across both experience modes.

**S23 — Security & compliance hardening**
- Threat-model test execution (ENG 04); SSO/MFA + admin step-up; full permission-matrix seed verified via role-based smoke suite (AOC §3); DR drill with RPO/RTO verification (ENG 09); TO BE audited alert registry triage (UPS §14/AOC §18).

**S24 — Launch prep & go-live**
- UAT/pilot protocol execution + sign-off (Doc 13); runbooks, training, rollout/comms plan; §3 launch-success baseline captured at go-live; legal sign-off pack (BRD §7.6/§22); production-readiness checklist (§18 here) fully ticked; **go/no-go** and launch.

## 10. Acceptance Criteria

Acceptance for every sprint is the **corresponding BRD §18 acceptance-criteria family**, which this plan does not re-derive but faults if unmet:

| Sprint group | Acceptance criteria applied |
|---|---|
| S1–S3 | BRD §18.0 (cross-cutting) + Phase 0 exit criteria (§4.3) + AOC §4.2 master-data AC + AOC §20 (admin AC pulled in as they become operative) |
| S4 | BRD §18.1 (Registration & Identity) |
| S5 | BRD §18.2 (Field Boundary) |
| S6–S7 | BRD §18.3 (Crop Production) |
| S8 | BRD §18.11 (Accounts) |
| S9 | BRD §18.12 (Admin console, pilot subset) + AOC §4.2 |
| S10 | BRD §18.0 + §18.1–3 + §18.11–12 consolidated; Phase 1 exit criteria; §3 launch metrics |
| S11 | BRD §18.4 (Land History) |
| S12 | BRD §18.5 (Soil & Labs) + OBJ-06 style server-side rejection check |
| S13–S14 | BRD §18.10 (Marketplace) + OBJ-11 zero-oversell; AOC §8/§10 AC |
| S15 | BRD §18.9 (Vendors) + AOC §7 AC |
| S16 | Phase 2 exit criteria (§4.3) |
| S17 | BRD §18.6 (Pest & Disease) + OBJ-07 zero-ungated-alert |
| S18 | BRD §18.7 (Knowledge Bank) + OBJ-08 chemical-publish gate |
| S19 | BRD §18.8 (Certifications) + OBJ-09 badge scope |
| S20–S21 | BRD §18.12 (remaining admin) + AOC §20; Phase 3 exit criteria |
| S22–S24 | BRD §11 NFR acceptance + Doc 12/13/14 acceptance + §3 launch-success baseline + BRD §22 sign-off |

Phase exit criteria from BRD §4.3 are the contractual definition of "phase complete"; sprint acceptance criteria are the leading indicator. Every acceptance run is recorded against the RTM (§13) with the requirement IDs exercised.

## 11. Testing Strategy

Layered execution across all 24 sprints, with phase-level test activities:

1. **Unit & contract tests** — per module; the API surface in ENG 03 is the contract. Idempotency-key and concurrency behaviors tested at the contract layer (UPS §2.7).
2. **Data-integrity tests** — double-entry: 100% of `JournalEntry` writes balance at creation (OBJ-12); marketplace: zero negative `RemainingQuantity` under concurrent offer storms (OBJ-11); append-only audit invariants.
3. **Geodesic correctness audit** — sample boundary areas vs independent survey within ±5% (OBJ-03); regression guards against naive planar path (UPS §4.12).
4. **Offline & sync tests** — field capture and `OperationLog` writes under flaky/no connectivity, conflict resolution against §2.7, plus the Doc 12 *under-specified* offline-disconnect category concretized here (*Requires Clarification* on thresholds).
5. **Review-engine tests** — SLA resolution (≥90% within `SlaHours`, OBJ/§3), COI exclusion, QA sampling, appeals routing, `ALT-AD06`/`ALT-AD08` unreachability-by-bug assertions.
6. **Security tests** — threat-model-driven (ENG 04): RBAC enforcement at action level (AOC §1.1), override-reason audit, break-glass 48h escalation, ADMIN device step-up, NIN/PII at-rest encryption.
7. **Performance & resilience** — load vs BRD §11.1 + Design System 2 budgets; DR drills at RPO/RTO (ENG 09); nightly reconciliation failure alerts exercised (UPS §2.8).
8. **UAT & pilot** — Doc 13 protocol executed in the pilot region (S10/S16/S21 exits).
9. **Accessibility conformance** — Doc 14, across both experience modes (admin console often forgotten); design tokens checked programmatically.
10. **Role-based smoke suite** — each admin role authenticated executes its permission matrix slice; mutually-exclusive grants (Auditor) asserted (AOC §3).

Doc 12 flags **concurrency-race**, **offline-disconnect**, and **constraint-violation** test categories as under-specified; this plan promotes them to first-class test activities (2, 4) and records them in §13 as required clarifications.

## 12. Technical Risks & Mitigations

| ID | Risk (source) | Impact | Likelihood | Mitigation | Gate |
|---|---|---|---|---|---|
| R-01 | **CBN escrow licensing** unresolved (BRD §7.2 — highest-consequence gap) | Marketplace escrow halted post-launch; regulatory exposure | High | Legal engagement with PayGuard compliance; §12.4.4 contact-only fallback as safe default; decision dossier prepared in S10 | Before S14 |
| R-02 | PayGuard production readiness / external dependency (UPS §12.0, §12.4.4) | Escrow mode slips | Medium | Fallback path spec'd to identical reservation logic; integration contract + mock in S13 | S14 |
| R-03 | Phase 3 reviewer staffing (BRD §4.3 note; `ALT-RV02` launch-blocking) | Full governance domains cannot go live safely | High | Staffing estimate per category vs projected volume as entry gate; recruiting starts Phase 2 | Before S17 |
| R-04 | Identity vendor reliance (Prembly) + liveness open decision (UPS §3.1) | Registration velocity/trust bar drift | Medium | Reuse PayGuard's proven identity pattern; single-vendor fallback contract; liveness decision logged by S4 | S4 |
| R-05 | Offline-first sync conflicts (UPS §5.8/§4.16; Doc 12 gap) | Crop/field data corruption at scale | Medium | §2.7 idempotency + last-writer-with-flag strategy; conflict tests in S5/S7 | S5/S7 |
| R-06 | Geodesic correctness regression (UPS §4.12) | Wrong land/area data on something meant for loans/leases | Medium | PostGIS `geography` only; ±5% audit; planar path banned in code review | S5 |
| R-07 | Land-history export misused as title (BRD §7.4; Land Use Act 1978) | Financial harm to farmers; liability | Medium | Mandatory disclaimer + counsel review before export ships | S11 |
| R-08 | Derived-data drift (UPS §2.8) | Trust tiers/badges/reports diverge from sources | Medium | Computed-not-stored enforced; nightly reconciliation with failure alerting | S1+ |
| R-09 | Ledger integrity (UPS §13.1; OBJ-12) | P1: unbalanced entries | Low/Med | Double-entry enforced at write; balanced-at-creation invariant tests; reversing-only edits | S8 |
| R-10 | Oversell/negative quantity (UPS §12.2.3; OBJ-11) | P1 marketplace incident | Medium | Concurrency-safe reservation (locks/idempotency); stress suite in S13 | S13 |
| R-11 | Registry facts drift (GlobalGAP/NAFDAC/NASC/NAQS APIs, UPS §10.2) | Hardcoded verification breaks | Medium | Re-confirm before hardcoding; table-driven verification profiles (config-driven, §2.9) | S19 prep in S16 |
| R-12 | Seed-data governance gaps (AOC §4.0/§3) | Unreviewable mutations / permission drift | Medium | Master Data Governance dual sign-off enforced before writable; permission-matrix smoke suite S23 | S9/S23 |
| R-13 | NDPA data residency & erasure tension (BRD §7.1, AOC §16.2) | Compliance/P1 exposure | High | Phase 0 data-handling sign-off; erasure=anonymization assumption requires legal sign-off before S21 | S3/S21 |
| R-14 | Scope creep beyond pruned items (BRD §4.2) | Timeline/effort overrun | Medium | Exclusions enforced (off-taker entity, separate financial stores, USSD, TTS) unless written decision reverses them | Continuous |
| R-15 | Design-system conformance cost (both experience modes, v2) | Inconsistent UX / integration delay | Medium | Tokens + arc-progress as implementation-time DoD, verified programmatically in S22 | S22 |
| R-16 | Intercropping & data-model decisions unresolved before schema freeze (UPS §5.8, §15) | Migration cost | Medium | Decide before S5; reserve interior-rings (UPS §4.24); `[DEFAULT — confirm]` values logged in §13 | Before S5 |

## 13. Documentation Gaps & Open Questions

Every item is *Requires Clarification*; none should be silently defaulted by the implementing team. Grouped by register.

| # | Item | Source | Decision needed before | Note |
|---|---|---|---|---|
| Q-01 | **CBN escrow-licensing posture** | BRD §7.2 | S14 (S10 dossier) | Highest-consequence decision; drives escrow vs §12.4.4 fallback |
| Q-02 | **PayGuard production readiness / which payment mode ships first** | UPS §12.0, §12.4.4, §15 | S14 | Fallback keeps reservation logic identical |
| Q-03 | **Review-category count: prose "ten" vs table eleven** | UPS §2.4 | S2 (seed) | Plan assumes the 11-row table |
| Q-04 | **Team capacity & sprint effort estimates** | — | S1 | All durations indicative; no sizing in doc set |
| Q-05 | Liveness capture for farmers/agents — launch vs fast-follow | UPS §3.1, §15 | S4 | Recommended; ties to consistent trust bar |
| Q-06 | Fob hardware: NFC vs RFID | UPS §15 | S4 procurement | Affects device lifecycle tests |
| Q-07 | Intercropping (multi-CropCycle per Field) | UPS §5.8, §15 | Before S5 | Data-model freeze |
| Q-08 | Mandatory photo evidence — which crop tasks | UPS §5.8, §15 | S7 | Certification/loan use cases |
| Q-09 | Lan History overlap noise threshold **[DEFAULT: 3%]**; fallow **[DEFAULT: 24 months]** | UPS §6.9, §15 | S11 | Confirm against real usage data |
| Q-10 | Soil GPS enforcement params **[DEFAULT: 5m / min 3 points]**; composite vs per-point; fee/subsidy; SelfDeclared labs at launch; re-review [12mo] | UPS §7.13, §15 | S12 | Server-side rejection semantics depend on radii/floor |
| Q-11 | Pest & Disease: region granularity [LGA, 25km fallback], corroboration [3 farms/48h], false-report threshold [40%, ≥5] | UPS §8.10, §15 | S17 | Anti-panic gate parameters |
| Q-12 | KB chemical/fertilizer sourcing: curated vs licensed registry; language scope; forum promotion threshold [20 upvotes] | UPS §9.11, §15 | S18 (research S16) | Combined effort with Q-14 registry work |
| Q-13 | Certification registry facts re-confirm (GlobalGAP post-migration API; NAQS shipment-scoped; others manual) | UPS §10.2/§10.10, §15 | S19 | Table-driven profiles; research in Phase 2 |
| Q-14 | Marketplace defaults: commission [2%], Verified-cap [₦2,000,000], dispute timeout [14 days] | UPS §12.10, §15 | S13 | Config-driven seeds |
| Q-15 | Accounts: income recognition (escrow release vs accrual), reporting scope, period definition, disposal scope, NGN-only | UPS §13.9, §15 | S8 | Posting-rule design |
| Q-16 | TrustProfile reliability thresholds for Verified→Trusted per role (Vendor/Farmer undefined — only Buyer: ≥5 txn) | UPS §2.3, §15 | S2/S15 | Asymmetry must be closed |
| Q-17 | Review QA sampling rate [DEFAULT 5%] — higher at launch? | UPS §2.4, §15 | S20 | Reviewer calibration unproven |
| Q-18 | PlatformReviewer internal-only vs vetted external experts | UPS §15 | S3 staffing plan | Phase 3 entry gate input |
| Q-19 | BRD §19 open decisions (product-level trusts not listed above) | BRD §19 | Per-phase | Cross-check §15 register when sprint opens |
| Q-20 | Part B §21 open decisions (admin-side, e.g. <>console-scope nuances) | AOC §21 | Sprout-time | Reviewed per console sprint |
| Q-21 | Doc 12 under-specified test categories (concurrency races, offline disconnects, constraint violations) | Doc 12 | S1 (test plan), S5/S7 execution | Concretized in §11 |
| Q-22 | PRD volumes vs Part A/B consistency spot-check result | §2 here | Each sprint DoD | None found to affect ordering |
| Q-23 | Legal: NDPA data residency; NDPR-erasure-as-anonymization sign-off; FCCPC; branch of land-export counsel | BRD §7 | S3 / S21 / S16 / S16 | §7.6 sign-off pack |
| Q-24 | Reviewer staff headcount estimate (entry gate) | BRD §4.3 note | Before S17 | Launch-blocking |

## 14. Parallelization Opportunities

Capable of running in parallel (subject to admitted staffing — *Requires Clarification*):

1. **Legal & compliance workstream** — from S3 onward, runs off the critical path: NDPA design review, CBN dossier (Q-01), land-export counsel (Q-23), FCCPC. Legal produces sign-off gates; engineering never blocks on legal review.
2. **Phase 2 dual streams** — after Phase 1 exits (S16 vs S11–S12 split): Stream A = Land History + Soil/Labs (S11–S12); Stream B = Marketplace + Vendors (S13–S15) — parallelizable under two teams because they share only the foundation and the GoS-data reads.
3. **Content sourcing & registry research** — Knowledge Bank content strategy (Q-12) and certification-registry verification profiles (Q-13) are research tasks that can run during Phase 2, in parallel with marketplace work, de-risking S18–S19.
4. **Seed-data authoring** — Geography, labs, soil-test catalog, `CropProfile`, `ReviewCategory`, full permission matrix: pure authoring work, startable in Phase 0, independent of app code, gated by the Master Data Governance console when it lands (S9).
5. **QA automation framework + design-system tooling** — established once in S1 (perf/load harness, contract-test scaffold, design-token linting) and reused every sprint thereafter; amortizes across all 24 sprints.
6. **Observability/DR org debt** — dashboards, alert routing, backup verification can each be advanced incrementally in parallel with feature sprints, rather than as a single hardening burst.

**Not parallelizable (hard precedence):** Identity foundation before any domain (S2→S4); RBAC basement before admin consoles (S3→S9+); review engine before review-dependent domains (S2→S17/§2.4); validation engine before task-catalog automation (S6→S7); Account posting rules before period-close tooling (S8→S9); marketplace reservations before escrow (S13→S14); reviewer staffing before Phase 3 go-live (S16→S17).

## 15. Critical Path

**Critical chain:**
```
S1 scaffolding/geo/audit → S2 identity/trust/review/notifications → S3 RBAC + console basement
 → S4 Registration → S5 Field Boundary → S6–S7 Crop Lifecycle → S8 Accounts → S9 Master-Data/Field-Ops consoles
 → S10 Pilot exit (Phase 1 live) → S13 Marketplace core → S14 [CBN/PayGuard gate] Escrow/fallback
 → S16 Phase 2 exit → S17 Pest & Disease → S18 Knowledge Bank → S19 Certifications
 → S20 Review Ops/T&S staffing → S21 Phase 3 exit → S22–S23 Hardening → S24 Launch
```

**Parallel off-chain but schedule-affecting:** S11 (Land History) and S12 (Soil & Labs) complete before Phase 2 exit (S16) but are not on the marketing critical chain; S15 (Vendors) rides the crop-lifecycle recommendations dependency.

**The four hard gates** (each can stop the critical path): Phase 0 exit (end S3) → Phase 1 pilot live (end S10) → **CBN escrow + PayGuard readiness** (before S14) → Phase 3 reviewer staffing (before S17) — plus the legal gates in BRD §7.6 (NDPA sign-off at S3, land-export at S16, NDPA-erasure at S21). **Any slippage on S1–S3 or S10 or S14's gate ripples to launch**; land/soil/support streams carry slack.

**Fast-track levers (do not cut product scope without a written decision):** merge S6+S7 into one sprint with a second stream (catalog vs engine); move Q-05 liveness to fast-follow; defer Q-08 photo scope to a later hardening sprint; pre-purchase research for Q-13 in Phase 0/1.

## 16. Requirement-to-Sprint Traceability

Indicative matrix; authoritative ID-level coverage is BRD §13 (RTM). Load-wise this plan asserts *family-to-sprint* mapping:

| Requirement family | Sprint(s) | Source |
|---|---|---|
| F-XC-* cross-cutting | S1–S3 | BRD §8 / UPS §2 |
| F-ID-* | S4 | BRD §9.1 / UPS §3 |
| F-FB-* | S5 | BRD §9.2 / UPS §4 |
| F-CY-* | S6–S7 | BRD §9.3 / UPS §5 |
| F-LH-* | S11 | BRD §9.4 / UPS §6 |
| F-SL-* | S12 | BRD §9.5 / UPS §7 |
| F-PD-* | S17 | BRD §9.6 / UPS §8 |
| F-KB-* | S18 | BRD §9.7 / UPS §9 |
| F-CT-* | S19 | BRD §9.8 / UPS §10 |
| F-VN-* | S15 | BRD §9.9 / UPS §11 |
| F-MP-* | S13–S14 | BRD §9.10 / UPS §12 |
| F-AC-* | S8 | BRD §9.11 / UPS §13 |
| A-RB-* | S3, S23 | BRD §10.1 / AOC §1–3 |
| A-MD-* | S9 | BRD §10.2 / AOC §4 |
| A-IT-* | S3, S4 | BRD §10.3 / AOC §5 |
| A-RV-* | S3, S20 | BRD §10.4 / AOC §6 |
| A-VP-* | S15 | BRD §10.5 / AOC §7 |
| A-MO-* | S13–S14 | BRD §10.6 / AOC §8 |
| A-FO-* | S9 | BRD §10.7 / AOC §9 |
| A-FN-* | S8, S14 | BRD §10.8 / AOC §10 |
| A-CG-* | S18–S19 | BRD §10.9 / AOC §11 |
| A-LC-* | S11, S19 | BRD §10.10 / AOC §12 |
| A-TS-* | S20 | BRD §10.11 / AOC §13 |
| A-AR-* | S9, S21 | BRD §10.12 / AOC §14 |
| A-CF-* | S3, S21 | BRD §10.13 / AOC §15 |
| A-CP-* | S3, S21 | BRD §10.14 / AOC §16 |
| A-SC-* | S3, S23 | BRD §10.15 / AOC §17 |
| NFR-* | S1 (baseline), S22–S24 (enforced) | BRD §11 |
| AC-* (acceptance) | same sprint as owning feature | BRD §18 |
| OBJ-01…14 | phase of owning domain; §3 success metrics at S10/S24 | BRD §2/§3 |
| ALT-* / ALT-AD* alert sets | same sprint as owning domain | BRD §20 / UPS §14 / AOC §18 |

**Exit-criteria traceability:** Phase 0 → S3; Phase 1 → S10; Phase 2 → S16; Phase 3 → S21; launch ($3 baseline) → S24. Each exit's explicit criteria (BRD §4.3) are asserted in the exit sprint's automated acceptance suite.

## 17. Definition of Done

A sprint or phase is **Done** when *all* of the following hold (each maps to a source):

1. **Acceptance criteria met** — every BRD §18 family for the sprint's features passes (cited in §10), recorded against BRD §13 RTM IDs. **Exit criteria** for a completed phase (BRD §4.3) are met and evidenced.
2. **Non-functional conformance** — BRD §11 NFRs measured for the increment; no target regression without a logged decision.
3. **Design conformance** — Design System 2 tokens, concentric-arc progress, and both experience-mode rules implemented; offline & performance budgets met at the sprint boundary (or a tracked debt item exists).
4. **Alerts & review wiring** — every new domain's alerts exist as rows in its registry and dispatch through `NotificationTemplate` (UPS §14/§2.5); any new review need is a `ReviewCategory` row (UPS §2.4), never bespoke code.
5. **Auditability** — append-only audit covers all mutating actions; overrides require reason and are audit-visible to `Auditor` (AOC §3 note).
6. **Data integrity** — invariants asserted in CI (balanced `JournalEntry`, non-negative `RemainingQuantity`, geodesic area, reconciliation job green).
7. **Governance** — governed-table edits flowed through Draft→Review→Applied with dual sign-off where required (AOC §4.0); `ALT-AD01/06/08` unreachable-by-construction verified.
8. **Tests** — unit, contract, and the domain's integrity/security tests green (Doc 12 scope); no open P0/P1 defects.
9. **Seed data** — the sprint's seed sets (geo, catalogs, roles/permissions, review categories) versioned and reviewable, not ad hoc.
10. **Legal gates** — the phase's BRD §7.6 sign-offs are current (no un-gated scope present).
11. **Documentation** — schema/API/threat-model/runbook updates (ENG 02/03/04/09) are raised with the code, not retrofitted.

## 18. Production Readiness Checklist

| Group | Checklist item | Source | When |
|---|---|---|---|
| Infra & DR | Environments parity + IaC reproducible | ENG 07 | S1, re-verified S24 |
| Infra & DR | RPO/RTO met; DR drill executed | ENG 09; BRD §11.5 | S23 |
| Infra & DR | Observability + dashboards + alert routing owned | ENG 08 | S1, S22 |
| Security | Threat-model tests passed (P1 seam NIN/escrow/ledger) | ENG 04 | S23 |
| Security | SSO/MFA + admin device step-up + break-glass exercised | AOC §17 | S23 |
| Security | Full permission-matrix seed verified role-by-role | AOC §3 | S23 |
| Data | All governed tables through dual sign-off; no uncovered mutations | AOC §4 | S9+, S24 |
| Data | Reconciliation jobs green; drift → alert proved | UPS §2.8 | S1+, S24 |
| Data | Seed data frozen + versioned (geo, labs, catalog, profiles, roles) | 06-DATA, UPS appendices | S24 |
| Operations | Runbooks (deploy, incident, escrow dispute, break-glass, DR) | ENG 08/09, §12 mitigations | S24 |
| Operations | Rotating alert-triage ownership for ALT-*/ALT-AD* | UPS §14/AOC §18 | S21+ |
| Legal | BRD §7.6 sign-offs signed for every phase shipped | BRD §7 | S3/S10/S16/S21 |
| Legal | ToS + Privacy finalized (DRAFT doc reconciled) | 11-LEGAL | S21–S24 |
| Marketplace | Escrow (or documented fallback) + dispute dual sign-off exercised | UPS §12.4, ALT-AD08 | S14/S16 |
| QA | Doc 13 UAT/pilot sign-off; Doc 12 test-case completion ≥ agreed coverage | Docs 12/13 | S24 |
| QA | Accessibility conformance report clean | Doc 14 | S22 |
| Launch | §3 launch-success baseline captured at go-live | BRD §3 | S24 |
| Launch | Go/no-go meeting with legal, engineering, finance, product | BRD §22 | S24 |

## 19. Overall Implementation Roadmap

Indicative calendar (2-week sprints; real dates *Requires Clarification*).

| Phase | Sprints | Milestone | Key exit |
|---|---|---|---|
| Phase 0 — Foundation | S1–S3 (~6 wks) | Foundation + admin basement live | Identity/trust/review/notifications + RBAC verified; NDPA sign-off |
| Phase 1 — Pilot | S4–S10 (~14 wks) | **Pilot live** (single state/LGA cluster) | §3 launch metrics; zero unbalanced entries; Region-scope RBAC proved |
| Phase 2 — Market Linkage & Provenance | S11–S16 (~12 wks*) | Marketplace + soil + land live | Zero oversell; escrow/fbllback exercised; CBN + land-export sign-offs |
| Phase 3 — Trust, Safety & Governance | S17–S21 (~10 wks*) | Full platform + consoles live | Reviewer staffing; zero ungated High/Critical alerts; NDPA-erasure sign-off |
| Hardening & Launch | S22–S24 (~6 wks) | General availability | Production-readiness checklist; go/no-go; launch |

\* Phase 2/3 windows assume two delivery streams under capacity; single-stream stretches them (*Requires Clarification*). Total indicative: **~48 weeks (~11 months)**. Fast-track options without scope cuts are listed in §15.

**Post-launch horizon (not scheduled here — tracked per §13):** organic-transition certification tracking, USSD/feature-phone channel, multi-language/audio delivery, interior-rings field geometry, intercropping (if [re]opened), marketplace GMV sizing against Phase 1 data (BRD §3 note).

## 20. Final Coverage Audit

Conformance of this plan against the entire document set:

| Claim | Verified by |
|---|---|
| Every BRD §4.1 in-scope capability has a delivery sprint | §4 inventory ↔ §8 table: all 11 platform families + 15 admin families mapped |
| Every UPS domain section (§3–§13) has a sprint | §4/§5 mapping (all 13 domains) |
| Every AOC console (§4–§16) has a sprint | §4/§5: MasterData S9, IdentityTrust S3, Review Ops S3/S20, Vendor S15, Marketplace S13–S14, Field Ops S9, Finance S8/S14, ContentGov S18, Land+Cert S11/S19, Trust&Safety S20, Analytics S9/S21, Config S3/S21, Compliance S3/S21 |
| Every phase exit criterion from BRD §4.3 has an exit sprint | §16 exit-traceability (S3/S10/S16/S21) |
| Every review category enumerated in UPS §2.4 is staffable & routed | S2 seed + S20 staffing; §11 tests locate the gate |
| Every alert registry (BRD §20, UPS §14, AOC §18) wired | §17 DoD item 4; per-domain alert rows |
| All 12 admin roles + permission matrix seeded & tested | S3 seed, S23 smoke suite |
| Non-functional, QA, accessibility, DR, design-system, legal requirements have execution windows | §19/§18 / S22–S24 / §7.6 gates |
| MRP critical dependencies (PayGuard, Prembly, registry APIs, geo seed) are explicit | §6/§12 |
| All `[DEFAULT — confirm]` and open decisions are surfaced, not silently defaulted | §13 registers (Q-01…Q-24) |

**Coverage holes deliberately left open (all *Requires Clarification* — none are engineering blockers on their own):**
- **Capacity & cost sizing** for people, budget, and dates (no authority in the doc set).
- **Reviewer headcount** commitment before Phase 3 (entry gate, Q-24).
- **One-season observation window** for the Phase 3 "zero ungated High/Critical alerts" evidence (longer than a sprint cadence — needs a program-level decision).
- **Marketing/GTM, hiring, and vendor contracts** for PayGuard/Prembly/IVR carriers (operational, not application-build, work the doc set delegates outside its scope).

---

*Authoring note: all section citations use the numbering of the source documents as-of revision dates listed in §2. This plan is a living document; every decision logged in §13 updates the affected sprint and this coverage audit.*