# Monolith — the single-process counterpart

This is the **same URL shortener as the microservice build, rebuilt as one
classic layered monolith**. Same public API, same database schema, same redirect
cache and timing-safe login — so the existing k6 load test runs against either
stack unchanged and the two can be compared apples-to-apples.

Its whole reason to exist is to make the trade-offs concrete: what the gateway,
load balancer, health checks, failover and inter-service HTTP were *buying* — and
what they *cost* — becomes visible when you delete them and keep everything else.

## Layered structure

```
src/Monolith/
  Program.cs            composition root — DI wiring + middleware pipeline
  Api/         (presentation)  AuthController, LinkController, RedirectController, AnalyticsController
  Services/    (application)   AuthAppService, LinkAppService, AnalyticsAppService, ClickRecorder, ClickWriter
  Data/        (infrastructure) Database (one migration) + UserRepository, LinkRepository, ClickRepository
  Auth/                        JwtService (issue+validate), AuthMiddleware, TokenBucketRateLimiter, RateLimitMiddleware
  Domain/                      Plans, CodeGenerator, DomainConfig, QrGenerator, LinkCache
  Models.cs                    shared records
```

Classic **Controller → Service → Repository**. Controllers stay thin (validate
input, map results to HTTP); services hold the business rules; repositories own
the SQL.

## What disappeared vs. the microservices

| Microservice concern | Fate in the monolith |
|---|---|
| Gateway **routing** | ASP.NET endpoint routing — built in, not a component |
| Gateway **load balancing** (`RoundRobinPool`) | gone — one process |
| Gateway **health checks + failover** (`HealthMonitor`, `Backend`) | gone — no backends to probe |
| Gateway **proxy** (`ProxyHandler`, hop-by-hop headers, X-Forwarded) | gone — nothing to forward to |
| Gateway **auth + trusted-header dance** (strip then inject `X-User-*`) | plain in-process `AuthMiddleware`; identity is a typed object on `HttpContext.Items`, never a forgeable header — no trust boundary needed |
| JWT **mint (auth) + validate (gateway)** across two services | one `JwtService` class; the cross-service contract collapses to a method call |
| `ClickShipper` **HTTP batch** to analytics-service | `ClickWriter` — same channel + batching, but writes **straight to Postgres**; the network hop disappears |
| **Database-per-service** (3 schemas, 3 migrations, 3 advisory locks) | one migration under one lock; layers share the DB directly |

**Kept identical** (so the load test is fair): the API surface, DB schema, redirect
`LinkCache`, code-collision retry, timing-safe login, per-client token bucket.

Net effect: the entire `src/Gateway/` project (13 files) evaporates; the three
services become three `Services/` classes in one deployable.

## Run locally

```bash
podman-compose -f docker-compose.mono.yml up --build
```

One app on host port **8090** (the microservice gateway uses 8080) plus its own
Postgres. Then:

```bash
# create a link (anonymous)
curl -s -X POST localhost:8090/api/links \
  -H 'Content-Type: application/json' -d '{"url":"https://example.com"}'

# follow it (Host is the domain the code lives under)
curl -i -H 'Host: localhost:8090' localhost:8090/<code>
```

`/metrics` (Prometheus) and `/health` are exposed just like each microservice.

## Load-test comparison (micro vs. mono)

The k6 script in `../../load/redirect.js` is parameterised by env, so it drives
either stack. Run them **one at a time** on the same machine for a clean signal.

> **Rate limiter caveat.** Both stacks default to 30 req/s **per client key**, and
> a load generator hammers from a *single* source IP — so the redirect throughput
> test is throttled unless you raise the ceiling. To measure *architecture* (the
> proxy hop vs. in-process) rather than the limiter, raise it on **both** sides.
> (This is also worth checking against the microservice README's headline number,
> whose repro command does go through the rate-limited path.)

Monolith:

```bash
RATE_LIMIT_PER_SECOND=100000 RATE_LIMIT_BURST=100000 \
  podman-compose -f docker-compose.mono.yml up --build -d

podman run --rm --network urlshortener-mono_default -v ./load:/scripts:ro \
  -e GATEWAY=http://monolith:8080 -e LINK_DOMAIN=localhost:8090 \
  docker.io/grafana/k6 run /scripts/redirect.js
```

Microservices (raise the gateway's `RATE_LIMIT_PER_SECOND` in
`docker-compose.local.yml` the same way first):

```bash
podman-compose -f docker-compose.local.yml up --build -d
podman run --rm --network urlshortener_default -v ./load:/scripts:ro \
  -e GATEWAY=http://gateway:8080 -e LINK_DOMAIN=localhost:8080 \
  docker.io/grafana/k6 run /scripts/redirect.js
```

Compare `http_reqs` (throughput) and `http_req_duration` p95/p99. Expect the
monolith to win **per-request latency** (no proxy hop) while the microservices'
advantage is **independent horizontal scaling** of the hot service (link-service
×3) — the trade-off this whole exercise exists to show.
