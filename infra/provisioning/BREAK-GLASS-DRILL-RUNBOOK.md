# Farm Basics — Break-Glass Ceremony & Drill Runbook (WP-B2 / S11.3.M1)

**Authority:** `Doc/04A-ENG-Network-Access-and-Zero-Trust-Architecture.md` (§6, §11.1, §11.4, §11.7), `Doc/PRD-Volume-D-Admin-Operations.md` (§4.15 `FR-SC-003` / `BR-D-08`), `apps/backend/src/FarmBasics.Api/Persistence/Migrations/009_sprint11_break_glass.sql`, `apps/backend/src/FarmBasics.Api/Observability/BreakGlassEndpoints.cs`  
**Audience:** `SuperAdmin` custody holders, Incident Commander

---

## 1. Hard Governance Rules

1. **Zero silent expiration:** A break-glass action remains in `pending_review` indefinitely until an independent second `SuperAdmin` reviews it. `idx_break_glass_pending_due` tracks actions older than `review_due_at` (`invoked_at + 48h`) and Grafana/metrics surface overdue reviews (`isOverdue = true` on `GET /v1/admin/break-glass`).
2. **Structurally enforced distinct reviewer (`FR-SC-003` / `BR-D-08`):**
   - Application layer (`BreakGlassEndpoints.HandleReviewAsync`) returns HTTP `403` with `code = "SELF_APPROVAL_BLOCKED"` when `invoked_by_admin_id == reviewerId`.
   - PostgreSQL constraint `chk_break_glass_distinct_reviewer` (`reviewed_by_admin_id IS NULL OR reviewed_by_admin_id <> invoked_by_admin_id`) rejects self-review even under direct SQL connection (`SQLSTATE 23514`).
3. **No secrets in plaintext:** Break-glass root/emergency SSH & DB recovery credentials are sealed offline using `age` (`Doc/04A-ENG` §11.7) across separated custody holders; plaintext credentials exist only during active incident mitigation and must be rotated + re-sealed immediately afterwards.

---

## 2. Offline `age` Sealing Ceremony (`Doc/04A-ENG` §11.7)

1. Generate two independent hardware/offline `age` identities (`superadmin-a` and `superadmin-b`, or founder primary + air-gapped escrow USB in safe custody).
2. Encrypt the break-glass emergency credential bundle using both recipients so recovery can be audited:
   ```bash
   age -R breakglass-recipients.txt -o breakglass-bundle.age breakglass-secrets.txt
   shred -u breakglass-secrets.txt
   ```
3. Verify ciphertext integrity without leaving plaintext on disk:
   ```bash
   age -d -i <holder-identity.txt> breakglass-bundle.age | wc -l
   ```
4. Immediately after any real invocation OR quarterly drill that decrypts `breakglass-bundle.age`:
   - Rotate the exposed SSH key / DB emergency password / SOPS secret.
   - Re-seal a new `breakglass-bundle.age` and bump its ceremony serial number in the post-incident record.

---

## 3. Emergency Execution & Mandatory Retroactive Log (`Keycloak Down` / `P1`)

Per `Doc/04A-ENG` §11.4, when Keycloak is unreachable during a P1 incident, operators must not bypass accountability:

1. Connect over WireGuard + Type 1 break-glass SSH (`Doc/04A-ENG` §11.1) using the decrypted ceremony credential.
2. Perform only the minimum stabilizing action (e.g., restart Keycloak/Postgres container, apply emergency firewall rule, verify `wal-g` WAL stream). Record exact UTC timestamps and shell actions in incident notes.
3. As soon as API/Keycloak availability returns (and within the 48-hour SLA), invoking `SuperAdmin` submits:
   - `POST /v1/admin/break-glass` with `{ "actionDescription": "...", "justification": "...", "retroactive": true }` (`break_glass:invoke` permission required).
   - Server writes `admin.break_glass_action` (`status = 'pending_review'`, `review_due_at = now() + interval '48 hours'`) and immutable `admin.break_glass_invoked` audit record.

---

## 4. Mandatory Second-SuperAdmin Review & Quarterly Drill Procedure

1. Second distinct `SuperAdmin` lists pending reviews via `GET /v1/admin/break-glass` (`break_glass:review` permission).
2. Reviewer compares `actionDescription` and `justification` against VM SSH auth logs and incident timeline, then submits:
   - `POST /v1/admin/break-glass/{actionId}/review` with `{ "notes": "Verified against VM auth log & rotated credentials." }`.
   - Server transitions row to `status = 'reviewed'`, sets `reviewed_by_admin_id` & `reviewed_at`, and appends `admin.break_glass_reviewed` to `audit.audit_log_entry`.
3. **Solo-founder / pre-team pilot caveat (`Doc/PRECEDENCE.md`):** If only one human founder holds operational custody during Stage 0–1 pilot, break-glass drills still verify the API/SQL `403 SELF_APPROVAL_BLOCKED` + `23514` invariant in automated/staging tests (`Sprint11PilotTelemetryAndBreakGlassIntegrationTests`), and any production Type 1 break-glass event is escalated via designated external trustee/advisory `SuperAdmin` keyholder rather than weakening DB constraints.
