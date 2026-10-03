# Farm Basics

Farm Basics is a product blueprint (specification set + implementation plan) and public landing page for a single-digital-identity platform for Nigerian smallholder farms. Implementation stack per `Doc/02-Engineering/01A-ENG-Technology-Stack-and-Tooling-Plan.md`: Flutter (farmer/buyer/vendor apps), React 19 (admin console), ASP.NET Core + PostgreSQL/PostGIS (backend), single VM + Docker Compose + Caddy behind a Cloudflare free-tier edge.

## Commands

| What | Command |
|---|---|
| Rebuild plan HTML from Markdown | `powershell -NoProfile -ExecutionPolicy Bypass -File Plan/regenerate.ps1` — needs `pandoc` on `PATH` or `$env:PANDOC` set; uses `Plan/tools/build_page.py` + `Plan/assets/farm-basics.css` |
| Landing page | static, no build step (`site/`) |
| Commit spec changes | `git -C Doc add -A; git -C Doc commit` — and likewise for `Archived/`. Two independent local-only repos, no remotes; see Conventions |
| Archive specs offsite | `powershell -NoProfile -ExecutionPolicy Bypass -File Plan/tools/archive-specs.ps1` — encrypted `git bundle` archives |
| Tests/lint/build | none yet — apps not scaffolded; see Doc 01A §11 execution plan |
| Environment | Windows host, Windows PowerShell 5.1 only (`pwsh`/PowerShell 7 is **not** installed); `Plan/regenerate.ps1` resolves Python at run time (Anaconda was removed 2026-10-01, so no anaconda path is assumed); `pandoc` installed to `%LOCALAPPDATA%\Pandoc` via winget and is off `PATH` until a new shell opens |

## Project structure

- `Plan/` — implementation programme + recommended skills; `Farm-Basics-Implementation-Sprints.html/.md` is the task breakdown
- `Doc/01-Business … 06-Operations` — specification set: BRD, PRD, engineering (01–11), legal, QA, design, ops. **Its own local-only Git repo, no remote**
- `Archived/` — superseded v1-era specs, cross-reference only. **Its own local-only Git repo, no remote**
- `site/` — static Netlify landing page
- `infra/`, `apps/` — planned (Doc 01A §11 execution plan), not yet created

## Skill map — load before acting (mandatory per global AGENTS.md)

| Task domain | Skills to load |
|---|---|
| Flutter app work | `flutter-*`, `dart-*` skills (state, routing, localization, widget tests, JSON serialization, analyze/format) |
| Admin/React work | `frontend-design`, `design-taste-frontend`, `webapp-testing` (Playwright) |
| Backend/.NET/PostGIS | `database-schema-designer`, `migration-architect`, `api-design-reviewer`, `ci-cd-pipeline-builder`, `observability-designer`, `runbook-generator`, `dependency-auditor` (installed 2026-10-01, `Recommended-Skills.md` §8) plus `dev-*` + `tdd`; follow Doc 02-Engineering |
| App QA / emulator | `agent-device`, `flutter-skill`, `e2e-testing` |
| Planning/execution | `dev-plan`, `dev-implement`, `dev-review`, `dev-verify`, `issue`, `todo`, `git` |
| Security audit | `owasp-mobile-security-checker` (Flutter/mobile) |
| Documents | `docx`, `pdf`, `xlsx`, `pptx` when producing those artifacts |
| Design system / brand | `canvas-design`, `brandkit`, `theme-factory` for branded deliverables |

## Conventions & gotchas

- Docs: keep doc-header convention (Version, Status, Document Sequence, Depends On, Source of Truth, Audience) used across `Doc/`.
- Design System 2 tokens are the source of truth for any UI work — read `Doc/05-Design/Farm-Basics-Design-System-2.md` and `Doc/05-Design/farm-basics-styleguide-2.html`. The v1 pair (`farm-basics-design-system.md`, `farm-basics-styleguide.html`) is **superseded**; never implement from it. Note v2 renumbered sections vs v1 (Accessibility §12→§19, Content & Voice §13→§20, Platform Guidance §14→§21, Tokens §15→§23, Screen Patterns §16→§24, Governance §17→§27).
- `Plan/Recommended-Skills.md` is Flutter-consistent (refreshed 2026-09-24 per Doc 01A §11 item 7 — its §5/§6 were the last native-Android holdouts) and was updated again 2026-10-01 with §8 recording seven backend skills installed from `alirezarezvani/claude-skills` and the `chisel` removal. Its header, not `skills/skills-lock.json`, is the authority on the current global inventory — the lock predates those changes (F-11).
- Before installing any skill from a third-party repo, run `skill-inspector` on it. `skillspector` (the SkillSpector CLI) is **not** installed here, so the 2026-10-01 batch was assessed by documented manual review, not a scanner run.
- Doc 01A §12 records open items (cloud provider/data-residency, CBN escrow, Keycloak realm shape, break-glass custody) — don't design around them.
- Doc 01A §14 lists five prior documents whose Kong-era statements were superseded. All five are now corrected in place (2026-09-28): `01-ENG` §15 (`Caddy` per D-08), `03-ENG` §1.8 (rate limiting → `Microsoft.AspNetCore.RateLimiting` per D-11), `04-ENG` (Caddy diagram + §9 threat table). Kong appears in `Doc/` only as historical narrative inside `01A` itself.
- Never invent commands; verify against the repo before quoting them.
- **`Doc/` and `Archived/` are separate local-only Git repos nested inside this one.** Each carries full version history and **no remote is configured** — a deliberate decision, so no third party (GitHub included) can be compelled or breached to disclose the specification set. This repo gitignores both, so `git add -A` here never touches them; commit spec changes in two places: `git -C Doc commit` and `git -C Archived commit`. Both set `.gitattributes` to `* -text`, so blobs are stored byte-exact with no line-ending normalisation — F-04 was a silent double-encoding incident, so archival fidelity outranks EOL tidiness. Offsite durability comes from scheduled `git bundle` archives (`Plan/tools/archive-specs.ps1`) rather than a hosting provider: a private GitHub repo would still be reachable by legal compulsion, and the `Farm-Basics` org is on the free plan where GitHub **cannot** enforce 2FA. **Never run `git clean -ffdx` in this repo** — single-force `-fdx` skips nested repositories, but `-ffdx` destroys them (verified empirically). `skills/` remains gitignored with no history (F-11). Resolved from F-12.
- **UTF-8 on every read and write.** Never mix a default-encoded read with a UTF-8 write. An unqualified `Get-Content -Raw` followed by a `UTF8Encoding` `WriteAllText` re-encodes the file as CP1252 and silently double-encodes every non-ASCII byte — em dashes, `§`, curly quotes, `→` all become mojibake, and the corruption looks like a display glitch rather than a write. This already damaged `05-ENG` (66 em dashes, 93 section signs) and `06-DATA` once. Safe options: the `edit` tool, `Get-Content -Encoding UTF8`, or an explicit `UTF8Encoding` on *both* sides. After any bulk write, verify with `U+FFFD` count == 0. Tracked as `F-04`.
- **Sprint changes land in the sprint.** This is a solo build with no current aspiration to be a team. A change reflects immediately in `Plan/Farm-Basics-Implementation-Sprints.md` unless an `F-##` entry exists in `Plan/sessions/Followups-Ledger.md`, in which case work continues against that entry. There is no separate SDLC document — P0.1–P0.8 of the Implementation Sprints Preamble *is* the lifecycle, plus Sprint-Plan §17 (DoD) and §18 (production readiness). Inline correction of `Doc/` sources is permitted per P0.7.4 when it is a consistency fix against another source or a locked `Doc 01A` decision; a new architectural/legal/commercial decision still halts for clarification per P0.7.2. Review-approval rules are solo-adapted in `Doc/02-Engineering/07-ENG-CI-CD-and-Environment-Strategy.md` §2 — CI plus declared self-review, not peer approval. The in-product dual sign-off (`MarketplaceOperationsAdmin` + `Finance`, BR-C-06, ALT-AD08) is a schema-enforced role requirement and is unaffected by solo operation.