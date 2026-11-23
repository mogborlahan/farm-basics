# Sprint 10 Accessibility Conformance & Low-Literacy Protocol (`WP-A3` / `Doc 14-QA` §3–§4)

**Version:** 1.0 (Sprint 10 — `S10.4.M1` & `S10.4.M2`)
**Sources:** `Doc/14-QA-Accessibility-Conformance-Plan.md` (§2–§4, §8), `Doc/Farm-Basics-Design-System-2.md` (§3.5, §6, §11.5, §19), `apps/admin/src/index.css`, `apps/farmer-app/lib/`

---

## 1. Automated WCAG 2.1 Contrast Verification (`S10.4.M1`)

Exact WCAG 2.1 relative-luminance ratios computed directly over the authoritative DS2 semantic tokens in `apps/admin/src/index.css` (and shared palette in `apps/farmer-app/lib/main.dart`):

| Token Pair | Foreground | Background | Ratio | WCAG AA (`4.5:1`) | WCAG AAA (`7.0:1`) | Surface Notes |
|---|---|---|---|---|---|---|
| `--color-ink-900` on `--color-cream` | `#1F2A22` | `#F4F1EA` | **13.18 : 1** | Pass | Pass | Primary outdoor high-sunlight body & canvas pair (exceeds `13.1:1` target) |
| `--color-ink-900` on White | `#1F2A22` | `#FFFFFF` | **14.87 : 1** | Pass | Pass | Admin cards, tables & dialog text |
| `--color-ink-700` on `--color-cream` | `#3D473F` | `#F4F1EA` | **8.57 : 1** | Pass | Pass | Secondary headings & labels on cream canvas |
| `--color-ink-700` on White | `#3D473F` | `#FFFFFF` | **9.67 : 1** | Pass | Pass | Helper captions inside white panels |
| `--color-ink-500` on White | `#6B7568` | `#FFFFFF` | **4.81 : 1** | Pass | Fail | Metadata / muted labels (kept at >= `4.8:1` on `#FFFFFF` only) |
| White on `--color-forest` | `#FFFFFF` | `#126A3A` | **6.67 : 1** | Pass | Fail | Primary CTA buttons & top AppBar |
| `--color-gold` on `--color-forest` | `#FDBB26` | `#126A3A` | **3.91 : 1** | Fail (`< 4.5`) | Fail | Large/graphical UI (`>= 3.0:1`); restricted to large bold monogram `"FB"` badge in `AdminShell.tsx`, never small text |
| White on `--color-surface-dark` | `#FFFFFF` | `#1F2A22` | **14.87 : 1** | Pass | Pass | Admin navigation sidebar active links |
| `--color-ink-300` on `--color-surface-dark` | `#A9AFA3` | `#1F2A22` | **6.62 : 1** | Pass | Fail | Admin navigation inactive items |
| `--color-clay` on White | `#B8471F` | `#FFFFFF` | **5.30 : 1** | Pass | Fail | Error alerts & negative deltas (paired with explicit text/status icon per WCAG 1.3.1) |

### Touch-Target & Color-Governance Scan (`S10.4.M1`)

- **Admin Console (`apps/admin/src/`):**
  - Zero ad-hoc inline hex colors across all `.tsx` components (`Select-String` verified clean; components strictly consume DS2 Tailwind tokens from `index.css`).
  - Primary interactive controls (`Sign in with Keycloak`, `Sign out`, `Apply Trust Override`) enforce `min-h-12` (`48px`), matching WCAG 2.5.5 and DS2 §19.
- **Mobile App (`apps/farmer-app/lib/`):**
  - Primary and secondary action buttons across `main.dart`, `registration_wizard_screen.dart`, `boundary_walk_screen.dart`, and `crop_task_list_screen.dart` enforce explicit `SizedBox(height: 48)` (`48dp`) and `SizedBox(height: 52)` (`52dp`) touch targets.
  - **Non-blocking remediation note:** `apps/farmer-app/lib/boundary/boundary_walk_screen.dart`, `crop_task_list_screen.dart`, and `registration_wizard_screen.dart` still declare local alert/border `Color(0x...)` literals (`0xFFFFF4D6`, `0xFFB45309`, `0xFFD6D0C4`, `0xFFB91C1C`, `0xFFFFF8E1`, `0xFFD9A300`, `0xFFFFEBEE`, `0xFFC62828`, `0xFFB71C1C`) instead of importing a single shared `FarmBasicsColors` DS2 token file. Tracked as severity **A4 (Minor)** for central token consolidation prior to Sprint 11 release polish.

---

## 2. Low-Literacy Icon-Comprehension & Voice-Mediated Test Pack (`S10.4.M2` / `Doc 14-QA` §4)

Because automated WCAG scanners cannot test whether a non-reading farmer understands an icon or spoken prompt (`Doc/14-QA` §1, §4), this protocol prepares the field research facilitator pack executed alongside the Sprint 11 / `Doc/13A-QA` pilot observations:

### A. Blind Icon-Only Comprehension Cards (`Doc 14-QA` §4.2)

For each card below, show **only the rendered Flutter icon on a phone screen (no text label)** to `>= 5` first-time non-reading participants per pilot language (`ha`, `yo`, `ig`, `pcm`, `en-NG`) and ask: *"Without tapping anything else, what do you think happens if you press this?"*

| Card ID | Screen | Action Under Test | Pass Criterion | Observed (`Pass / Misunderstood`) |
|---|---|---|---|---|
| `IC-01` | Home Shell | Self-Service Registration CTA | Participant associates symbol with signing up / identity card | Pending field session |
| `IC-02` | Home / Field | Start GPS Boundary Walk | Participant associates symbol with walking around farm edge | Pending field session |
| `IC-03` | Boundary Walk | Pause / Resume / Finish Walk | Distinctly understood as wait vs continue vs save | Pending field session |
| `IC-04` | Crop Tasks | OK vs Warning (`±10%`) vs Flag (`OUT_OF_BAND`) | Flag unmistakably recognized as "needs check / stop" without reading badge text | Pending field session |
| `IC-05` | Harvest / Sync | Offline Queued Indicator | Participant understands saved on phone, waiting for network | Pending field session |

### B. Voice-Mediated Screen-Off Drill (`Doc 14-QA` §4.3)

Verify that spoken prompts remain load-bearing without requiring reading of screen labels:
1. Agent-assisted registration VoiceOTP challenge: screen turned away from participant during OTP playback; confirm participant repeats or enters/confirms flow purely from audio cue.
2. Boundary walk audio + haptic cue (`WalkFeedbackSignal` tone + vibration in `boundary_walk_controller.dart`): tester completes a full 4-corner walk keeping eyes forward outdoors rather than staring at the screen.
3. Registration teach-back comprehension check (`Doc 13-QA` §7.2 / `11A-LEGAL`): participant answers the 3 teach-back questions unprompted in their spoken language.

### C. Severity Classification Rules (`Doc 14-QA` §8)

- **A1 (Blocking):** Participant cannot complete a core-path action (`register -> walk boundary -> log crop task`) or misinterprets a required icon with no audio/agent recovery -> **blocks pilot release**.
- **A2 (Severe):** High friction / hesitancy on frequent path requiring facilitator hints -> **blocks release absent documented mitigation**.
- **A3 (Moderate) / A4 (Minor):** Viable workaround or token-consolidation cleanup -> scheduled fix.
