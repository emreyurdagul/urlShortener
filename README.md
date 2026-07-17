# URL Shortener — from-scratch API Gateway

A URL shortener ecosystem built to showcase an API gateway written **from scratch** on Kestrel — no YARP, no off-the-shelf proxy. The gateway does path-based routing, round-robin load balancing across service replicas, hop-by-hop header handling, `X-Forwarded-*` propagation and backend timeouts, with health checks, failover, metrics and autoscaling arriving in later phases.

## Current state (Phase 4)

```
                   ┌──► auth-service ───────┐
client ──► gateway ─┼──► link-service ×3 ────┼──► postgres
   (:8080)  │      └──► analytics-service ◄──┘        │
        JWT auth        ▲   (async click batches)     ▼
      + rate limit      │                        prometheus ──► grafana
      + RED metrics     └── redirect hot path enqueues clicks (non-blocking)
```

- `auth-service` — register/login, Identity password hashing, HMAC-signed JWTs carrying the user's plan; a mock `POST /api/auth/plan` upgrades tiers (real payment lands in phase 6).
- **Gateway auth** — validates the bearer token and injects trusted `X-User-Id`/`X-User-Plan` headers for backends, **stripping any client-supplied copies first** so those headers are trustworthy by construction. A presented-but-invalid token is rejected with 401.
- **Rate limiting** — a from-scratch per-client token bucket (keyed by user id, or IP when anonymous); over-limit requests get 429 + `Retry-After` and never reach a backend.
- **Plan quota** — link-service enforces a per-plan link quota (free = 50, premium = unlimited) using the trusted plan header; over-quota creation returns 403.
- `analytics-service` — ingests click batches and serves per-code stats. link-service records clicks off the redirect hot path via a bounded `Channel<T>` + background batch shipper, so redirects never wait on analytics (and drop rather than block if the buffer fills).

- `Gateway` — hand-written reverse proxy: config-driven route table, `RoundRobinPool`, streamed request/response bodies, RFC 9110 hop-by-hop header stripping.
- **Health checks & failover** — a background monitor probes every backend's `/health` on an interval and takes unhealthy replicas out of rotation; on a connection-level failure the proxy ejects the backend immediately and retries the request on the next healthy one (bodyless requests only — a consumed body can't be replayed). Recovered backends rejoin automatically.
- `LinkService` — minimal API: create short links (`POST /api/links`, selectable code length 5–8, multi-domain aware) and redirect (`GET /{code}` → 302). Postgres via Dapper, uniqueness enforced by a `(domain, code)` constraint.
- **Observability** — the gateway exports RED metrics from its own proxy loop (`gateway_requests_total`, `gateway_request_duration_seconds` histogram, `gateway_backend_healthy`, `gateway_failovers_total`); Prometheus scrapes the gateway and each replica, Grafana ships with a provisioned RED dashboard (rate, error ratio, p50/p95/p99, backend health, failovers) at `localhost:3000`.

## Numbers (k6, 20 VUs × 30s, local podman)

| metric | value |
|---|---|
| throughput | **5 753 req/s** (172 633 requests, 0 failed) |
| latency (client-side) | med 3.06 ms · p95 6.14 ms · max 27.4 ms |
| latency (gateway-side histogram) | p50 1.86 ms · p95 4.68 ms · p99 8.14 ms |
| failover | 50/50 requests kept returning 302 while a replica was killed mid-stream |

```bash
# reproduce
docker run --rm --network urlshortener_default -v ./load:/scripts:ro \
  grafana/k6 run /scripts/redirect.js
```

## Run

```bash
docker compose up --build
```

```bash
# create a link through the gateway
curl -s -X POST localhost:8080/api/links \
  -H 'Content-Type: application/json' \
  -d '{"url": "https://example.com", "codeLength": 6}'

# follow it
curl -i localhost:8080/<code>
```

Watch the gateway logs to see requests alternating between `link-service` replicas.

## Failover demo

```bash
# hammer the redirect in one terminal
while true; do curl -s -o /dev/null -w "%{http_code} " localhost:8080/<code>; sleep 0.2; done

# kill a replica in another — requests keep succeeding
docker kill urlshortener-link-service-2

# bring it back — the health monitor puts it back in rotation
docker start urlshortener-link-service-2
```

## Tests

```bash
dotnet test
```
