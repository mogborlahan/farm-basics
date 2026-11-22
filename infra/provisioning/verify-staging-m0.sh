#!/usr/bin/env bash
# End-to-end M0 staging verification script (S0.1.M5, S0.6.M3, 04A-ENG §4.2)

set -euo pipefail

: "${API_DOMAIN:?Set API_DOMAIN (e.g. api.staging.farmbasics.ng)}"
: "${ADMIN_DOMAIN:?Set ADMIN_DOMAIN (e.g. admin.staging.farmbasics.ng)}"
: "${AUTH_DOMAIN:?Set AUTH_DOMAIN (e.g. auth.staging.farmbasics.ng)}"

fail=0

check_status() {
  local label="$1"
  local url="$2"
  local expected_regex="$3"
  local status
  status="$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 "${url}")"
  if [[ "${status}" =~ ${expected_regex} ]]; then
    echo "PASS [${label}] ${url} -> HTTP ${status}"
  else
    echo "FAIL [${label}] ${url} -> HTTP ${status} (expected ${expected_regex})" >&2
    fail=1
  fi
}

echo "==> Checking edge readiness & zero-trust rules"
check_status "API live" "https://${API_DOMAIN}/health/live" "^200$"

READY_BODY="$(curl -fsSL --max-time 15 "https://${API_DOMAIN}/health/ready" || true)"
if echo "${READY_BODY}" | grep -qi "ready" && echo "${READY_BODY}" | grep -qi "postgis"; then
  echo "PASS [API PostGIS ready] https://${API_DOMAIN}/health/ready"
else
  echo "FAIL [API PostGIS ready] unexpected body: ${READY_BODY}" >&2
  fail=1
fi

# 04A-ENG §4.2: /metrics must NEVER be exposed on the public edge.
check_status "Metrics hidden at edge" "https://${API_DOMAIN}/metrics" "^404$"

# Fail-closed Tier 2 forward_auth / JWT check on admin endpoints.
check_status "Admin API fail-closed" "https://${API_DOMAIN}/v1/admin/ping" "^(302|401)$"
check_status "Admin SPA reachable" "https://${ADMIN_DOMAIN}/" "^200$"
check_status "Keycloak OIDC discovery" "https://${AUTH_DOMAIN}/realms/farm-basics/.well-known/openid-configuration" "^200$"

if [ "${fail}" -ne 0 ]; then
  exit 1
fi

echo "OK: Automated M0 edge verification passed (complete manual Keycloak PKCE login in Admin Console for S0.6.M3)."
