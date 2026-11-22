# Surface Pro 4 (`8 GB` RAM) Staging Provisioning Guide (Ubuntu Server `24.04 LTS`)

Use this step-by-step operator playbook once **Ubuntu Server `24.04 LTS` (`x86_64`)** is installed on your **Surface Pro 4 (`8 GB` RAM)**. It turns the machine into a zero-cost (`$0`), Tailscale-protected Farm Basics staging host backed up to Backblaze B2 (`10 GB` free tier) and walks through closing the remaining M0 infrastructure gates (**`S0.1.M5`**, **`S0.4.M1`–`S0.4.M3`**, **`S0.6.M3`**, tracked as **`F-32`**).

> [!CAUTION]
> **Synthetic data only (`Doc/AGENTS.md` Rule 19, `PRECEDENCE.md` `P12`).** Never place real farmer identities, real NINs, real phone numbers, or real bank/production credentials on this host.

---

## Prerequisites Check Before Starting

- Surface Pro 4 connected to AC power and home LAN/Wi-Fi with OpenSSH running (`ssh <user>@<surface-lan-ip>` works from your main Windows laptop).
- Free **Tailscale** personal account ready (for mesh VPN across home/mobile CGNAT).
- Free **Backblaze B2** account ready (`10 GB` always free, no card required, for off-provider `wal-g` backups).
- Staging `age` private key available on your Windows workstation (`%LOCALAPPDATA%\Farm-Basics\sops-staging-age.txt`), matching the public recipient in [infra/.sops.yaml](../.sops.yaml).

---

## Phase 0 — Surface Pro 4 Hardware, Power & `4 GB` Swap Hardening

Run all commands below inside your SSH session on the Surface Pro 4.

1. **Verify OS, architecture, RAM, and disk space:**
   ```bash
   lsb_release -a
   uname -m
   free -h
   df -h /
   ```
   Confirm `Ubuntu 24.04` (`x86_64`) and ~`8 GiB` RAM.

2. **Confirm UEFI Battery Limit (Kiosk Mode, 50% max charge):**
   Because a Surface Pro 4 stays plugged in 24/7 as a server, ensure UEFI **Battery Limit** is enabled (`Volume Up` + `Power` during boot -> *Boot configuration* -> *Advanced options* -> *Enable Battery Limit Mode* ON) to prevent lithium battery swelling.

3. **Disable sleep when the Type Cover / lid is closed:**
   ```bash
   sudo sed -i 's/^#*HandleLidSwitch=.*/HandleLidSwitch=ignore/' /etc/systemd/logind.conf
   sudo sed -i 's/^#*HandleLidSwitchExternalPower=.*/HandleLidSwitchExternalPower=ignore/' /etc/systemd/logind.conf
   grep -q '^HandleLidSwitch=ignore' /etc/systemd/logind.conf || echo 'HandleLidSwitch=ignore' | sudo tee -a /etc/systemd/logind.conf
   grep -q '^HandleLidSwitchExternalPower=ignore' /etc/systemd/logind.conf || echo 'HandleLidSwitchExternalPower=ignore' | sudo tee -a /etc/systemd/logind.conf
   sudo systemctl restart systemd-logind
   ```
   Verify with `grep -E '^HandleLidSwitch' /etc/systemd/logind.conf`.

4. **Create a persistent `4 GB` swapfile and set `vm.swappiness=10`:**
   Steady-state Compose usage (`postgres`, `keycloak`, `api`, `admin`, `caddy`, `oauth2-proxy`, `prometheus`, `alertmanager`, `grafana`) is ~`2.5–3.5 GB`, while `dotnet build` / `vite build` inside `deploy.sh` can spike RAM.
   ```bash
   sudo fallocate -l 4G /swapfile
   sudo chmod 600 /swapfile
   sudo mkswap /swapfile
   sudo swapon /swapfile
   grep -q '^/swapfile ' /etc/fstab || echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
   echo 'vm.swappiness=10' | sudo tee /etc/sysctl.d/99-farmbasics-swap.conf
   sudo sysctl --system
   ```
   **Verify:** `free -h && swapon --show && sysctl vm.swappiness` shows a `4G` `/swapfile` and `vm.swappiness = 10`.

---

## Phase 1 — Put the Repository in `/opt/farm-basics` & Run `bootstrap-vm.sh`

1. **Install `git` (and OpenSSL/utilities if absent) and stage the repository in `/opt/farm-basics`:**
   ```bash
   sudo apt-get update
   sudo apt-get install -y --no-install-recommends git openssl
   ```
   Then transfer or clone the repository into `/opt/farm-basics`:
   - **If pulling from your Git remote/LAN mirror:**
     ```bash
     sudo mkdir -p /opt/farm-basics
     sudo chown "$USER":"$USER" /opt/farm-basics
     git clone <your-repo-url> /opt/farm-basics
     ```
   - **Or copy from your Windows workstation via `scp` (e.g., if keeping everything local-only without a Git remote):**
     Run from Windows PowerShell, excluding build outputs:
     ```powershell
     ssh <user>@<surface-lan-ip> "sudo mkdir -p /opt/farm-basics && sudo chown `$USER:`$USER /opt/farm-basics"
     scp -r C:\source\repos\Farm-Basics\infra C:\source\repos\Farm-Basics\apps C:\source\repos\Farm-Basics\tests C:\source\repos\Farm-Basics\FarmBasics.sln C:\source\repos\Farm-Basics\Directory.Build.props C:\source\repos\Farm-Basics\Directory.Packages.props C:\source\repos\Farm-Basics\NuGet.Config C:\source\repos\Farm-Basics\.dockerignore <user>@<surface-lan-ip>:/opt/farm-basics/
     ```

2. **Run [bootstrap-vm.sh](bootstrap-vm.sh) as root:**
   This installs Docker Engine + Compose v2, `age`, `sops v3.13.3` (SHA-256 verified), `ufw`, `fail2ban`, `unattended-upgrades`, adds your user to the `docker` group, creates `~/.config/farm-basics` (`0700`), and applies base firewall rules (`80/tcp`, `443/tcp`, `51820/udp`, temporary `OpenSSH`).
   ```bash
   sudo bash /opt/farm-basics/infra/provisioning/bootstrap-vm.sh
   ```

3. **Activate `docker` group membership and verify installed tools:**
   Log out and back in over SSH (or run `newgrp docker` in your active session), then verify:
   ```bash
   docker --version
   docker compose version
   sops --version
   age --version
   sudo ufw status verbose
   sudo systemctl is-active fail2ban docker
   ```

---

## Phase 2 — Tailscale Mesh VPN & SSH Lockdown (Home/Mobile CGNAT)

Because home and mobile ISPs typically place hosts behind CGNAT (blocking unsolicited inbound `80`/`443` and WireGuard `51820/udp`), Tailscale gives private encrypted SSH and service access without router port forwarding.

1. **Install and connect Tailscale on the Surface Pro 4:**
   ```bash
   curl -fsSL https://tailscale.com/install.sh | sh
   sudo tailscale up
   tailscale status
   tailscale ip -4
   ```
   Complete the browser authentication URL printed by `sudo tailscale up`. Note your Surface Tailscale IPv4 address (`100.x.y.z`).

2. **Install Tailscale on your main Windows laptop** and sign into the **same** tailnet.

3. **Test SSH over Tailscale BEFORE changing firewall rules:**
   Open a **second** terminal on your Windows laptop and connect via the Tailscale IP:
   ```powershell
   ssh <user>@<surface-tailscale-ip>
   ```
   Keep your original session open until this second Tailscale SSH login succeeds.

4. **Restrict SSH strictly to `tailscale0` (`ufw`):**
   ```bash
   sudo ufw allow in on tailscale0
   sudo ufw allow in on tailscale0 to any port 22 proto tcp
   sudo ufw delete allow OpenSSH
   sudo ufw delete allow 51820/udp || true
   sudo ufw reload
   sudo ufw status numbered
   ```
   Confirm `22/tcp` is allowed **only** on `tailscale0` (never public `Anywhere`).

5. **Choose private HTTPS hostname resolution for the 4 Caddy virtual hosts:**
   [infra/caddy/Caddyfile](../caddy/Caddyfile) routes four distinct hostnames on ports `80`/`443`: `API_DOMAIN`, `ADMIN_DOMAIN`, `AUTH_DOMAIN`, and `GRAFANA_DOMAIN`. Pick one of two patterns:
   - **Pattern A — Tailnet-only (`$0`, no public DNS exposure):**
     In your Tailscale Admin Console, enable **MagicDNS** + **HTTPS Certificates**. Since a single Tailscale node gets one `<node>.<tailnet>.ts.net` name natively, map four staging hostnames (e.g., `api.staging.farmbasics.local`, `admin.staging.farmbasics.local`, `auth.staging.farmbasics.local`, `grafana.staging.farmbasics.local`) to your Surface's Tailscale IP (`100.x.y.z`) in your laptop's `hosts` file (`C:\Windows\System32\drivers\etc\hosts`) **and** add `tls internal` inside `infra/caddy/Caddyfile` (or keep a real subdomain + Cloudflare DNS-01 challenge) and trust Caddy's root certificate on your laptop.
   - **Pattern B — Public domain + Cloudflare (`$0` Cloudflare Free / `cloudflared` behind CGNAT):**
     If you have a staging domain on Cloudflare and need off-tailnet mobile APK tests, keep `API_DOMAIN` Grey-Cloud / tunnelled per `D-10b` OFF (`infra/caddy/Caddyfile` handles origin headers and `D-19`), route via `cloudflared` across CGNAT, and restrict admin/Grafana (`OAUTH2_PROXY` + Tailscale ACLs or Cloudflare Access).

   > [!IMPORTANT]
   > Whatever hostnames you choose for `API_DOMAIN`, `ADMIN_DOMAIN`, `AUTH_DOMAIN`, and `GRAFANA_DOMAIN`, ensure `infra/keycloak/realm-farm-basics.json` redirect URIs / web origins and `AUTH_REQUIRE_HTTPS_METADATA=true` match them before running `verify-staging-m0.sh`.

---

## Phase 3 — Secrets (`SOPS` + `age`) & `8 GB` RAM Compose Tuning

1. **Install the staging `age` private key onto the Surface Pro 4:**
   From your **Windows workstation**, copy the non-production key generated during Sprint 0 to the Surface over Tailscale SSH:
   ```powershell
   scp $env:LOCALAPPDATA\Farm-Basics\sops-staging-age.txt <user>@<surface-tailscale-ip>:~/.config/farm-basics/sops-age.txt
   ```
   Back on the **Surface Pro 4**, enforce strict `0600` permissions and confirm the public key matches [infra/.sops.yaml](../.sops.yaml) (`age1rz9vvpuj3z8w75vpnm76tr7j08fm9y2rsqn9263u4muatcrk6pkqzedl7z`):
   ```bash
   chmod 700 ~/.config/farm-basics
   chmod 600 ~/.config/farm-basics/sops-age.txt
   age-keygen -y ~/.config/farm-basics/sops-age.txt
   ```

2. **Generate random staging secrets (do not reuse defaults from `.env.example`):**
   ```bash
   echo "PG_PASS=$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 28)"
   echo "KC_PASS=$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 28)"
   echo "OIDC_SECRET=$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 32)"
   # oauth2-proxy requires an exact 32-byte URL-safe base64 cookie secret (44 chars)
   echo "COOKIE_SECRET=$(dd if=/dev/urandom bs=32 count=1 2>/dev/null | base64 | tr '+/' '-_')"
   echo "GRAFANA_PASS=$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 28)"
   ```

3. **Create a temporary plaintext YAML file matching [infra/.env.example](../.env.example), encrypt it with `sops`, and shred the plaintext:**
   ```bash
   cat > /tmp/farmbasics-staging.env.yaml <<'EOF'
   ASPNETCORE_ENVIRONMENT: Staging
   API_DOMAIN: api.<your-staging-domain>
   ADMIN_DOMAIN: admin.<your-staging-domain>
   AUTH_DOMAIN: auth.<your-staging-domain>
   GRAFANA_DOMAIN: grafana.<your-staging-domain>
   OIDC_ISSUER_URL: https://auth.<your-staging-domain>/realms/farm-basics
   AUTH_REQUIRE_HTTPS_METADATA: "true"
   VITE_API_BASE_URL: https://api.<your-staging-domain>
   VITE_KEYCLOAK_AUTHORITY: https://auth.<your-staging-domain>/realms/farm-basics
   VITE_KEYCLOAK_CLIENT_ID: farm-basics-admin
   VITE_APP_URL: https://admin.<your-staging-domain>
   POSTGRES_USER: farmbasics_app
   POSTGRES_PASSWORD: <PG_PASS>
   KEYCLOAK_ADMIN_USER: kc_bootstrap_admin
   KEYCLOAK_ADMIN_PASSWORD: <KC_PASS>
   OAUTH2_PROXY_CLIENT_SECRET: <OIDC_SECRET>
   OAUTH2_PROXY_COOKIE_SECRET: <COOKIE_SECRET>
   GRAFANA_ADMIN_USER: grafana_ops
   GRAFANA_ADMIN_PASSWORD: <GRAFANA_PASS>
   EOF
   ```
   Encrypt and immediately destroy the temporary file:
   ```bash
   cd /opt/farm-basics
   sops --config infra/.sops.yaml --encrypt --input-type yaml --output-type yaml /tmp/farmbasics-staging.env.yaml > infra/.env.sops.yaml
   shred -u /tmp/farmbasics-staging.env.yaml 2>/dev/null || rm -f /tmp/farmbasics-staging.env.yaml
   ```
   **Verify round-trip SOPS decryption without printing secrets to terminal logs:**
   ```bash
   SOPS_AGE_KEY_FILE=~/.config/farm-basics/sops-age.txt \
     sops --decrypt --input-type yaml --output-type dotenv infra/.env.sops.yaml | wc -l
   ```
   Should output `19` (matching the 19 non-comment lines in `infra/.env.example`). Also set `infra/alertmanager/alertmanager.yml` to a real test webhook/SMTP receiver before staging go-live.

4. **Optional Keycloak JVM heap cap (`infra/docker-compose.override.yml`, kept uncommitted):**
   To leave generous memory headroom during concurrent builds on `8 GB` RAM:
   ```bash
   cat > /opt/farm-basics/infra/docker-compose.override.yml <<'EOF'
   services:
     keycloak:
       environment:
         JAVA_OPTS_APPEND: "-Xms256m -Xmx512m"
   EOF
   ```

---

## Phase 4 — Blue/Green Deploy & M0 Edge Verification (`S0.1.M5`, `S0.4.M1`, `S0.6.M3`)

1. **Deploy the stack with [infra/deploy.sh](../deploy.sh):**
   ```bash
   cd /opt/farm-basics
   bash infra/deploy.sh
   ```
   `deploy.sh` safely decrypts `infra/.env.sops.yaml` into a `0600` temp file with `trap cleanup EXIT`, validates `docker compose config --quiet`, brings up `postgres` + `keycloak`, builds `api` + `admin`, recreates `api`, `admin`, `caddy`, and `oauth2-proxy`, waits up to `90s` for `/health/ready`, and starts `prometheus`, `alertmanager`, and `grafana`.

   **Verify container health:**
   ```bash
   docker compose -f /opt/farm-basics/infra/docker-compose.yml ps
   ```

2. **Run automated M0 edge verification (`S0.1.M5`) via [verify-staging-m0.sh](verify-staging-m0.sh):**
   ```bash
   API_DOMAIN=api.<your-staging-domain> \
   ADMIN_DOMAIN=admin.<your-staging-domain> \
   AUTH_DOMAIN=auth.<your-staging-domain> \
   bash /opt/farm-basics/infra/provisioning/verify-staging-m0.sh
   ```
   Confirm all 6 checks print `PASS`:
   - `https://<API_DOMAIN>/health/live` -> `200`
   - `https://<API_DOMAIN>/health/ready` -> body includes `ready` and `postgis`
   - `https://<API_DOMAIN>/metrics` -> `404` (Prometheus scrape endpoint hidden from public edge, `04A-ENG` §4.2)
   - `https://<API_DOMAIN>/v1/admin/ping` -> `302` or `401` (fail-closed)
   - `https://<ADMIN_DOMAIN>/` -> `200`
   - `https://<AUTH_DOMAIN>/realms/farm-basics/.well-known/openid-configuration` -> `200`

3. **Manual Admin PKCE Login & Grafana Histogram Check (`S0.6.M3`):**
   - From your Windows laptop browser (connected to Tailscale), open `https://<ADMIN_DOMAIN>/` and sign in via Keycloak PKCE + MFA.
   - Open `https://<GRAFANA_DOMAIN>/` (protected by `oauth2-proxy` forward-auth), log in with your `GRAFANA_ADMIN_USER`, and verify `farmbasics_http_request_duration_seconds` histograms appear after hitting `/health/ready` and `/v1/auth/me`.

---

## Phase 5 — Off-Provider `wal-g` Backup & Timed Restore Drill on Backblaze B2 (`S0.4.M2`–`S0.4.M3`)

1. **Create your Backblaze B2 bucket and scoped key (`01A-ENG` §5.3, `09-ENG` §3):**
   - Create a free Backblaze B2 account (`10 GB` always free, zero card).
   - Create a **Private** bucket (e.g., `farmbasics-staging-walg`) in a B2 region (e.g., `us-west-004` or `eu-central-003`) with 35-day Object Lock / lifecycle retention rules.
   - Create an **Application Key** restricted strictly to `farmbasics-staging-walg` (`Read and Write`). Record:
     - `keyID` (`AWS_ACCESS_KEY_ID`)
     - `applicationKey` (`AWS_SECRET_ACCESS_KEY`)
     - Endpoint (`https://s3.<b2-region>.backblazeb2.com`) and region (`<b2-region>`).

2. **Install `wal-g` (`wal-g-pg`) + PostgreSQL 15 CLI/server utilities for the backup & isolated restore drill:**
   [wal-g-backup.sh](../backup/wal-g-backup.sh) and [wal-g-restore-drill.sh](../backup/wal-g-restore-drill.sh) invoke `wal-g`, `pg_ctl`, and `psql` directly against a specified `PGDATA` / `RESTORE_PGDATA` path. Ensure the `wal-g` PostgreSQL release binary (`wal-g-pg-ubuntu-24.04-amd64` / official release tarball matching SHA-256) and `postgresql-15` tools are available on `PATH` (or execute the drill inside a PostgreSQL 15 container with the `pgdata` volume mounted).

3. **Generate your client-side encryption key (`F-26(1)`):**
   ```bash
   export WALG_LIBSODIUM_KEY="$(openssl rand -hex 32)"
   ```
   Store `WALG_LIBSODIUM_KEY` securely off-host (without this key, B2 backups cannot be decrypted).

4. **Push a base backup and execute the timed isolated restore drill (`RTO <= 4h`):**
   ```bash
   export WALG_S3_PREFIX="s3://farmbasics-staging-walg/pg15"
   export AWS_ENDPOINT="https://s3.<b2-region>.backblazeb2.com"
   export AWS_S3_FORCE_PATH_STYLE="true"
   export AWS_REGION="<b2-region>"
   export AWS_ACCESS_KEY_ID="<b2-key-id>"
   export AWS_SECRET_ACCESS_KEY="<b2-application-key>"
   export PGUSER="farmbasics_app"
   export PGDATABASE="farmbasics"

   bash /opt/farm-basics/infra/backup/wal-g-backup.sh

   export RESTORE_PGDATA="/tmp/farmbasics-restore-test"
   bash /opt/farm-basics/infra/backup/wal-g-restore-drill.sh
   rm -rf /tmp/farmbasics-restore-test
   ```
   **Verify output:** `wal-g-restore-drill.sh` confirms `PostGIS_Version()`, schema table counts, `non_internal_triggers`, and `OK: Restore drill passed within RTO target` (`ELAPSED <= 14400s`).

---

## Phase 6 — Close M0 Gates & Update Trackers

Once Phases 0–5 succeed end to end:

1. Tick **`S0.1.M5`**, **`S0.4.M1`**, **`S0.4.M2`**, **`S0.4.M3`**, and **`S0.6.M3`** in `Plan/Farm-Basics-Implementation-Sprints.md` and run `Plan/regenerate.ps1` on Windows.
2. Mark **`F-32`** as **`done`** in `Plan/sessions/Followups-Ledger.md` and log measured `wal-g-restore-drill.sh` RTO + verification output in `Plan/sessions/Sprint-Journal.md` (`git -C Plan/sessions commit`).
3. Append the completion entry to `Plan/progress/PROGRESS.md`.
