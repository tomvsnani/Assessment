# Agentic SDLC System — URL Shortener

An orchestrator that takes a plain-English requirement and drives it through a software delivery
lifecycle with AI agents doing the work and humans approving the high-impact steps. The system it
develops is a production-shaped .NET 9 URL shortener. Both are in this repository.

```
requirement ──▶ requirements ──▶ design ──▶ plan ──▶ { implement ‖ test-design } ──▶ verify ──▶ { review ‖ docs } ──▶ release
                     👤 approve       👤 approve                                        ⇄ loops back on failure         👤 approve
```

## Ten-minute tour for a reviewer

| Want to see… | Open |
|---|---|
| The decisions and why | [PLAN.md](PLAN.md) §0, [docs/adr/](docs/adr/) |
| Which assessment requirement is met where, with evidence | [TRACEABILITY.md](TRACEABILITY.md) |
| Components, orchestration model, control flow | [docs/architecture.md](docs/architecture.md) |
| The stage graph with gates, retries, re-plan rules | [workflows/sdlc.yaml](workflows/sdlc.yaml) |
| The runtime: scheduler, gates, re-planning, rollback | [src/Orchestrator.Core/Engine/](src/Orchestrator.Core/Engine/) — one concept per file |
| Governance: policies, approvals, audit chain | [src/Orchestrator.Core/Governance/](src/Orchestrator.Core/Governance/) |
| What each agent is told | [prompts/](prompts/) |
| The three scenarios: decomposition, orchestration, validation | [docs/scenarios/](docs/scenarios/) |
| Testing approach, limitations, trade-offs | [docs/testing.md](docs/testing.md), [docs/tradeoffs.md](docs/tradeoffs.md) |
| The engineering summary | [docs/engineering-summary.md](docs/engineering-summary.md) |

## Run it

Prerequisites: .NET 9 SDK, git. No API key and no Docker needed for the default paths.

```bash
dotnet test
```

Replay a recorded lifecycle end to end in the terminal (agents' recorded model exchanges, the
recorded human decisions labelled as such, real `dotnet build`/`test` in the sandbox):

```bash
dotnet run --project src/Orchestrator.Host -- run brownfield
```

Or watch it in the dashboard — stage graph, live agent activity, artifacts, policy verdicts,
approvals, metrics, lineage, audit chain:

```bash
dotnet run --project src/Orchestrator.Host
```

then open http://localhost:5100, **New run…**, pick a preset (or write your own requirement), and
untick *live* to replay without a key.

### Live runs (your own key, any provider)

```bash
$env:GEMINI_API_KEY = "..."        # or ANTHROPIC_API_KEY / OPENAI_API_KEY; session only, never a file
dotnet run --project src/Orchestrator.Host
```

Tick *live* in the composer; you decide at each approval gate in the browser. Headless:

```bash
dotnet run --project src/Orchestrator.Host -- run greenfield --live --provider gemini --model gemini-2.5-flash
dotnet run --project src/Orchestrator.Host -- run --requirement my-requirement.md --baseline git:v1-legacy --live
```

Every run writes `runs/<id>/` (events, audit, artifacts, changed files, recording) and can be
replayed by id. See [docs/runbook.md](docs/runbook.md).

### The shortener on its own

```bash
dotnet run --project src/Shortener.Api                 # in-memory; http://localhost:8080
dotnet run --project load/Shortener.LoadTest -- http://localhost:8080 32 10   # redirect p99 vs SLO
docker compose --profile real up -d                    # Postgres + Redis (+ Redpanda for v2)
RUN_DOCKER_TESTS=1 dotnet test tests/Shortener.IntegrationTests
```

API contract: [docs/openapi.yaml](docs/openapi.yaml). Tag `v1-legacy` is the deliberately
pre-event-driven baseline the brownfield scenario modernizes.

## Layout

```
src/Orchestrator.Core        runtime: Workflow · Engine · Governance · State · Metrics · Contracts
src/Orchestrator.Agents      Llm adapters + record/replay · Agents · Tools · Codebase (Roslyn) · Workspace
src/Orchestrator.Host        ASP.NET Core: REST + SSE, dashboard (wwwroot), headless mode
src/Shortener.{Core,Infrastructure,Api}   the product
workflows/ scenarios/ prompts/ requirements/ recordings/   the lifecycle, the presets, what agents are told, the evidence
tests/                       Orchestrator.Tests (77) · Shortener.UnitTests (39) · Shortener.IntegrationTests (14)
docs/                        architecture, ADRs, scenarios, testing, trade-offs, runbook, engineering summary, openapi
```

Conventions the agents and humans both follow: [CLAUDE.md](CLAUDE.md).
