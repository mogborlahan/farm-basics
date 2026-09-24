# Recommended Agent Skills for the Farm Basics Project

> **Status: INSTALLED — see `skills\skills-lock.json` for the current lock (141 skills, installed 2026-09-23 at `~/.agents/skills\`).** Skills live as `SKILL.md` folders and are loaded on demand by the agent; opencode discovers them from `.opencode/skills/`, `~/.config/opencode/skills/`, `~/.agents/skills/`, and `~/.claude/skills/` (see https://opencode.ai/docs/skills). The "Fit" column explains why each skill helps this repo (plan/BRD/UPS documents, Phase 0–3 builds, QA, consoles, free-tier infra).

> **Stack note (2026-09-24):** the mobile platform decision is now **Flutter (Android + iOS + Web)** — see `Doc\02-Engineering\11-ENG-Technology-Stack-and-Tooling-Plan.md` D-01 (implements the revised rationale in `01-ENG-System-Architecture-Document.md` §8.1). Earlier draft sections of this file that referenced native Android / Flutter-as-rejected are outdated; §5 and §6 below are updated to match. The Matt Pocock discipline skills (`tdd`, `diagnosing-bugs`, `code-review`, `research`, `writing-for-agents`) were installed 2026-09-24 via `npx skills add mattpocock/skills` (global, opencode).

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

## 5. Mobile app: verification & E2E (current stack — Flutter per ENG 01 §8.1 / Doc 11 D-01)

> The platform decision in `Doc\02-Engineering\01-ENG-System-Architecture-Document.md` §8.1 and `Doc\02-Engineering\11-ENG-Technology-Stack-and-Tooling-Plan.md` D-01 elected **Flutter (Android + iOS + Web)**, so the Flutter-oriented entries apply directly. The pre-2026-09-24 draft of this section referenced a native-Android decision — superseded.

| Skill | Source | What it does | Fit for Farm Basics |
|---|---|---|---|
| `agent-device` (CLI + MCP server) | github.com/callstack/agent-device | AI-agent device automation & verification for native Android/iOS and more (ADB/XCTest bridges, accessibility-tree snapshots, interactions, evidence/replay, CI) | Mobile E2E verification for registration, field-boundary capture, and offline-sync flows (S5–S8); regression + race/dupe scenarios (S23); UAT on real Android devices (S24). **Recommended.** |
| `flutter-skill` (MCP server; has native Android + iOS SDKs) | github.com/ai-dashboad/flutter-skill | Zero-test-code E2E via MCP across Android, iOS, web, React Native, desktop | Native Android/iOS E2E without brittle selectors. **Review before adopting:** large third-party dependency (tracking name has a typo), so pilot on one device stack in staging before committing. |
| `awesome-agent-skills` (registry, not a skill) | github.com/VoltAgent/awesome-agent-skills | Verified index of 1000+ agent skills from official teams + community (includes Android-aligned entries such as Appium/Espresso/XCUITest suites, Android APK security scanners, Sentry Android SDK) | Discovery source whenever an Android/iOS or security skill is needed; the repo lists the ones worth installing. |

## 6. Flutter/Dart skill set (ACTIVE stack — Flutter elected per ENG 01 §8.1 / Doc 11 D-01)

> **Install as part of the farmer-app build phase (S5+).** Flutter is the decided platform (Doc 11 D-01), so the Flutter/Dart skill set is now required, not conditional. The pre-2026-09-24 draft labelled this section "NOT needed under the current native-Kotlin decision" — superseded. Adopt in this order:

| Skill | Source | Notes |
|---|---|---|
| `flutter/agent-plugins` | github.com/flutter/agent-plugins | Official Flutter team (3k+ stars): layout fixes, go_router navigation, layered architecture, integration/widget tests. First port of call for farmer-app work. |
| `dash_skills` | github.com/kevmoo/dash_skills | Core Dart team contributor: best practices, test coverage, profile-dart-code, dash-discover. Also links the official `flutter/skills` and `dart-lang/skills` repos. |
| `owasp-mobile-security-checker` (+ `flutter-tester`) | github.com/Harishwarrior/flutter-claude-skills | OWASP Mobile Top 10 automated scanners + Flutter testing patterns. |
| `flutter-skills` (scalable-app + auditors) | github.com/RobertAlvv/flutter-skills | Architecture/state/perf/design-system/testability/CI-CD audit skills for enterprise Flutter. |

## 7. Already present on this machine (no action needed)

- `microsoft-foundry` skill set at `~/.agents/skills/microsoft-foundry/` (deploy-model, capacity, finetuning, agents). Use it if the platform later adds Azure OpenAI-based advisory features.
- Matt Pocock discipline skills at `~/.agents/skills/`: `tdd`, `diagnosing-bugs`, `code-review`, `research`, `writing-for-agents` (installed 2026-09-24). See §3.

## Suggested review order

1. `docx`, `pdf`, `xlsx` + `skill-creator` (immediate value for deliverables).
2. `webapp-testing` + `frontend-design` (actively building consoles in S1–S12).
3. Design-quality set — `impeccable` + `taste-skill`, plus the transferable `emilkowalski/skills` (at UI build start; supports S23 design conformance).
4. `agent-device` (callstack) ahead of mobile feature sprints — Flutter E2E verification (S5+); add the §6 Flutter/Dart set as the farmer-app build starts.
5. `ponytail` from S1 to set the YAGNI/discipline tone.
6. `devsecops-free-*` before the Phase-0 infra sprint (S1–S2).
7. Everything else on demand; Section 6 is now the active mobile stack, not conditional.

## Next step

- Tag the 3–5 skills you approve in the first review order (reply with their names) and I will install only those, then confirm each was found by the agent.