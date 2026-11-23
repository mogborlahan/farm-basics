import http from "k6/http";
import { check, sleep } from "k6";

// Sprint 10 WP-A1 (S10.2.M1) — Baseline Load Test against Doc/08-ENG §9 SLOs:
//   - Availability: < 0.5% HTTP failures (99.5% target, BRD NFR-AVAIL-01)
//   - Concurrency/API latency: p(99) < 2000ms (BRD NFR-PERF-04) & p(95) < 500ms
const BASE_URL = __ENV.BASE_URL || "http://localhost:8080";

export const options = {
  stages: [
    { duration: "15s", target: 10 },
    { duration: "30s", target: 25 },
    { duration: "15s", target: 0 }
  ],
  thresholds: {
    http_req_failed: ["rate<0.005"],
    http_req_duration: ["p(95)<500", "p(99)<2000"]
  }
};

export default function () {
  const liveRes = http.get(`${BASE_URL}/health/live`);
  check(liveRes, {
    "health/live returns 200": (r) => r.status === 200
  });

  const readyRes = http.get(`${BASE_URL}/health/ready`);
  check(readyRes, {
    "health/ready returns 200": (r) => r.status === 200
  });

  const cropsRes = http.get(`${BASE_URL}/v1/crop-profiles`);
  check(cropsRes, {
    "crop-profiles returns 200": (r) => r.status === 200
  });

  const statesRes = http.get(`${BASE_URL}/v1/geography/states`);
  check(statesRes, {
    "geography states returns 200": (r) => r.status === 200
  });

  sleep(0.5);
}
