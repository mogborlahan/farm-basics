# Farm Basics

Farm Basics is a product blueprint for giving Nigerian smallholder farms a single digital identity, so a farmer, their plot, and their produce history travel together from planting to market.

This repository contains the public landing page plus the internal blueprint: the full specification set (business case, product requirements, engineering design, design system) and the implementation sprint plan derived from it.

## Contents

- `Plan/` - the implementation programme in Farm Basics design-system HTML (dark sidebar, brand mark, scrollspy TOC; self-contained):
  - `Farm-Basics-Sprint-Plan.html` / `.md` - the 20-section, dependency-aware sprint plan (S1-S24 across BRD phases 0-3 + hardening)
  - `Farm-Basics-Implementation-Sprints.html` / `.md` - the full project as progressive tasks → sub-tasks → micro-tasks, every micro-task ≤ 10 minutes, with interactive checkboxes (progress persists in your browser)
  - `Recommended-Skills.md` - researched agent/developer skills, mapped to the stack (largest subset installed; tracks installs via the local `skills/skills-lock.json`)
  - `assets/farm-basics.css`, `tools/build_page.py`, `regenerate.ps1` - shared design-system shell and build pipeline (pandoc + Python)
- `Doc/` - the specification set, in a single flat directory:
  - `BRD-v2-Nigeria-Farm-Platform.md`, `PRD-Master-Volume.md`, and `PRD-Volume-A`…`E` plus the proposed `PRD-Volume-E1-CEA-Requirements-Delta.md` - business and product requirements
  - Engineering `01`–`10-ENG`/`DATA` with companions `01A-ENG`, `02B-ENG`, `04A-ENG` - architecture, tech stack, schema/ERD, API, security, zero-trust network, integrations, master-data strategy, CI/CD, observability, DR runbook, AI/agent guardrails
  - `10-`/`11-LEGAL` - regulatory/compliance memo, Terms of Service & Privacy (draft)
  - `12-`…`14-QA` - master test plan, UAT & pilot protocol, accessibility conformance
  - `Farm-Basics-Design-System-2.md`, `farm-basics-styleguide-2.html`, `assets/` - Design System 2 (current); the Design System 1 files are superseded
  - `Part A — Platform Operations.md` and `Part B — Administration & Governance.md` - unified system spec and admin console spec. Note: these two filenames use an em-dash, not a hyphen
- `Archived/` - superseded v1-era specs and pre-flatten re-downloads (`Re-downloaded-superseded/`); cross-reference only
- `site/` - the static landing page (HTML and CSS plus `netlify.toml` header/cache policy, no build step)
- `apps/backend/` - ASP.NET Core `.NET 10` API + BuildingBlocks (`FarmBasics.sln`) with Serilog secret redaction, identity-only Keycloak JWT validation, rate limiting, `/health/ready` PostGIS check, and `/metrics` Prometheus instrumentation
- `apps/admin/` - React 19 + Vite + TypeScript + Tailwind v4 (Design System 2 tokens) + Keycloak PKCE shell
- `apps/farmer-app/` - Flutter offline-first app (`WP-04` walking skeleton: `drift` `local_outbox_mutation` SQLite outbox, `dio` SHA-256 certificate-pinning adapter `D-14`, `OfflineSyncEngine` FIFO drain & idempotent replay tests)
- `infra/` - zero-trust Docker Compose topology (only `caddy` publishes 80/443), Caddy, Keycloak realm import, `oauth2-proxy`, PostGIS 15/3.5 init, Prometheus (`30d`), Alertmanager, Grafana, SOPS/age config (`.sops.yaml`, `.env.sops.yaml`), Blue/Green `deploy.sh`, and `backup/` (`wal-g-backup.sh`, `wal-g-restore-drill.sh`)
- `tests/` - `FarmBasics.UnitTests` (Serilog redaction, fail-closed auth, race harness) and `FarmBasics.IntegrationTests` (Testcontainers `postgis/postgis:15-3.5`, geodesic `ST_Area`, 50-iteration barrier-synchronised DB & `POST /v1/sync/spike-records` idempotency tests)
- `.github/workflows/ci.yml` & `codemagic.yaml` - seven non-skippable CI stages (`ci-required-gate`) and Flutter mobile CI pipeline

## Local Verification (Sprint 0)

```powershell
dotnet restore FarmBasics.sln --configfile NuGet.Config
dotnet build FarmBasics.sln --configuration Release --no-restore
dotnet test tests/FarmBasics.UnitTests/FarmBasics.UnitTests.csproj --configuration Release --no-build
dotnet test tests/FarmBasics.IntegrationTests/FarmBasics.IntegrationTests.csproj --configuration Release --no-build
dotnet format FarmBasics.sln --verify-no-changes --no-restore

pnpm --dir apps/admin run typecheck
pnpm --dir apps/admin run lint
pnpm --dir apps/admin run test
pnpm --dir apps/admin run build

pushd apps\farmer-app
flutter pub get
dart run build_runner build --delete-conflicting-outputs
flutter analyze
flutter test
popd

docker compose -f infra/docker-compose.yml --env-file infra/.env.example config --quiet
docker build -f apps/backend/Dockerfile -t farmbasics-api:local .
docker build -f apps/admin/Dockerfile -t farmbasics-admin:local .
```

## Live

- Landing page: https://farm-basics.netlify.app
- Portfolio: https://mogbolahan.netlify.app

## Contact

- Gbolahan Folarin
- gbolahan.folarin@outlook.com
- https://linkedin.com/in/mogbolahan
