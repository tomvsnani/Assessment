# Agentic SDLC System — URL Shortener

An agentic orchestration layer (`src/Orchestrator.*`) that drives the software development
lifecycle of a URL shortener service (`src/Shortener.*`), with human approval gates, policy
guardrails, audit trail and reliability metrics.

- `PLAN.md` — decisions, layout, build phases
- `TRACEABILITY.md` — assessment requirement → code → evidence
- `docs/` — architecture, ADRs, scenario walkthroughs, testing, trade-offs, runbook

## Quick start (no key, no Docker)

```bash
dotnet test
```

Run the shortener:

```bash
dotnet run --project src/Shortener.Api
```

```bash
curl -s -X POST http://localhost:8080/links -H "Content-Type: application/json" -d "{\"url\":\"https://example.com\"}"
```

Measure the redirect path against its SLO (p99 ≤ 20 ms):

```bash
dotnet run --project load/Shortener.LoadTest -- http://localhost:8080 32 10
```

## With real backing services

```bash
docker compose --profile real up -d
```

Then run the API with `Shortener__Storage=Postgres`, `ConnectionStrings__Postgres=...`,
`ConnectionStrings__Redis=localhost:6379` (see `docker-compose.yml`). Postgres integration tests:

```bash
RUN_DOCKER_TESTS=1 dotnet test tests/Shortener.IntegrationTests
```

## Orchestrator

_Phase 2+ — see `PLAN.md`._
