# Recommended Agent Skills for the Farm Basics Project

> **Status: INSTALLED — see `skills\skills-lock.json` for the tracked lock (142 skills: the original 141 installed 2026-09-23, plus `archify` on 2026-10-03, at `~/.agents/skills\`).** Skills live as `SKILL.md` folders and are loaded on demand by the agent; opencode discovers them from `.opencode/skills/`, `~/.config/opencode/skills/`, `~/.agents/skills/`, and `~/.claude/skills/` (see https://opencode.ai/docs/skills). The "Fit" column explains why each skill helps this repo (plan/BRD/UPS documents, Phase 0–3 builds, QA, consoles, free-tier infra).
>
> **Current global inventory (2026-10-03): 156 skill directories** at `~/.agents/skills\` — 141 from the 2026-09-23 lock, plus 7 selectively installed from `alirezarezvani/claude-skills` (§8 below), plus additions since, plus `archify` on 2026-10-03 (§9 below). **`chisel` was removed 2026-10-01**: its server binary is not supported on Windows and no Chisel MCP server was configured, so the skill described tools no session had. Reassess only if a Linux/WSL host is adopted. `skills\skills-lock.json` predates the §8 additions and the chisel removal and is therefore no longer a complete inventory — treat this file as the authority for what changed after 2026-09-23. It was refreshed on 2026-10-03 to add the `archify` entry only; the 18 post-lock skills still on disk remain untracked and the four removed ones (`frontend-design`, `gpt-tasteskill`, `soft-skill`, `taste-skill-v1`) remain as tombstones. Provenance for the untracked 18 could not be recovered: `%LOCALAPPDATA%\opencode\skills-install\repos` is hollow (3778 empty directories, 48 files) and `git` rejects each clone as not a repository, so hash-matching them against the original sources is impossible until that staging area is re-cloned.

> **Stack note (2026-09-24):** the mobile platform decision is now **Flutter (Android + iOS + Web)** — see `Doc\02-Engineering\01A-ENG-Technology-Stack-and-Tooling-Plan.md` D-01 (implements the revised rationale in `01-ENG-System-Architecture-Document.md` §8.1). Earlier draft sections of this file that referenced native Android / Flutter-as-rejected are outdated; §5 and §6 below are updated to match. The Matt Pocock discipline skills (`tdd`, `diagnosing-bugs`, `code-review`, `research`, `writing-for-agents`) were installed 2026-09-24 via `npx skills add mattpocock/skills` (global, opencode).

## 1. Document & artifact generation (highest leverage for this repo)

| Skill | Source | What it does | Fit for Farm Basics |
|---|---|---|---|
| `docx` | github.com/anthropics/skills | Generates Word documents with styles, TOC, tables | Deliver the sprint plan, UAT protocol, and BRD as polished `.docx` to stakeholders |
| `pdf` | github.com/anthropics/skills | Produces styled PDF artifacts | Export specs/checklists as shareable PDFs |
| `pptx` | github.com/anthropics/skills | Builds PowerPoint decks | Phase gate / go-no-go board presentations |
| `xlsx` | github.com/anthropics/skills | Creates Excel workbooks with formulas | The 12-role permission matrix and effort/capacity tracking sheets |
| `doc-coauthoring` | github.com/anthropics/skills | Co-writes long documents with tracked revisions | Maintaining Parts A/B and the BRD through change requests |
| `skill-creator` | github.com/anthropics/skills | Authoring new skills | Wrap repeated project procedures (e.g., a "10-minute micro-task slicing" skill) |

## 2. Web, design & testing

| Skill | Source | What it does | Fit for Farm Basics |
|---|---|---|---|
| `webapp-testing` | github.com/anthropics/skills | Automated testing of web applications | Browser-level regression for farmer app + admin console |
| `frontend-design` | github.com/anthropics/skills | Clean, modern UI design | Admin console and farmer app screens in the Design System |
| `canvas-design` | github.com/anthropics/skills | Styled drawings/illustrations | Field-boundary maps and how-to visual guides |
| `theme-factory` | github.com/anthropics/skills | Generates cohesive color themes | Enforcing Design System 2 tokens across platforms |
| `brand-guidelines` | github.com/anthropics/skills | Consistent brand application | Keeping the logo/mark usage compliant inside consoles |

### Design & animation quality — frontend build phase (consoles S1/S12/S19, farmer app UI, S23 conformance)

| Skill | Source | What it does | Fit for Farm Basics & caveats |
|---|---|---|---|
| `impeccable` | github.com/pbakaus/impeccable (Paul Bakaus; ~66k★, Apache-2.0) | Design-language guardrails for AI-generated frontends: 23 commands, live browser iteration, 61 deterministic detector rules | Enforce DS2 design language across admin console + farmer app once UI builds start. OpenCode-supported (`npx impeccable install`). Caveat: installer downloads a compiled engine binary on first run. |
| `taste-skill` (`design-taste-frontend`) | github.com/Leonxlnx/taste-skill (Leon Lin; ~85k★, MIT; Vercel OSS + Emil Kowalski programs) | Anti-slop frontend framework: reads the brief, infers design direction, maps to a design system, bans template output | Map DS2 tokens/typography/motion into agent output and stop boilerplate UI. OpenCode-compatible. Caveat: v2 is experimental — pin v1 if a change disturbs DS2 styling. |
| `emilkowalski/skills` | github.com/emilkowalski/skills (Emil Kowalski, Vercel/Linear; ~40k★, MIT) | Design/engineering skills: emil-design-eng, animate, review/improve-animations, animation-vocabulary, apple-design, mobile-native, pick-ui-library, prototype, write-swift, ask-sonner | DS2 emphasises motion (arc motif, two experience modes). Adopt the transferable ones (emil-design-eng, review-animations, animation-vocabulary, apple-design, mobile-native for the web admin console). Skip Write-Swift (the mobile app is Flutter/Dart, not Swift) and animate-expo (React Native). |

## 3. Workflow, planning & engineering

| Skill | Source | What it does | Fit for Farm Basics |
|---|---|---|---|
| `/dev-plan`, `/dev-implement`, `/dev-review` | github.com/matasarei/opencode-skills | Plan → implement → review agent workflow | Matches the progressive task → micro-task discipline of this repo |
| `system-design`, `skill-manager`, `git` | github.com/opensassi/opencode | Design docs, skill mgmt, git hygiene | Sprint 1 scaffolding, branching, gitops |
| `/opencode-add-mcp`, `/opencode-sessions` | github.com/gideonfip/opencode-skills | MCP server setup, session management | Wiring MCP connectors as the project grows |
| `mcp-builder` | github.com/anthropics/skills | Builds MCP servers | Project-specific connectors (e.g., Prembly/PayGuard-style service clients) |
| `claude-api` | github.com/anthropics/skills | Robust Claude API usage | If AI-assisted features are added later (support assistant) |
| `ponytail` (always-on ruleset + `/ponytail` review/audit/debt/gain commands) | github.com/DietrichGebert/ponytail (MIT; OpenCode-supported) | Forces the "laziest senior dev" path: reach for the platform/stdlib before writing new code; marks every shortcut with an upgrade-path comment (~80–94% less code in published benchmarks) | Aligns with this repo's micro-task discipline and debt-free architecture (ENG 02/03): keeps shortcuts traceable and prevents over-engineering across S1–S24. Set the YAGNI tone from S1. |
| `tdd`, `diagnosing-bugs`, `code-review`, `research`, `writing-for-agents` | github.com/mattpocock/skills (Matt Pocock; ~268k★, MIT) — **installed 2026-09-24** | Red-green-refactor TDD loop; gated bug-diagnosis loop; two-axis (Standards+Spec) parallel code review; cited primary-source research; SKILL.md/AGENTS.md authoring | Backend, admin, and farmer-app build phases (S1+): TDD per `12-QA-Master-Test-Plan.md` §9, review discipline to match the plan's verification gates. `code-review` references a `docs/agents/issue-tracker.md` that we did not install (`/setup-matt-pocock-skills` deliberately skipped) — it degrades to asking for the spec source. |

## 4. Infrastructure & security (free-tier devsecops)

| Skill | Source | What it does | Fit for Farm Basics |
|---|---|---|---|
| `devsecops-free-*` (e.g. free-cloud, free-cicd, free-monitoring, free-security, free-auth) | github.com/open-hax/opencode-skills | Free-tier infrastructure provisioning & audit | Sprint 1–2 infra: closed-loop free Postgres/CI/CD/monitoring/auth |
| `internal-comms`, `discernment-nudge` | github.com/anthropics/skills | Clear comms, decide-vs-ask | Phase gate communications; flag when scope needs review |

## 5. Mobile app: verification & E2E (current stack — Flutter per ENG 01 §8.1 / Doc 01A D-01)

> The platform decision in `Doc\02-Engineering\01-ENG-System-Architecture-Document.md` §8.1 and `Doc\02-Engineering\01A-ENG-Technology-Stack-and-Tooling-Plan.md` D-01 elected **Flutter (Android + iOS + Web)**, so the Flutter-oriented entries apply directly. The pre-2026-09-24 draft of this section referenced a native-Android decision — superseded.

| Skill | Source | What it does | Fit for Farm Basics |
|---|---|---|---|
| `agent-device` (CLI + MCP server) | github.com/callstack/agent-device | AI-agent device automation & verification for native Android/iOS and more (ADB/XCTest bridges, accessibility-tree snapshots, interactions, evidence/replay, CI) | Mobile E2E verification for registration, field-boundary capture, and offline-sync flows (S5–S8); regression + race/dupe scenarios (S23); UAT on real Android devices (S24). **Recommended.** |
| `flutter-skill` (MCP server; has native Android + iOS SDKs) | github.com/ai-dashboad/flutter-skill | Zero-test-code E2E via MCP across Android, iOS, web, React Native, desktop | Native Android/iOS E2E without brittle selectors. **Review before adopting:** large third-party dependency (tracking name has a typo), so pilot on one device stack in staging before committing. |
| `awesome-agent-skills` (registry, not a skill) | github.com/VoltAgent/awesome-agent-skills | Verified index of 1000+ agent skills from official teams + community (includes Android-aligned entries such as Appium/Espresso/XCUITest suites, Android APK security scanners, Sentry Android SDK) | Discovery source whenever an Android/iOS or security skill is needed; the repo lists the ones worth installing. |

## 6. Flutter/Dart skill set (ACTIVE stack — Flutter elected per ENG 01 §8.1 / Doc 01A D-01)

> **Install as part of the farmer-app build phase (S5+).** Flutter is the decided platform (Doc 01A D-01), so the Flutter/Dart skill set is now required, not conditional. The pre-2026-09-24 draft labelled this section "NOT needed under the current native-Kotlin decision" — superseded. Adopt in this order:

| Skill | Source | Notes |
|---|---|---|
| `flutter/agent-plugins` | github.com/flutter/agent-plugins | Official Flutter team (3k+ stars): layout fixes, go_router navigation, layered architecture, integration/widget tests. First port of call for farmer-app work. |
| `dash_skills` | github.com/kevmoo/dash_skills | Core Dart team contributor: best practices, test coverage, profile-dart-code, dash-discover. Also links the official `flutter/skills` and `dart-lang/skills` repos. |
| `owasp-mobile-security-checker` (+ `flutter-tester`) | github.com/Harishwarrior/flutter-claude-skills | OWASP Mobile Top 10 automated scanners + Flutter testing patterns. |
| `flutter-skills` (scalable-app + auditors) | github.com/RobertAlvv/flutter-skills | Architecture/state/perf/design-system/testability/CI-CD audit skills for enterprise Flutter. |

## 7. Already present on this machine (no action needed)

- `microsoft-foundry` skill set at `~/.agents/skills/microsoft-foundry/` (deploy-model, capacity, finetuning, agents). Use it if the platform later adds Azure OpenAI-based advisory features.
- Matt Pocock discipline skills at `~/.agents/skills/`: `tdd`, `diagnosing-bugs`, `code-review`, `research`, `writing-for-agents` (installed 2026-09-24). See §3.
- `skill-inspector` at `~/.agents/skills/` — pre-install review gate for any skill pulled from a third-party repo. See §8.

## 8. Installed 2026-10-01 — backend & platform (from `alirezarezvani/claude-skills`)

Source: `github.com/alirezarezvani/claude-skills` at commit `19392f7`, MIT. Reviewed by hand because `skillspector` (the SkillSpector CLI the reviewer prefers) is not installed on this host; treat the security assessment as a documented manual review, not a scanner run. Installed globally to `~/.agents/skills/`.

| Skill | What it does | Fit for Farm Basics |
|---|---|---|
| `database-schema-designer` | Schema/ERD review, normalisation, indexing, constraint design | `02-ENG-Database-Schema-and-ERD.md` is the DB-change baseline for every migration; use before any `apps/` schema work |
| `migration-architect` | Expand/contract migration sequencing, zero-downtime rollout, backfill & rollback planning | EN 07 §4 promotion/rollback model; contract-phase-first sequencing for the sign-off and idempotency changes |
| `api-design-reviewer` | REST/resource review — verbs, status codes, pagination, error envelopes, versioning | `03-ENG-API-Specification.md`; consistency pass against the idempotency and webhook replay rules |
| `ci-cd-pipeline-builder` | Pipeline authoring and hardening (stages, gates, caching, secrets handling) | The seven-stage mandatory CI gate in EN 07 §2 (solo mode) is the first real consumer |
| `runbook-generator` | Turns a procedure into an operator runbook | `09-ENG-Disaster-Recovery-Runbook.md` (R-09 restore verification) and the Phase-0 alert-triage runbook |
| `observability-designer` | SLI/SLO definition, metric/label cardinality, dashboard & alert design | `08-ENG-Observability-and-Alerting.md`; constrains the S3.2 alert taxonomy so cardinality is bounded by design |
| `dependency-auditor` | Dependency review — licence, known CVEs, lockfile drift, upgrade risk | Runs against the ASP.NET Core / NuGet and Flutter / pub graphs as they appear |

Deliberately **not** installed: `skill-security-auditor` from the same repo — `skill-inspector` already covers that ground locally.

Overlap note: this batch was initially suspected of duplicating existing skills. A semantic comparison against the installed inventory found the concerns were unfounded — all seven are materially distinct from what is already present, and nothing was removed as a result.

## 9. Installed 2026-10-03 — `archify` (from `tt-a1i/archify`)

Source: `github.com/tt-a1i/archify` at commit `d5a1333` (2026-09-30), MIT, v3.0.1. Reviewed by hand because `skillspector` is not installed on this host; treat the security assessment as a documented manual review, not a scanner run. Installed globally to `~/.agents/skills/archify/` from the maintainer's own release artifact `archify.zip` (106 files), which was byte-compared against the reviewed clone — 105 of 106 files identical, the sole difference being `package.json` with `scripts` and `devDependencies` stripped, i.e. the release deliberately removes build and test surface. `SKILL.md` SHA-256 `66DFE99B…AAF3`, recorded in `skills\skills-lock.json` as `sourceType: github-release-zip`.

| Skill | What it does | Fit for Farm Basics |
|---|---|---|
| `archify` | Renders typed JSON-IR into standalone interactive HTML with inline SVG — `architecture`, `workflow`, `sequence`, `dataflow`, and `lifecycle` diagram types, dark/light themes, opt-in trace motion, PNG/JPEG/WebP/SVG/WebM export. Accepts plain-language requirements or pasted Mermaid `flowchart` / `sequenceDiagram` / `stateDiagram`. | `lifecycle` maps onto the `review_status` and reservation state machines settled in RUN-0003; `sequence` onto `03-ENG-API-Specification.md`; `architecture` onto the D-08 Caddy topology; `dataflow` onto the 12-schema PostGIS model. Produces shareable standalone artifacts alongside `site/`. |

Security findings from the manual review:

- **Zero runtime dependencies** — no `dependencies` field at all. Repo `devDependencies` (`ajv`, `parse5`, `saxes`, `simple-icons`) are stripped from the shipped artifact and were not installed.
- **Two narrow network paths, both documented.** The update check is a single `GET` to one hardcoded URL, pinned by equality against `DEFAULT_MANIFEST_URL` (`scripts/check-update.mjs:1383`) so a tampered `skill-release.json` cannot redirect it; `redirect: 'error'`, JSON content-type asserted, body bounded, 1s deadline, SIGKILLed child, writes only to `%LOCALAPPDATA%\archify-skill\`. The module contains no install, exec, or self-update path. Opt out with `ARCHIFY_UPDATE_CHECK_DISABLED=1`. Brand-mark fetching happens only on an explicit logo request and carries an SSRF guard: http/https only, non-default ports and `localhost`/`.local` rejected, and DNS-resolved addresses checked for private ranges (IPv4, IPv6, IPv4-mapped) across *all* returned addresses, which defeats DNS rebinding.
- **Process execution is bounded** — its own `node` via `process.execPath`, read-only `git` (`cat-file --batch`, `rev-parse`, `remote get-url`, `show`, `status`) with `--no-replace-objects`, and the platform opener. The Windows opener passes the target through the `ARCHIFY_OPEN_TARGET` env var into `Start-Process -FilePath $env:ARCHIFY_OPEN_TARGET` rather than string interpolation, so it is injection-safe. Git remotes are redacted before being embedded in output.
- **No credential access** — env reads are limited to `ARCHIFY_*`, `LOCALAPPDATA`, `XDG_CACHE_HOME`. No `~/.ssh`, no `~/.aws`, no token reads. No `eval`, no `new Function`.
- **Cache layer is hardened** — path-escape rejection, directory-snapshot anti-swap verification, atomic temp+rename writes, `0o700` directories, and PID+token claim files with generation fencing for concurrent runs.

Residual risk: single unknown maintainer, no release signatures, so integrity rests on trusting GitHub plus the pinned commit. `finalize` and `deliver` egress on every run unless the env opt-out is set.

Scope decision — **use as a presentation tool, not as a Mermaid replacement inside `Doc\`**:

- `Doc\`, `Archived\`, and `skills\` are gitignored, so Archify's `--repo-root` evidence mode — which reads through `git rev-parse` / `ls-files` / `cat-file` — **cannot see the specification set**. Diagrams must be authored from plain-language descriptions, which forfeits the tool's main differentiator for this repo.
- Output is standalone HTML, not Markdown, so a generated diagram cannot be pasted back into a `Doc\*.md` file. Substituting it for Mermaid would fork the source of truth that this repo's governance explicitly guards.
- It writes a `.archify/` folder into the working directory, which needs gitignoring.

Preconditions verified on this host: Node v24.21.0 (requires `>=18`) and Chrome at `C:\Program Files\Google\Chrome\Application\chrome.exe`, which `finalize`'s mandatory `browser-check` stage drives headless over CDP. `node bin/archify.mjs doctor` reports all 22 checks `[ok]` and exits 0.

## Suggested review order

1. `docx`, `pdf`, `xlsx` + `skill-creator` (immediate value for deliverables).
2. `webapp-testing` + `frontend-design` (actively building consoles in S1–S12).
3. Design-quality set — `impeccable` + `taste-skill`, plus the transferable `emilkowalski/skills` (at UI build start; supports S23 design conformance).
4. `agent-device` (callstack) ahead of mobile feature sprints — Flutter E2E verification (S5+); add the §6 Flutter/Dart set as the farmer-app build starts.
5. `ponytail` from S1 to set the YAGNI/discipline tone.
6. `devsecops-free-*` before the Phase-0 infra sprint (S1–S2).
7. Everything else on demand; Section 6 is now the active mobile stack, not conditional.

Items 1–7 are installed already or on demand. The one genuinely outstanding decision is **`impeccable`** (F-02): it needs `npx impeccable install`, which downloads a compiled engine binary on first run, so hold it until UI work starts rather than pre-installing.

## Next step

- Install the §6 Flutter/Dart set when the farmer-app build phase (S5) opens — `flutter/agent-plugins` first, then `dash_skills`, then the OWASP/testing pair.
- F-01 (revisit `awesome-agent-skills` discovery) is stale: the mobile platform is already decided as Flutter (Doc 01A D-01), so the "which mobile stack" premise no longer applies. Close it unless you want the registry kept as a general security-skill discovery source.