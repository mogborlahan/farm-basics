# Farm Basics — Followups & Backlog Ledger

> **Purpose:** add-only ("append-only") record of **suggested followups** to implementation — debt, improvements, review notes, refactors, unanswered questions, and deferred work that surfaced mid-implementation. Entries are **never edited or deleted**; a status change is recorded as a new line or an appended status note referencing `F-##`.
>
> **Authority:** governed by `Plan\Farm-Basics-Implementation-Sprints.md` Preamble P0.7/P0.8 and the companion `Plan\sessions\Sprint-Journal.md`.
>
> **Status values:** `open` · `approved` · `in-progress` · `deferred` · `done` · `superseded` (a supersession or closure note must cite the replacing entry).
>
> **Entry template:**
>
> - **ID:** `F-##` (strictly sequential).
> - **Date:** YYYY-MM-DD.
> - **Source session:** Sprint-Journal entry that spawned the followup.
> - **Followup:** what is suggested and why.
> - **Trace:** originating sprints / source cites / guardrails (per P0.4, P0.5).
> - **Status:** one of the values above.
> - **Notes:** append-only status/decision notes.

---

## Ledger

- **ID:** F-01
- **Date:** 2026-09-23
- **Source session:** 2026-09-23 — Global skills install.
- **Followup:** Revisit `VoltAgent/awesome-agent-skills` (registry of 1000+ skills incl. Android Appium/Espresso/XCUITest, APK security scanners, Sentry SDK) once the mobile platform is confirmed — harvest the Appium/Espresso/XCUITest and APK-security set needed by the QA Master Test Plan.
- **Trace:** `Plan\Recommended-Skills.md` §5 (`awesome-agent-skills` row); QA Master Test Plan; [ENG 01 §8.1] platform decision.
- **Status:** open
- **Notes:** Registry ships no `SKILL.md`, so nothing to copy at install time.

- **ID:** F-02
- **Date:** 2026-09-23
- **Source session:** 2026-09-23 — Global skills install.
- **Followup:** Run `npx impeccable install` on first UI sprint to download the compiled Impeccable engine binary (`~/.impeccable\bin\`) so its 23 commands / live browser iteration / 61 detector rules become usable; then gate one DS2 screen through it as acceptance.
- **Trace:** `Plan\Recommended-Skills.md` §2 (`impeccable` row); DS2 tokens; [ENG 01 §8.1].
- **Status:** open
- **Notes:** Skill folder + launcher scripts installed; only the engine binary is deferred (downloads on first run).

- **ID:** F-03
- **Date:** 2026-09-23
- **Source session:** 2026-09-23 — Global skills install.
- **Followup:** Re-run an opencode session under this project to confirm the 141 installed skills resolve from `~/.agents/skills\` (restart needed), and confirm `interactive checkbox` state of the HTML pages survived the regenerate.
- **Trace:** Preamble P0.6; this session's Sprint-Journal entry.
- **Status:** open
- **Notes:** —