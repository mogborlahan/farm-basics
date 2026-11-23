# Farm Basics — Load Baseline (`WP-A1` / `S10.2.M1`)

Verifies `Doc/08-ENG-Observability-and-Monitoring-Plan.md` §9 SLO targets (`99.5%` availability / `http_req_failed < 0.005` and `p(99) < 2000ms`) against a running Farm Basics API + PostgreSQL/PostGIS instance (never production — synthetic data only per `Rule 19`).

## Run Locally or Against Staging

```powershell
# Default target: http://localhost:8080
k6 run tests/load/k6-baseline.js

# Against Surface Pro 4 / staging Tailscale or Caddy URL
$env:BASE_URL = "https://api-staging.example.invalid"
k6 run tests/load/k6-baseline.js
```

## Full Concurrency Race Suite Reminder (`Doc/12-QA` §4.1, `Rule 4`)

Alongside `k6`, every parallel barrier-synchronised database contention harness runs in `FarmBasics.IntegrationTests` against live `postgis/postgis:15-3.5`:

- `ParallelRaceHarness` (`50` iterations × `4` threads) — `HealthAndConcurrencyIntegrationTests`, `SyncSpikeIdempotencyIntegrationTests`
- `50` iterations × `2` threads duplicate-NIN registration race — `Sprint2RegistrationIntegrationTests`
- `50` parallel open-field concurrent-cycle race — `Sprint7CropLifecycleIntegrationTests`
