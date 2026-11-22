#!/usr/bin/env bash
# Timed fresh-volume PITR restore drill using wal-g (WP-03 S0.4.M3, 09-ENG §1/§7, 01A-ENG §6.1)
# Refuses to run against non-empty data directories; asserts RTO <= 4h (14400s) and PostGIS health.

set -euo pipefail

: "${RESTORE_PGDATA:?Set RESTORE_PGDATA to a dedicated fresh/empty restore directory}"
: "${TARGET_BACKUP:=LATEST}"
: "${RESTORE_PORT:=55432}"
: "${RTO_TARGET_SECONDS:=14400}"

if ! command -v wal-g >/dev/null 2>&1; then
  echo "ERROR: wal-g binary not found on PATH." >&2
  exit 1
fi
if [ -z "${WALG_S3_PREFIX:-}" ]; then
  echo "ERROR: WALG_S3_PREFIX must point to the off-provider/off-region archive." >&2
  exit 1
fi
if [ -e "${RESTORE_PGDATA}" ] && [ -n "$(ls -A "${RESTORE_PGDATA}" 2>/dev/null || true)" ]; then
  echo "ERROR: RESTORE_PGDATA (${RESTORE_PGDATA}) must be empty to avoid overwriting live data." >&2
  exit 1
fi

START_EPOCH="$(date +%s)"
mkdir -p "${RESTORE_PGDATA}"
chmod 700 "${RESTORE_PGDATA}"

echo "==> Fetching backup '${TARGET_BACKUP}' from ${WALG_S3_PREFIX}"
wal-g backup-fetch "${RESTORE_PGDATA}" "${TARGET_BACKUP}"

cat > "${RESTORE_PGDATA}/postgresql.auto.conf" <<EOF
restore_command = 'wal-g wal-fetch "%f" "%p"'
port = ${RESTORE_PORT}
EOF
touch "${RESTORE_PGDATA}/recovery.signal"

echo "==> Starting isolated PostgreSQL instance on port ${RESTORE_PORT} for recovery verification"
pg_ctl -D "${RESTORE_PGDATA}" -o "-p ${RESTORE_PORT}" -w start

cleanup() {
  pg_ctl -D "${RESTORE_PGDATA}" -m fast -w stop >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "==> Verifying PostGIS extension and pg_catalog census (relkind IN ('r','p'))"
psql -h 127.0.0.1 -p "${RESTORE_PORT}" -U "${PGUSER:-postgres}" -d "${PGDATABASE:-farmbasics}" -v ON_ERROR_STOP=1 -c "
SELECT PostGIS_Version() AS postgis_version;
SELECT n.nspname, count(*) AS table_count
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r','p') AND n.nspname NOT IN ('pg_catalog','information_schema')
GROUP BY n.nspname
ORDER BY n.nspname;
SELECT count(*) AS non_internal_triggers FROM pg_trigger WHERE NOT tgisinternal;
"

END_EPOCH="$(date +%s)"
ELAPSED=$(( END_EPOCH - START_EPOCH ))
echo "==> Measured RTO: ${ELAPSED}s (target <= ${RTO_TARGET_SECONDS}s)"
if [ "${ELAPSED}" -gt "${RTO_TARGET_SECONDS}" ]; then
  echo "WARNING: Restore exceeded 4-hour RTO target (${ELAPSED}s > ${RTO_TARGET_SECONDS}s)." >&2
  exit 1
fi

echo "OK: Restore drill passed within RTO target."
