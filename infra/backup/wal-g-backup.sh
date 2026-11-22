#!/usr/bin/env bash
# Continuous off-provider WAL/base backup helper via wal-g (01A-ENG §5.3/§6.3, 09-ENG §3, F-26(1))
# Requires WALG_S3_PREFIX targeting a provider/region separate from the primary VM
# and client-side encryption (WALG_LIBSODIUM_KEY or WALG_PGP_KEY_PATH).

set -euo pipefail

: "${PGDATA:=/var/lib/postgresql/data}"
: "${PGHOST:=localhost}"
: "${PGUSER:=postgres}"
: "${WALG_RETENTION_DAYS:=35}"

if ! command -v wal-g >/dev/null 2>&1; then
  echo "ERROR: wal-g binary not found on PATH." >&2
  exit 1
fi
if [ -z "${WALG_S3_PREFIX:-}" ]; then
  echo "ERROR: WALG_S3_PREFIX must point to an off-provider/off-region object store." >&2
  exit 1
fi
if [ -z "${WALG_LIBSODIUM_KEY:-}" ] && [ -z "${WALG_LIBSODIUM_KEY_PATH:-}" ] && [ -z "${WALG_PGP_KEY_PATH:-}" ]; then
  echo "ERROR: Client-side backup encryption key required (WALG_LIBSODIUM_KEY* or WALG_PGP_KEY_PATH per F-26(1))." >&2
  exit 1
fi

echo "==> Pushing full PostgreSQL base backup to ${WALG_S3_PREFIX}"
wal-g backup-push "${PGDATA}"

echo "==> Retaining ${WALG_RETENTION_DAYS} days of FULL + PITR WAL history (09-ENG §3)"
wal-g delete retain FULL "${WALG_RETENTION_DAYS}" --confirm

echo "==> Current remote backup inventory"
wal-g backup-list --detail
