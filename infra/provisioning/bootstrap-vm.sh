#!/usr/bin/env bash
# Ubuntu 24.04 LTS staging VM bootstrap & zero-trust hardening script
# References: Doc/01A-ENG §4-5, Doc/04A-ENG §4.1-4.2, Infra WP-03

set -euo pipefail

SOPS_VERSION="3.13.3"
SOPS_X86_64_SHA256="65c537e649f7b722354f8ee18f756a813c5750384369c5d3c563a02d6e595e5c"
DEPLOY_DIR="${DEPLOY_DIR:-/opt/farm-basics}"
DEPLOY_USER="${DEPLOY_USER:-${SUDO_USER:-$USER}}"

if [ "$(id -u)" -ne 0 ]; then
  echo "ERROR: Run bootstrap-vm.sh as root (sudo bash bootstrap-vm.sh)." >&2
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive

echo "==> Installing base hardening and transport packages"
apt-get update
apt-get install -y --no-install-recommends \
  ca-certificates curl gnupg jq age wireguard ufw fail2ban unattended-upgrades

if ! command -v docker >/dev/null 2>&1; then
  echo "==> Installing Docker Engine & Compose v2 plugin from official apt repository"
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  ARCH="$(dpkg --print-architecture)"
  CODENAME="$(. /etc/os-release && echo "${VERSION_CODENAME}")"
  echo "deb [arch=${ARCH} signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${CODENAME} stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update
  apt-get install -y --no-install-recommends docker-ce docker-ce-cli containerd.io docker-compose-plugin
fi

if id -u "${DEPLOY_USER}" >/dev/null 2>&1; then
  usermod -aG docker "${DEPLOY_USER}" || true
fi

if ! command -v sops >/dev/null 2>&1; then
  echo "==> Installing SOPS v${SOPS_VERSION} with checksum verification"
  TMP_DIR="$(mktemp -d)"
  trap 'rm -rf "${TMP_DIR}"' EXIT
  curl -fsSL -o "${TMP_DIR}/sops" \
    "https://github.com/getsops/sops/releases/download/v${SOPS_VERSION}/sops-v${SOPS_VERSION}.linux.amd64"
  curl -fsSL -o "${TMP_DIR}/sops.checksums.txt" \
    "https://github.com/getsops/sops/releases/download/v${SOPS_VERSION}/sops-v${SOPS_VERSION}.checksums.txt"
  EXPECTED_SUM="$(awk '/sops-v'"${SOPS_VERSION}"'\.linux\.amd64$/ {print $1}' "${TMP_DIR}/sops.checksums.txt" || echo "${SOPS_X86_64_SHA256}")"
  echo "${EXPECTED_SUM}  ${TMP_DIR}/sops" | sha256sum -c -
  install -m 0755 "${TMP_DIR}/sops" /usr/local/bin/sops
fi

echo "==> Configuring UFW firewall (80/443 HTTP/HTTPS, 51820/udp WireGuard)"
ufw --force reset
ufw default deny incoming
ufw default allow outgoing
ufw allow 80/tcp comment "Caddy ACME/HTTP redirect"
ufw allow 443/tcp comment "Caddy HTTPS edge (04A-ENG)"
ufw allow 51820/udp comment "WireGuard VPN admin tunnel"
ufw allow OpenSSH comment "Temporary bootstrap SSH — restrict to wg0 once VPN verified"
ufw --force enable

systemctl enable --now fail2ban

echo "==> Creating deployment directories and SOPS key location"
mkdir -p "${DEPLOY_DIR}"
if id -u "${DEPLOY_USER}" >/dev/null 2>&1; then
  USER_HOME="$(getent passwd "${DEPLOY_USER}" | cut -d: -f6)"
  mkdir -p "${USER_HOME}/.config/farm-basics"
  chmod 700 "${USER_HOME}/.config/farm-basics"
  chown -R "${DEPLOY_USER}:${DEPLOY_USER}" "${DEPLOY_DIR}" "${USER_HOME}/.config/farm-basics"
fi

cat <<'EOF'
OK: Staging VM bootstrap complete.

IMPORTANT next hardening steps before storing secrets:
1. Configure /etc/wireguard/wg0.conf and enable: sudo systemctl enable --now wg-quick@wg0
2. Verify SSH works over the WireGuard tunnel IP, then lock SSH to wg0 only:
     sudo ufw allow in on wg0 to any port 22 proto tcp
     sudo ufw delete allow OpenSSH
3. Place the staging age secret key in ~/.config/farm-basics/sops-age.txt (chmod 600).
EOF
