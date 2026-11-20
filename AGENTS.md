# Farm Basics

Farm Basics is a product blueprint (specification set + implementation plan) and public landing page for a single-digital-identity platform for Nigerian smallholder farms. Implementation stack per `Doc/01A-ENG-Technology-Stack-and-Tooling-Plan.md`: Flutter (farmer/buyer/vendor apps), React 19 (admin console), ASP.NET Core + PostgreSQL/PostGIS (backend), single VM + Docker Compose + Caddy behind a Cloudflare free-tier edge.

## Commands

| What | Command |
|---|---|
| Rebuild plan HTML from Markdown | `powershell -NoProfile -ExecutionPolicy Bypass -File Plan/regenerate.ps1` — needs `pandoc` on `PATH` or `$env:PANDOC` set; uses `Plan/tools/build_page.py` + `Plan/assets/farm-basics.css` |
| Landing page | static, no build step (`site/`) |
| Commit spec changes | `git -C Doc add -A; git -C Doc commit` — and likewise for `Archived/` and `Plan/sessions/`. Three independent local-only repos, no remotes; see Conventions |
| Archive specs offsite | `powershell -NoProfile -ExecutionPolicy Bypass -File Plan/tools/archive-specs.ps1` — verifies and encrypts `git bundle` archives of all three local-only repos with `age`; needs the identity at `%LOCALAPPDATA%\Farm-Basics\archive-identity.txt` (see Conventions) |
| Tests/lint/build | none yet — apps not scaffolded; see Doc 01A §11 execution plan |
| Environment | Windows host, Windows PowerShell 5.1 only (`pwsh`/PowerShell 7 is **not** installed); `Plan/regenerate.ps1` resolves Python at run time (Anaconda was removed 2026-10-01, so no anaconda path is assumed); `pandoc` (installed to `%LOCALAPPDATA%\Pandoc`) and `age` (winget `FiloSottile.age`) are both off `PATH` until a new shell opens — `archive-specs.ps1` resolves `age` itself as a fallback |

## Project structure

- `Plan/` — implementation programme + recommended skills; `Farm-Basics-Implementation-Sprints.html/.md` is the task breakdown; `Plan/progress/PROGRESS.md` holds the mandatory tabular implementation progress log; `Plan/sessions/` (**its own local-only Git repo, no remote**) holds `Sprint-Journal.md` and `Followups-Ledger.md`
- `Doc/` (flat) — specification set: BRD, PRD, engineering (01–10, 19), legal (10–11), QA (12–14), design (Design System 2), ops (15–18, 20), plus `Doc/00A-INDEX-Documentation-Map-Addendum.md` (authoritative inventory), `Doc/PRECEDENCE.md`, `Doc/DECISIONS.md`, and `Doc/AGENTS.md` (Document 19 Appendix A hard engineering rules — read alongside this file; never overwrite one with the other). **Its own local-only Git repo, no remote**
- `Archived/` — superseded v1-era specs, cross-reference only. **Its own local-only Git repo, no remote**
- `site/` — static Netlify landing page
- `infra/`, `apps/` — planned (Doc 01A §11 execution plan), not yet created

## Skill map — load before acting (mandatory per global AGENTS.md)

| Task domain | Skills to load |
|---|---|
| Flutter app work | `flutter-*`, `dart-*` skills (state, routing, localization, widget tests, JSON serialization, analyze/format) |
| Admin/React work | `frontend-design`, `design-taste-frontend`, `webapp-testing` (Playwright) |
| Backend/.NET/PostGIS | `database-schema-designer`, `migration-architect`, `api-design-reviewer`, `ci-cd-pipeline-builder`, `observability-designer`, `runbook-generator`, `dependency-auditor` (installed 2026-10-01, `Recommended-Skills.md` §8) plus `dev-*` + `tdd`; follow the 02-ENG/03-ENG engineering specs |
| App QA / emulator | `agent-device`, `flutter-skill`, `e2e-testing` |
| Planning/execution | `dev-plan`, `dev-implement`, `dev-review`, `dev-verify`, `issue`, `todo`, `git` |
| Security audit | `owasp-mobile-security-checker` (Flutter/mobile) |
| Documents | `docx`, `pdf`, `xlsx`, `pptx` when producing those artifacts |
| Design system / brand | `canvas-design`, `brandkit`, `theme-factory` for branded deliverables |

## Conventions & gotchas

- Docs: keep doc-header convention (Version, Status, Document Sequence, Depends On, Source of Truth, Audience) used across `Doc/`.
- Design System 2 tokens are the source of truth for any UI work — read `Doc/Farm-Basics-Design-System-2.md` and `Doc/farm-basics-styleguide-2.html`. The v1 pair (`farm-basics-design-system.md`, `farm-basics-styleguide.html`) is **superseded**; never implement from it. Note v2 renumbered sections vs v1 (Accessibility §12→§19, Content & Voice §13→§20, Platform Guidance §14→§21, Tokens §15→§23, Screen Patterns §16→§24, Governance §17→§27).
- `Plan/Recommended-Skills.md` is Flutter-consistent (refreshed 2026-09-24 per Doc 01A §11 item 7 — its §5/§6 were the last native-Android holdouts) and was updated again 2026-10-01 with §8 recording seven backend skills installed from `alirezarezvani/claude-skills` and the `chisel` removal. Its header, not `skills/skills-lock.json`, is the authority on the current global inventory — the lock predates those changes (F-11).
- Before installing any skill from a third-party repo, run `skill-inspector` on it. `skillspector` (the SkillSpector CLI) is **not** installed here, so the 2026-10-01 batch was assessed by documented manual review, not a scanner run.
- Pending decisions are tracked authoritatively in `Doc/PRECEDENCE.md` §3 (`P1`–`P25`) and `Doc/DECISIONS.md` (alongside `Doc/01A-ENG` §12) — implement the stated safe build posture and don't design around open items. Note: break-glass custody is now locked (`D-18` / `04A-ENG`) and PayGuard's 3-tier non-custodial commercial-bank escrow architecture is locked (`ADR-004` / `05A-ENG`), while the live commercial bank contract remains gated under `P9`.
- Doc 01A §14 lists five prior documents whose Kong-era statements were superseded. All five are now corrected in place (2026-09-28): `01-ENG` §15 (`Caddy` per D-08), `03-ENG` §1.8 (rate limiting → `Microsoft.AspNetCore.RateLimiting` per D-11), `04-ENG` (Caddy diagram + §9 threat table). Kong appears in `Doc/` only as historical narrative inside `01A` itself.
- Never invent commands; verify against the repo before quoting them.
- **`Doc/`, `Archived/`, and `Plan/sessions/` are separate local-only Git repos nested inside this one.** Each carries full version history and **no remote is configured** — a deliberate decision, so no third party (GitHub included) can be compelled or breached to disclose the specification set. This repo gitignores all three (`Doc/`, `Archived/`, and the existing `sessions/` rule), so `git add -A` here never touches them; commit their changes with `git -C Doc commit`, `git -C Archived commit`, and `git -C Plan/sessions commit`. All three set `.gitattributes` to `* -text`, so blobs are stored byte-exact with no line-ending normalisation — F-04 was a silent double-encoding incident, so archival fidelity outranks EOL tidiness. Offsite durability comes from `Plan/tools/archive-specs.ps1`, which bundles and encrypts all three with **age** (keypair at `%LOCALAPPDATA%\Farm-Basics\archive-identity.txt`, no remote), rather than a hosting provider: a private GitHub repo would still be reachable by legal compulsion, and the `Farm-Basics` org is on the free plan where GitHub **cannot** enforce 2FA. **Never run `git clean -ffdx` in this repo** — single-force `-fdx` skips nested repositories, but `-ffdx` destroys them (verified empirically). `skills/` remains gitignored with no history (F-11). Resolved from F-12.
- **UTF-8 on every read and write.** Never mix a default-encoded read with a UTF-8 write. An unqualified `Get-Content -Raw` followed by a `UTF8Encoding` `WriteAllText` re-encodes the file as CP1252 and silently double-encodes every non-ASCII byte — em dashes, `§`, curly quotes, `→` all become mojibake, and the corruption looks like a display glitch rather than a write. This already damaged `05-ENG` (66 em dashes, 93 section signs) and `06-DATA` once. Safe options: the `edit` tool, `Get-Content -Encoding UTF8`, or an explicit `UTF8Encoding` on *both* sides. After any bulk write, verify with `U+FFFD` count == 0. Tracked as `F-04`.
- **Sprint changes land in the sprint.** This is a solo build with no current aspiration to be a team. A change reflects immediately in `Plan/Farm-Basics-Implementation-Sprints.md` unless an `F-##` entry exists in `Plan/sessions/Followups-Ledger.md`, in which case work continues against that entry. There is no separate SDLC document — P0.1–P0.8 of the Implementation Sprints Preamble *is* the lifecycle, plus Sprint-Plan §17 (DoD) and §18 (production readiness). **Non-modification policy for `Doc/`:** never edit existing base specification documents in `Doc/` in place (`Doc/00A-INDEX` §2, `Doc/DECISIONS.md` `ADR-007`). Record amendments as lettered addenda aligned to the base document number (e.g. `A`, `B`, `C`, `D`) and register them append-only in `Doc/PRECEDENCE.md` and `Doc/DECISIONS.md`; any new architectural/legal/commercial decision still halts for clarification per P0.7.2. Review-approval rules are solo-adapted in `Doc/07-ENG-CI-CD-and-Environment-Strategy.md` §2 — CI plus declared self-review, not peer approval. The in-product dual sign-off (`MarketplaceOperationsAdmin` + `Finance`, BR-C-06, ALT-AD08) is a schema-enforced role requirement and is unaffected by solo operation.

## Mandatory Progress Logging

For every implementation task, you must maintain a structured tabular log in `Plan/progress/PROGRESS.md`. Do not skip these steps.

### 1. Pre-Implementation Log (Before touching code)

After analyzing and understanding the request—and **before** creating, editing, or deleting any project files—append a new entry to `Plan/progress/PROGRESS.md` using this exact tabular format:

```markdown
## [YYYY-MM-DD HH:MM] - <Short Task Title>

| Phase | What | Why |
|---|---|---|
| **Before (Plan)** | **What needs to be done:** <Clear, actionable summary of the planned changes> | <Reasoning, root cause, or goal behind this approach> |
```

### 2. Post-Implementation Log (After completing code)

Immediately after finishing the implementation and verifying your changes—and **before** replying to the user—update that same entry in `Plan/progress/PROGRESS.md` by appending the `After (Summary)` row directly to its table:

```markdown
| **After (Summary)** | **What was done:** <Specific files modified/created and summary of actual changes> | <Reasoning for final implementation choices, trade-offs made, or deviations from the plan> |
```

### Strict Rules

- **Location:** Always write progress logs to `Plan/progress/PROGRESS.md` (inside the `Plan/progress/` folder).
- **Tabular Format:** All entries in `Plan/progress/PROGRESS.md` must be output in Markdown tabular form (`| Phase | What | Why |`). Keep each row on a single line (use `<br>` for multi-item lists and escape literal `|` as `\|`) and preserve clean UTF-8 (`U+FFFD` count == 0 per `F-04`).
- **Gatekeeping:** Never modify source files until the `Before (Plan)` row is saved to `Plan/progress/PROGRESS.md`.
- **Completion:** A task is not considered complete until the `After (Summary)` row is saved to `Plan/progress/PROGRESS.md`.
- **Preserve History:** Always append new entries to the bottom of `Plan/progress/PROGRESS.md`; never overwrite previous task logs.
