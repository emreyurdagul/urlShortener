# URL Shortener — from-scratch API Gateway

A URL shortener ecosystem built to showcase an API gateway written **from scratch** on Kestrel — no YARP, no off-the-shelf proxy. The gateway does path-based routing, round-robin load balancing across service replicas, hop-by-hop header handling, `X-Forwarded-*` propagation and backend timeouts, with health checks, failover, metrics and autoscaling arriving in later phases.

## Current state (Phase 2)

```
client ──► gateway (:8080) ──► link-service ×3 ──► postgres
              round robin
           + health checks
           + failover
```

- `Gateway` — hand-written reverse proxy: config-driven route table, `RoundRobinPool`, streamed request/response bodies, RFC 9110 hop-by-hop header stripping.
- **Health checks & failover** — a background monitor probes every backend's `/health` on an interval and takes unhealthy replicas out of rotation; on a connection-level failure the proxy ejects the backend immediately and retries the request on the next healthy one (bodyless requests only — a consumed body can't be replayed). Recovered backends rejoin automatically.
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
