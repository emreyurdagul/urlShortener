# URL Shortener — from-scratch API Gateway

A URL shortener ecosystem built to showcase an API gateway written **from scratch** on Kestrel — no YARP, no off-the-shelf proxy. The gateway does path-based routing, round-robin load balancing across service replicas, hop-by-hop header handling, `X-Forwarded-*` propagation and backend timeouts, with health checks, failover, metrics and autoscaling arriving in later phases.

## Current state (Phase 1)

```
client ──► gateway (:8080) ──► link-service ×2 ──► postgres
              round robin
```

- `Gateway` — hand-written reverse proxy: config-driven route table, `RoundRobinPool`, streamed request/response bodies, RFC 9110 hop-by-hop header stripping.
- `LinkService` — minimal API: create short links (`POST /api/links`, selectable code length 5–8, multi-domain aware) and redirect (`GET /{code}` → 302). Postgres via Dapper, uniqueness enforced by a `(domain, code)` constraint.

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

## Tests

```bash
dotnet test
```
