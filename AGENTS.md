# Farm Basics

Farm Basics is a product blueprint (specification set + implementation plan) and public landing page for a single-digital-identity platform for Nigerian smallholder farms. Implementation stack per `Doc/02-Engineering/11-ENG-Technology-Stack-and-Tooling-Plan.md`: Flutter (farmer/buyer/vendor apps), React 19 (admin console), ASP.NET Core + PostgreSQL/PostGIS (backend), single VM + Docker Compose + Caddy.

## Commands

| What | Command |
|---|---|
| Rebuild plan HTML from Markdown | `pwsh Plan/regenerate.ps1` (pandoc + `Plan/tools/build_page.py`) |
| Landing page | static, no build step (`site/`) |
| Tests/lint/build | none yet — apps not scaffolded; see Doc 11 §9 |
| Environment | Windows host; `plan/regenerate.ps1` pins anaconda python |

## Project structure

- `Plan/` — implementation programme + recommended skills; `Farm-Basics-Implementation-Sprints.html/.md` is the task breakdown
- `Doc/01-Business … 06-Operations` — specification set: BRD, PRD, engineering (01–11), legal, QA, design, ops
- `Archived/` — superseded v1-era specs, cross-reference only
- `site/` — static Netlify landing page
- `infra/`, `apps/` — planned (Doc 11 §9), not yet created

## Skill map — load before acting (mandatory per global AGENTS.md)

| Task domain | Skills to load |
|---|---|
| Flutter app work | `flutter-*`, `dart-*` skills (state, routing, localization, widget tests, JSON serialization, analyze/format) |
| Admin/React work | `frontend-design`, `design-taste-frontend`, `webapp-testing` (Playwright) |
| Backend/.NET/PostGIS | no dedicated skill — apply `dev-*` skills + tdd; follow Doc 02-Engineering |
| App QA / emulator | `agent-device`, `flutter-skill`, `e2e-testing` |
| Planning/execution | `dev-plan`, `dev-implement`, `dev-review`, `dev-verify`, `issue`, `todo`, `git` |
| Security audit | `owasp-mobile-security-checker` (Flutter/mobile) |
| Documents | `docx`, `pdf`, `xlsx`, `pptx` when producing those artifacts |
| Design system / brand | `canvas-design`, `brandkit`, `theme-factory` for branded deliverables |

## Conventions & gotchas

- Docs: keep doc-header convention (Version, Status, Document Sequence, Depends On, Source of Truth, Audience) used across `Doc/`.
- Design System 2 tokens are the source of truth for any UI work — read `Doc/05-Design/farm-basics-styleguide.html`.
- `Plan/Recommended-Skills.md` is stale (still claims native Android/Flutter rejected) — refresh per Doc 11 §9 item 6 before relying on it.
- Doc 11 §10 records open items (cloud provider/data-residency, CBN escrow) — don't design around them.
- Never invent commands; verify against the repo before quoting them.