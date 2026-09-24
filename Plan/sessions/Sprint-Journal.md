# Farm Basics — Sprint Journal

> **Purpose:** session ledger per `Plan\Farm-Basics-Implementation-Sprints.md` Preamble P0.8. Each session appends one entry (append-only — do not edit earlier entries). Drives the resumption protocol: the journal tail must point at the exact next un-ticked micro-task.
>
> **Entry template:**
>
> - **Date:** YYYY-MM-DD
> - **Session goal:** one line.
> - **Micro-tasks touched:** start → end IDs (e.g. S1.3.M13 → S1.3.M15).
> - **Skills applied:** per P0.6.
> - **Guardrails checked:** per P0.5 (e.g. C-1…C-5, I-1…I-4).
> - **Gate evidence:** artifacts backing any exit ticket / gate (per P0.3).
> - **Decisions / Requires Clarification:** each with one-line reason.
> - **Followup IDs raised:** any `F-##` entries added to `Plan\sessions\Followups-Ledger.md` this session.
> - **Next up:** exact un-ticked micro-task.

---

## Session log

### Session 2026-09-23 — Global skills install (Recommended-Skills.md Sections 1–6)

- **Date:** 2026-09-23
- **Session goal:** Install all recommended agent skills globally so any agent (opencode, Claude Code, freebuff) can load them; refresh lock-file and records.
- **Micro-tasks touched:** none (tooling/operating-layer session, pre-Sprint-1).
- **Skills applied:** `customize-opencode` (install mechanics per https://opencode.ai/docs/skills).
- **Guardrails checked:** none triggered (no repo code touched; no secrets; external sources are public GitHub repos).
- **Gate evidence:** `skills\skills-lock.json` regenerated (v2) with 141 entries, each traced to its source repo + `SKILL.md` SHA-256; installed at `~/.agents/skills\`.
- **Decisions / Requires Clarification:**
  - Installed **globally** (user: "install everything") at `~/.agents/skills\` (cross-agent) + `~/.config/opencode\command\` for ponytail commands. Global install is additive, does not restrict this project.
  - Scope = all Sections 1–6 including the Flutter/Dart Section 6 set (user flagged possible stack review; native-Android may not be appropriate as-is for android/ios/web).
  - `impeccable` engine binary downloads on first run (`npx impeccable install` best-effort per skill).
  - `awesome-agent-skills` is a registry, not a skill — not installed (tracked in Followups-Ledger as F-01).
  - Agent-scoped skills under `flutter/agent-plugins` `.agents/agents/reidbaker-agent/skills/` are agent-bundled, skipped from global install.
- **Followup IDs raised:** F-01, F-02.
- **Next up:** Sprint 1 micro-task S1.1.M01 (needs agent restart to pick up newly installed skills).

---