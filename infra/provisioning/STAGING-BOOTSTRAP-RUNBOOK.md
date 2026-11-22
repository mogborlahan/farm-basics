# Staging VM, WireGuard, DNS & Off-Provider `wal-g` Bootstrap Runbook

This runbook clears the remaining infrastructure blockers gating Sprint 0 exit (`S0.1.M5`, `S0.4.M1`–`M3`, `S0.6.M3`) using synthetic data only (`AGENTS.md` Rule 19, `P12`).

---

## 1. Provision Ubuntu 24.04 LTS Staging VM (`P12` Synthetic Only)

1. Provision a single Ubuntu `24.04 LTS` x86_64 VM (minimum `4 vCPU`, `8 GB RAM`, `80 GB SSD`, no real farmer/NIN/financial data) from your chosen cloud provider.
2. Clone the repository into `/opt/farm-basics` on the VM and run [bootstrap-vm.sh](bootstrap-vm.sh) as root:
   ```bash
   sudo bash /opt/farm-basics/infra/provisioning/bootstrap-vm.sh
   ```

## 2. Enable WireGuard Admin Tunnel (`04A-ENG` §4.1)

1. Generate WireGuard server and operator keypairs:
   ```bash
   wg genkey | tee /etc/wireguard/server.key | wg pubkey > /etc/wireguard/server.pub
   chmod 600 /etc/wireguard/server.key
   ```
2. Create `/etc/wireguard/wg0.conf` (`10.200.0.1/24`, `ListenPort = 51820`), add your operator laptop peer public key, and bring it up:
   ```bash
   sudo systemctl enable --now wg-quick@wg0
   ```
3. Verify SSH works over the tunnel IP (`ssh <user>@10.200.0.1`), then remove public SSH access (`ufw allow in on wg0 to any port 22 proto tcp && ufw delete allow OpenSSH`).

## 3. Configure DNS & Cloudflare Edge (`D-10a` / `D-10b` Split)

Point four `A` (and optional `AAAA`) records to the staging VM IP:

| Hostname | Role | Cloudflare Proxy Toggle (`D-19`) |
|---|---|---|
| `api.<staging-domain>` | `API_DOMAIN` (Backend + Mobile API) | **DNS Only (Grey Cloud)** — `D-10b` OFF |
| `admin.<staging-domain>` | `ADMIN_DOMAIN` (Admin SPA + `oauth2-proxy`) | Proxied (`D-10a`) or DNS Only |
| `auth.<staging-domain>` | `AUTH_DOMAIN` (Keycloak OIDC) | DNS Only (Grey Cloud) |
| `grafana.<staging-domain>` | `GRAFANA_DOMAIN` (Ops metrics, VPN/Edge gated) | Proxied or VPN-only |

## 4. Provision Off-Provider `wal-g` Object Store & Secrets (`S0.4.M2`–`M3`)

1. Create an S3-compatible bucket in a **different provider and region** from the staging VM (e.g., Backblaze B2 or Cloudflare R2 if the VM is on DigitalOcean/Hetzner/AWS) with Object Lock / 35-day retention rules (`01A-ENG` §5.3, `09-ENG` §3).
2. Generate a `WALG_LIBSODIUM_KEY` (`openssl rand -hex 32`) for client-side backup encryption (`F-26(1)`), plus real staging secrets (`POSTGRES_PASSWORD`, `KEYCLOAK_ADMIN_PASSWORD`, `OAUTH2_PROXY_CLIENT_SECRET`, `OAUTH2_PROXY_COOKIE_SECRET`, `GRAFANA_ADMIN_PASSWORD`) and real domains (`API_DOMAIN`, `ADMIN_DOMAIN`, `AUTH_DOMAIN`, `GRAFANA_DOMAIN`, `OIDC_ISSUER_URL`, `AUTH_REQUIRE_HTTPS_METADATA=true`, `VITE_API_BASE_URL`, `VITE_KEYCLOAK_AUTHORITY`, `VITE_APP_URL`).
3. Update `infra/.env.sops.yaml` with `sops` (`SOPS_AGE_KEY_FILE` matching `~/.config/farm-basics/sops-age.txt` or a fresh staging `age` recipient in `infra/.sops.yaml`), and configure your real Alertmanager receiver webhook/SMTP in `infra/alertmanager/alertmanager.yml`.

## 5. Deploy & Execute M0 Gate Checks (`S0.1.M5`, `S0.4.M1`–`M3`, `S0.6.M3`)

1. **Blue/Green deploy (`S0.4.M1`, `S0.1.M5`):**
   ```bash
   cd /opt/farm-basics
   bash infra/deploy.sh
   API_DOMAIN=api.<staging-domain> ADMIN_DOMAIN=admin.<staging-domain> AUTH_DOMAIN=auth.<staging-domain> \
     bash infra/provisioning/verify-staging-m0.sh
   ```
2. **Off-provider `wal-g` backup & timed fresh-volume restore drill (`S0.4.M2`–`M3`, RTO `<= 4h`):**
   ```bash
   export WALG_S3_PREFIX="s3://<bucket>/farm-basics-staging"
   export WALG_LIBSODIUM_KEY="<32-byte-hex>"
   bash infra/backup/wal-g-backup.sh
   bash infra/backup/wal-g-restore-drill.sh
   ```
3. **End-to-end Admin Login (`S0.6.M3`):** Open `https://admin.<staging-domain>/` in Chrome, complete Keycloak PKCE login + MFA, confirm Grafana (`grafana.<staging-domain>`) shows `farmbasics_http_request_duration_seconds` histograms.
4. Tick `S0.1.M5`, `S0.4.M1`–`S0.4.M3`, and `S0.6.M3` in `Plan/Farm-Basics-Implementation-Sprints.md` and record the measured RTO in `Plan/sessions/Sprint-Journal.md`.

---

## 6. Zero-Cost Local Staging Track: Surface Pro 4 (`8 GB` RAM) + Tailscale + Backblaze B2

> **Full post-install playbook:** If you have already installed Ubuntu Server `24.04 LTS` on your Surface Pro 4, follow the copy-pasteable Phase 0–6 operator guide in [SURFACE-PRO4-PROVISIONING-GUIDE.md](SURFACE-PRO4-PROVISIONING-GUIDE.md).

Use this path when provisioning an existing **Surface Pro 4 (`8 GB` RAM, `x86_64`)** instead of a paid cloud VM (`$0`, no credit card required; **synthetic data only** per `P12` and Rule 19).

### 6.1 Surface Pro 4 Hardware & Ubuntu 24.04 LTS Server Setup

1. **Surface UEFI (`Volume Up` + `Power`):** Disable Secure Boot (only if needed by your USB installer) and enable **Battery Limit (Kiosk Mode, 50% max charge)** so leaving the device plugged in 24/7 prevents battery swelling.
2. Install **Ubuntu Server 24.04 LTS (`x86_64`)** and enable OpenSSH during setup.
3. Prevent sleep when the Type Cover is closed:
   ```bash
   sudo sed -i 's/^#*HandleLidSwitch=.*/HandleLidSwitch=ignore/' /etc/systemd/logind.conf
   sudo sed -i 's/^#*HandleLidSwitchExternalPower=.*/HandleLidSwitchExternalPower=ignore/' /etc/systemd/logind.conf
   sudo systemctl restart systemd-logind
   ```
4. Clone the repository into `/opt/farm-basics` and run [bootstrap-vm.sh](bootstrap-vm.sh) (`sudo bash /opt/farm-basics/infra/provisioning/bootstrap-vm.sh`).

### 6.2 `4 GB` Swapfile & Keycloak JVM Headroom

An `8 GB` host comfortably runs the entire Compose stack (~`2.5–3.5 GB` steady-state RAM), while a `4 GB` swapfile and an optional `JAVA_OPTS_APPEND` heap cap protect against OOM spikes during simultaneous `.NET` / Vite Docker image builds:

1. **Create a persistent `4 GB` swapfile with conservative swappiness:**
   ```bash
   sudo fallocate -l 4G /swapfile
   sudo chmod 600 /swapfile
   sudo mkswap /swapfile
   sudo swapon /swapfile
   echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
   echo 'vm.swappiness=10' | sudo tee /etc/sysctl.d/99-farmbasics-swap.conf
   sudo sysctl --system
   ```
2. **Optional Keycloak JVM heap cap:** If running parallel builds or `4 GB` constrained tests, create `infra/docker-compose.override.yml` (uncommitted local override) with:
   ```yaml
   services:
     keycloak:
       environment:
         JAVA_OPTS_APPEND: "-Xms256m -Xmx512m"
   ```

### 6.3 Tailscale Mesh VPN (Home/Mobile CGNAT Workaround)

Home and mobile ISPs typically place hosts behind CGNAT, blocking direct inbound `80`/`443` and WireGuard `51820/udp`. Tailscale's free personal plan traverses CGNAT cleanly:

1. **Install and connect Tailscale on both the Surface Pro 4 and your main Windows laptop:**
   ```bash
   curl -fsSL https://tailscale.com/install.sh | sh
   sudo tailscale up
   tailscale status
   ```
2. **Allow Tailscale interface traffic and restrict SSH to `tailscale0`:**
   ```bash
   sudo ufw allow in on tailscale0
   sudo ufw allow in on tailscale0 to any port 22 proto tcp
   sudo ufw delete allow OpenSSH
   sudo ufw reload
   ```
3. **Private HTTPS routing without public ports:** Enable **MagicDNS** + **HTTPS Certificates** in your Tailscale admin console, or map your dev hostnames (`api.staging.ts.net`, `admin.staging.ts.net`, `auth.staging.ts.net`, `grafana.staging.ts.net`) to the Surface's Tailscale IP (`100.x.y.z`) and enable `tailscale serve` (or Caddy internal TLS) so Keycloak OIDC (`AUTH_REQUIRE_HTTPS_METADATA=true`) works strictly inside your private tailnet.
   - *(Alternative if public HTTPS endpoints are needed for mobile APK testing off-tailnet: run a free **Cloudflare Tunnel (`cloudflared`)** for `admin` / `auth` and keep `api` access restricted to tailnet/allowlisted test IPs while `D-10b` stays OFF.)*

### 6.4 Off-Provider `wal-g` Backup on Backblaze B2 (`10 GB` Always Free, No Card)

1. Create a free Backblaze B2 account and a **Private** bucket (e.g., `farmbasics-staging-walg`) in a US or EU B2 region (distinct provider/location from your local Surface Pro 4, satisfying `01A-ENG` §5.3).
2. Create an Application Key scoped solely to that bucket (`Read and Write`), and note `keyID` (`AWS_ACCESS_KEY_ID`), `applicationKey` (`AWS_SECRET_ACCESS_KEY`), S3 Endpoint (`https://s3.<b2-region>.backblazeb2.com`), and region (e.g. `us-west-004` / `eu-central-003`).
3. Generate a client-side encryption key (`openssl rand -hex 32`) and run the first off-provider push + timed restore drill (`S0.4.M2`–`S0.4.M3`):
   ```bash
   export WALG_S3_PREFIX="s3://farmbasics-staging-walg/pg15"
   export AWS_ENDPOINT="https://s3.<b2-region>.backblazeb2.com"
   export AWS_S3_FORCE_PATH_STYLE="true"
   export AWS_REGION="<b2-region>"
   export AWS_ACCESS_KEY_ID="<b2-key-id>"
   export AWS_SECRET_ACCESS_KEY="<b2-application-key>"
   export WALG_LIBSODIUM_KEY="<32-byte-hex>"

   bash infra/backup/wal-g-backup.sh
   bash infra/backup/wal-g-restore-drill.sh
   ```
4. Once `deploy.sh`, `verify-staging-m0.sh`, `wal-g-backup.sh`, `wal-g-restore-drill.sh` (confirming RTO `<= 4h`), and browser Admin PKCE login succeed, tick **`S0.1.M5`**, **`S0.4.M1`–`S0.4.M3`**, and **`S0.6.M3`** in `Plan/Farm-Basics-Implementation-Sprints.md` and log the measured restore time in `Plan/sessions/Sprint-Journal.md`.
