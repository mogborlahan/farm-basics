#!/usr/bin/env bash
# Blue/Green zero-downtime deployment script for single-VM Farm Basics staging/production
# References: 01A-ENG §5.4-5.6, 04A-ENG §4.2, 07-ENG §4.2, 09-ENG §3

set -euo pipefail
umask 077

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
COMPOSE_FILE="${SCRIPT_DIR}/docker-compose.yml"
SOPS_FILE="${SCRIPT_DIR}/.env.sops.yaml"

: "${SOPS_AGE_KEY_FILE:=${HOME}/.config/farm-basics/sops-age.txt}"
: "${HEALTH_URL:=http://127.0.0.1/health/ready}"
: "${ROLLOUT_TIMEOUT_SECONDS:=90}"

if ! command -v sops >/dev/null 2>&1; then
  echo "ERROR: sops CLI is required on PATH." >&2
  exit 1
fi
if ! command -v docker >/dev/null 2>&1; then
  echo "ERROR: docker CLI is required on PATH." >&2
  exit 1
fi
if [ ! -f "${SOPS_FILE}" ]; then
  echo "ERROR: Encrypted secrets file not found: ${SOPS_FILE}" >&2
  exit 1
fi
if [ ! -f "${SOPS_AGE_KEY_FILE}" ]; then
  echo "ERROR: age identity key not found: ${SOPS_AGE_KEY_FILE}" >&2
  exit 1
fi

TMP_ENV="$(mktemp "${TMPDIR:-/tmp}/farmbasics-env.XXXXXX")"
cleanup() {
  rm -f "${TMP_ENV}"
}
trap cleanup EXIT

export SOPS_AGE_KEY_FILE
sops --decrypt --input-type yaml --output-type dotenv "${SOPS_FILE}" > "${TMP_ENV}"
chmod 600 "${TMP_ENV}"

PREV_API_IMAGE="$(docker compose -f "${COMPOSE_FILE}" --env-file "${TMP_ENV}" images -q api 2>/dev/null || true)"

echo "==> Validating compose schema and starting postgres/keycloak dependencies"
docker compose -f "${COMPOSE_FILE}" --env-file "${TMP_ENV}" config --quiet
docker compose -f "${COMPOSE_FILE}" --env-file "${TMP_ENV}" up -d postgres keycloak

echo "==> Building application images from ${REPO_ROOT}"
docker compose -f "${COMPOSE_FILE}" --env-file "${TMP_ENV}" build api admin

echo "==> Starting Green application containers"
docker compose -f "${COMPOSE_FILE}" --env-file "${TMP_ENV}" up -d --no-deps --force-recreate api admin caddy oauth2-proxy

echo "==> Polling readiness probe (${HEALTH_URL}) for up to ${ROLLOUT_TIMEOUT_SECONDS}s"
DEADLINE=$(( $(date +%s) + ROLLOUT_TIMEOUT_SECONDS ))
READY=0
while [ "$(date +%s)" -lt "${DEADLINE}" ]; do
  if docker compose -f "${COMPOSE_FILE}" --env-file "${TMP_ENV}" exec -T api curl --fail --silent http://localhost:8080/health/ready >/dev/null 2>&1; then
    READY=1
    break
  fi
  sleep 3
done

if [ "${READY}" -ne 1 ]; then
  echo "ERROR: /health/ready did not pass within ${ROLLOUT_TIMEOUT_SECONDS}s." >&2
  docker compose -f "${COMPOSE_FILE}" --env-file "${TMP_ENV}" logs --tail=50 api >&2 || true
  if [ -n "${PREV_API_IMAGE}" ]; then
    echo "INFO: Previous API image ID was ${PREV_API_IMAGE}; execute tag rollback per 07-ENG §11." >&2
  fi
  exit 1
fi

echo "==> Ensuring observability stack is running"
docker compose -f "${COMPOSE_FILE}" --env-file "${TMP_ENV}" up -d prometheus alertmanager grafana

echo "OK: Deployment verified via /health/ready."
