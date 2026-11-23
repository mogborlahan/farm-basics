# Sprint 10 Security, Authorization & Dependency Audit (`WP-A1` / `Doc 04-ENG` §14 / `Rule 14`)

**Date:** 2026-11-23 (`S10.2.M2`–`S10.2.M3`)
**Scope:** Static source analysis, live RBAC route verification, exact-pinning & vulnerability scan across `.NET 10` (`FarmBasics.sln`), React 19 Admin Console (`apps/admin`), Flutter Mobile (`apps/farmer-app`), and Docker Compose (`infra/`).

---

## 1. Dependency Vulnerability & Exact-Pinning Audit (`Rule 14`)

- **NuGet (`dotnet list FarmBasics.sln package --vulnerable --include-transitive`):**
  - `FarmBasics.BuildingBlocks`: **0 vulnerable packages**
  - `FarmBasics.Api`: **0 vulnerable packages**
  - `FarmBasics.UnitTests`: **0 vulnerable packages**
  - `FarmBasics.IntegrationTests`: **0 vulnerable packages**
- **npm (`pnpm --dir apps/admin audit --audit-level=high`):**
  - **Exit 0** (**0 High**, **0 Critical** vulnerabilities).
  - Full `pnpm --dir apps/admin audit`: **1 moderate** dev-tooling advisory (`minimatch` `>=7.0.0 <11.0.1` via `eslint` / `typescript-eslint`, `GHSA-g3hv-75wg-m249`), zero runtime production bundle exposure. Per CI Stage 4 (`audit --audit-level=high`) and `Rule 14` dependency freeze, no dependency override was added.
- **Exact Version Pinning & Image Tag Scan (`Rule 14`):**
  - `Directory.Packages.props`: **0** `*` / `^` / `~` specifiers.
  - `apps/admin/package.json`: **0** `^` / `~` / `*` specifiers.
  - `apps/farmer-app/pubspec.yaml`: **0** `^` / `~` / `any` specifiers.
  - `infra/docker-compose.yml`: **0** `:latest` container tags.

---

## 2. Static Secret, Sensitive-Log & SAST Scan (`Doc 04` §14, `Rule 1`)

- **Hardcoded secret pattern scan** across `apps/`, `tests/`, `infra/` (private keys, AWS keys, GitHub/Slack/Stripe tokens): **0 hits**.
- **Serilog string-interpolation scan (`Log*( $"..." )`)** across `apps/backend/src/`: **0 hits** — all structured logging uses named message templates guarded by `SensitiveDataLogEventEnricher` + unit test verification (`SensitiveDataRedactionTests`).
- **Pre-production / DAST note (`Doc/04` §14, `P12`):** Interactive staging DAST against a deployed URL remains gated on live staging VM bring-up (`F-32`); local Testcontainers + `WebApplicationFactory` integration tests execute full HTTP request/response authentication, rate limiting, and RBAC rejection paths against PostgreSQL 15 + PostGIS 3.5.

---

## 3. Authorization & Scope-Enforcement Review (`Rule 8`)

Every `/v1/admin/*` route mapped across `apps/backend/src/FarmBasics.Api` (`AccountsEndpoints`, `AgentOnboardingEndpoints`, `AnalyticsEndpoints`, `AuditEndpoints`, `FieldOpsEndpoints`, `InvariantCanaryService`, `MasterDataChangeRequestEndpoints`, `NotificationAndCapabilityEndpoints`, `ReviewEngineEndpoints`, `SupportEndpoints`) was verified to enforce:

1. **Authentication:** `.RequireAuthorization()` / `[Authorize]` with JWT algorithm pinning (`RS256`, `ES256`).
2. **Fail-closed live DB permission check:** `.RequireAdminPermission(resource, action)` backed by `PostgresAdminPermissionChecker` querying active (`revoked_at IS NULL`, `identity.status = 'active'`) DB role grants on every request (never trusting JWT claims), or strict assigned-reviewer / active `reviewer_profile` ownership validation on reviewer actions (`claim` / `verdict`).
3. **Scope boundary enforcement:** `FieldOpsEndpoints`, `AnalyticsEndpoints`, `SupportEndpoints`, and `FobEndpoints` (admin revoker branch) enforce `AdminScopeResolver.ResolveScopesAsync` / `IsIdentityInScopeAsync`, returning `403 SCOPE_VIOLATION` on cross-organization or cross-region access attempts.
4. **Deliberate absences preserved (`Doc/AGENTS.md` §4):** No direct `JournalEntry` mutation endpoints, no fob reactivation or remote reissue, no remote farmer phone change (`P6`), no direct `CustomerSupportAgent` fob revocation (`P3`), and no NDPA erasure execution (`P10`).
