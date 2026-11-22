# Farm Basics Infrastructure (`infra/`)

Implements `Doc/01A-ENG-Technology-Stack-and-Tooling-Plan.md` §5 (`D-08`, `D-10a`/`D-10b`, `D-13`, `D-17`–`D-21`) and `Doc/04A-ENG-Network-Access-and-Zero-Trust-Architecture.md`.

## Network & Security Posture

- **Zero-trust port publishing (`04A-ENG` §4.2):** `docker-compose.yml` publishes ports **only** on `caddy` (`80`, `443`). PostgreSQL, Prometheus, Alertmanager, Grafana, API, Keycloak, and `oauth2-proxy` publish zero ports.
- **Tier 2 gate (`D-21`):** `/v1/admin/*` and Grafana pass through Caddy `forward_auth` → `oauth2-proxy` → Keycloak (`fail-closed`). `/metrics` returns `404` at the public edge.
- **D-10a / D-10b split (`D-19`):** Static admin/web assets (`D-10a`) may sit behind Cloudflare CDN; the API proxy toggle (`D-10b`) stays **OFF** by default until counsel clears NDPA cross-border transfer (`P12`).
- **Observability retention (`01A` §8.1–§8.2):** Prometheus retains `30d` locally; Alertmanager routes off-VM (configure real webhook in SOPS before M0 exit).

## SOPS + `age` Secrets & Deployment (`D-13`, `WP-03`)

- **Encrypted secrets (`infra/.env.sops.yaml`):** Encrypted with `sops` (`3.13.3`) + `age` against the recipient in `infra/.sops.yaml`. The local non-production private key is stored outside the repository in ACL-locked `%LOCALAPPDATA%\Farm-Basics\sops-staging-age.txt`.
  ```powershell
  $env:SOPS_AGE_KEY_FILE = Join-Path $env:LOCALAPPDATA 'Farm-Basics\sops-staging-age.txt'
  sops --decrypt --input-type yaml --output-type yaml infra/.env.sops.yaml
  ```
- **Deployment & DR scripts (`WP-03`):**
  - `infra/deploy.sh` — decrypts `infra/.env.sops.yaml` into a `0600` temp file (cleaned via `trap EXIT`), builds/recreates containers, and verifies `/health/ready` (`bash -n` verified).
  - `infra/backup/wal-g-backup.sh` & `infra/backup/wal-g-restore-drill.sh` — `wal-g` 35-day retention backup push (`F-26(1)` client-side encryption enforced) and timed fresh-volume PITR restore drill asserting `PostGIS_Version()` + `pg_catalog` census within the 4-hour RTO (`09-ENG` §7).

## Remaining External Setup (`WP-03` Staging Gate)

Follow [`infra/provisioning/STAGING-BOOTSTRAP-RUNBOOK.md`](provisioning/STAGING-BOOTSTRAP-RUNBOOK.md) (with [`bootstrap-vm.sh`](provisioning/bootstrap-vm.sh) and [`verify-staging-m0.sh`](provisioning/verify-staging-m0.sh)):

1. Provision Ubuntu 24.04 LTS staging VM (`P12` synthetic data only) + WireGuard (`wg0`, `51820/udp`) + DNS records (`api/admin/auth/grafana`, Cloudflare proxy OFF on `api`).
2. Provision off-provider `wal-g` object storage target (`WALG_S3_PREFIX` + `WALG_LIBSODIUM_KEY`) and execute live `S0.1.M5`, `S0.4.M1`–`M3`, and `S0.6.M3` drills.
3. Replace placeholder webhook URL in `infra/alertmanager/alertmanager.yml` with your live off-VM channel and set `farm-basics-oauth2-proxy` confidential client secret in Keycloak at staging bootstrap.

## Validate Compose Schema Locally

```powershell
docker compose -f infra/docker-compose.yml --env-file infra/.env.example config --quiet
```
