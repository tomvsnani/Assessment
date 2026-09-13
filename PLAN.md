# Implementation Plan — Agentic SDLC System (URL Shortener)

Status of each task is tracked in `TRACEABILITY.md` (requirement → code → evidence).
This file is the *how*; that file is the *proof*.

## 0. Decisions made up front

| # | Decision | Why |
|---|----------|-----|
| D1 | **.NET 9 for both the URL shortener and the orchestrator** | Role is .NET/Kafka; graders can judge orchestrator code quality in their own language. One solution, one toolchain. |
| D2 | **No agent frameworks** (no Semantic Kernel, LangChain, AutoGen). Hand-written runtime in small named modules. | Orchestration is the graded differentiator; it must be readable and ours. |
| D3 | **Live LLM, provider-agnostic, raw `HttpClient` adapters** (`LLM_PROVIDER=anthropic / openai / gemini`). Recordings committed; replay is the default, `--live` needs a key. | Graders run everything with no key. Live mode proves it is not scripted. |
| D4 | **No staged failures, no scripted human decisions.** Failures and approvals in recordings come from real runs and are labelled as recorded. | Honesty; the first attempt was rejected for faking this. |
| D5 | **Shortener is committed as the human-owned reference implementation.** The greenfield scenario generates into `workspace/` (git-ignored); agent output is snapshotted under `recordings/greenfield/output/` and compared to the reference in the walkthrough. | De-risks the timeline; keeps the "agent output → human QC → committed" story truthful. |
| D6 | **Brownfield = modernization**: move click counting from a synchronous DB increment on the redirect path to outbox → Kafka → analytics consumer. | Mirrors the JD's "legacy → event-driven" bullet; forces real impact analysis. |
| D7 | **Ambiguous = compliance**: "analytics must be retention-compliant" — regulation, retention window, what counts as PII (IP, full URL), raw vs aggregate are all unspecified. | Financial-services flavour; the agent must surface options, the human must decide. |
| D8 | **Codebase reasoning without RAG**: Roslyn repo map (type/method signatures) + deterministic impact analysis behind an `ICodebaseIndex` seam. | Repo is ~2k lines; embeddings would be theatre. The seam shows where an index would plug in. |
| D9 | **Runs without Docker by default** (in-memory repo/cache/bus). `docker compose --profile real up` adds Postgres, Redis, Redpanda. | Graders must get a green run in one command. |

**Open — needed before Phase 4:** which provider/key to record with (assumed Anthropic; key read from env var only, never written to disk).

## 1. Repository layout

```
claudeassess/
├── AgenticSdlc.sln
├── Directory.Build.props          nullable on, warnings as errors, analyzers
├── CLAUDE.md                      custom instructions used by the agents AND by me
├── README.md                      setup, run, one-command demo
├── PLAN.md / TRACEABILITY.md      this plan + requirement matrix
├── docker-compose.yml             postgres, redis, redpanda (optional profile)
├── .github/workflows/ci.yml       build, test, format check
│
├── src/
│   ├── Shortener.Core/            domain + ports (no dependencies)
│   │   ├── Links/                 Link, ShortCode, CreateLink, LinkPolicy
│   │   ├── Analytics/             ClickEvent, ClickAggregate
│   │   └── Ports/                 ILinkRepository, ILinkCache, IClickPublisher, IClock
│   ├── Shortener.Infrastructure/  adapters, one folder per backing service
│   │   └── Postgres/ Redis/ Kafka/ InMemory/ Outbox/
│   ├── Shortener.Api/             ASP.NET Core minimal API
│   │   ├── Endpoints/             CreateLinkEndpoint, RedirectEndpoint, AnalyticsEndpoint, HealthEndpoints
│   │   ├── Middleware/            CorrelationId, ProblemDetails, RateLimiting
│   │   └── Program.cs             composition root only
│   ├── Shortener.Analytics/       Kafka consumer → aggregates (hosted service)
│   │
│   ├── Orchestrator.Core/         the runtime — knows nothing about LLMs
│   │   ├── Contracts/             Requirement, Spec, WorkItem, Artifact, Decision, StageResult (records)
│   │   ├── Workflow/              WorkflowDefinition (YAML), Stage, Gate, DependencyGraph
│   │   ├── Engine/                Scheduler, Executor, Coordinator (re-plan), RetryPolicy, Saga, SafeStop
│   │   ├── Governance/            IPolicy + policies, ApprovalGate, AuditLog
│   │   ├── State/                 EventStore (append-only JSONL), Projections
│   │   └── Metrics/               ReliabilityMetrics (success rate, retries, rollbacks, MTTR, latency)
│   ├── Orchestrator.Agents/       everything that talks to an LLM or the repo
│   │   ├── Llm/                   ILlmClient, AnthropicClient, OpenAiClient, GeminiClient, RecordingClient
│   │   ├── Agents/                one class per role: Requirements, Architect, Planner, Implementer,
│   │   │                          Tester, Reviewer, DocWriter, ReleaseManager
│   │   ├── Tools/                 ReadFile, WriteFile, Grep, RunTests, RunBuild (sandboxed to workspace/)
│   │   └── Codebase/              RepoMap (Roslyn), ImpactAnalysis, ICodebaseIndex
│   └── Orchestrator.Cli/          `sdlc run <scenario> [--live] [--provider x]`, interactive approvals
│
├── workflows/                     greenfield.yaml, brownfield.yaml, ambiguous.yaml (the DAGs)
├── prompts/                       one .md per agent, versioned, reviewable
├── recordings/                    committed LLM exchanges + human decisions per scenario
├── workspace/                     git-ignored; where agents write code
├── load/                          NBomber redirect latency test with a stated SLO
├── docs/
│   ├── architecture.md            components, orchestration model, control flow
│   ├── adr/                       ADR-001..n, one decision per file
│   ├── scenarios/                 greenfield.md, brownfield.md, ambiguous.md walkthroughs
│   ├── testing.md  tradeoffs.md  runbook.md  engineering-summary.md
│   └── openapi.yaml
└── tests/
    ├── Shortener.UnitTests/
    ├── Shortener.IntegrationTests/ WebApplicationFactory; Testcontainers behind a category flag
    └── Orchestrator.Tests/         graph, scheduler, saga, policies, replay of all three scenarios
```

Rule enforced throughout: **one concept per file, one folder per concern, `Program.cs` wires and does nothing else.**

## 2. Orchestration model (what the graders are looking for)

- **Workflow = YAML DAG** of stages:
  `requirements → design → plan → {implement, test-plan} → test → {review, docs} → release-readiness`.
  Braces are parallel branches; each stage declares `entry` and `exit` gates.
- **Scheduler** walks the graph, runs ready stages concurrently (bounded `Task.WhenAll`), waits at joins.
- **Executor** runs one stage: entry gate → agent → exit gate (policies) → emit events.
- **Coordinator** watches the event stream; when an upstream artifact changes (e.g. a human edits the approved spec) it invalidates downstream stages and re-plans.
- **Saga** records a compensation per stage (delete generated files, revert workspace commit); rollback runs them in reverse.
- **Governance**: `ApprovalGate` blocks on human input for high-impact actions (approve spec, approve design, merge, release).
  Policies: `NoSecretsPolicy`, `PiiInLogsPolicy`, `SchemaChangeNeedsApproval`, `SegregationOfDuties` (reviewer ≠ implementer).
  At least one policy fails for real in the recordings.
- **State**: every event appended to `runs/<id>/events.jsonl`. Status, lineage and metrics are projections rebuilt from events alone.
- **Metrics**: success rate, retry and rollback counts, MTTR (failure → recovered), per-stage and end-to-end latency, printed at the end of each run.

## 3. Build phases

Each phase ends with a green `dotnet test` and an update to `TRACEABILITY.md`.

### Phase 0 — Bootstrap
- [x] `git init` on `main` (standalone repo, not the home-directory repo)
- [x] `.gitignore`, `.editorconfig`, `Directory.Build.props`, empty solution
- [x] `CLAUDE.md` with the conventions above
- [x] CI workflow (build + test + format check)
- [x] `docker-compose.yml` (optional `real` profile)

### Phase 1 — URL shortener v1, the "legacy" baseline (tag `v1-legacy`)
This is the system the brownfield scenario modernizes, so it is deliberately *pre*-event-driven:
click counting is a synchronous `UPDATE links SET clicks = clicks + 1` on the redirect path.
- [x] Core: `Link`, `ShortCode` (base62, collision retry), `CreateLink` use case, `LinkPolicy`
- [x] Infrastructure: InMemory adapters; Postgres (Npgsql + Dapper) repository; Redis cache
- [x] Api: `POST /links` (idempotency key), `GET /{code}` (cache-first, 302, **sync click update**), `GET /links/{code}/stats`, `/health/live`, `/health/ready`
- [x] Cross-cutting: Serilog JSON, correlation id, OpenTelemetry, Polly (timeout / retry / circuit-breaker on DB), rate limiting
- [x] Tests: unit (domain, short code, policy), integration (endpoints via `WebApplicationFactory`), Postgres via Testcontainers when Docker is present
- [x] Load test: small HttpClient-based console in `load/` measuring redirect p50/p95/p99 against a stated SLO
- [x] `docs/openapi.yaml`
- [x] Tag `v1-legacy`

### Phase 5b — URL shortener v2 (after the brownfield run)
- [ ] Take the brownfield agent output (outbox table → `IClickPublisher` → Kafka/in-memory bus → `Shortener.Analytics` consumer), review it by hand, harden, commit on `main`
- [ ] Record in `docs/engineering-summary.md` exactly what was kept, changed and rejected from the agent output

### Phase 2 — Orchestrator core
- [x] Contracts (records only)
- [x] Workflow: YAML loader, `DependencyGraph` (topological order, cycle check, parallel groups)
- [x] Engine: `Scheduler`, `Executor`, `RetryPolicy` (bounded, jittered), `Saga`, `SafeStop` (cancellation + poison flag)
- [x] State: `EventStore`, projections (`RunStatus`, `Lineage`)
- [x] Metrics: `ReliabilityMetrics`
- [x] Tests: graph ordering, parallel join, retry exhaustion → fallback, rollback order, re-plan on upstream change

### Phase 3 — Governance
- [x] `IPolicy`, the four policies above, `PolicyGate`
- [x] `ApprovalGate` + `IApprover` port (console and recorded approvers arrive with the CLI in Phase 4)
- [x] `AuditLog` (structured JSONL: actor, stage, input hash, outcome, timestamp)
- [x] Tests: policy blocks stage, approval denied → safe stop, audit completeness

### Phase 4 — LLM + agents
- [x] `ILlmClient` + three raw-HTTP adapters + `RecordingClient` (record / replay keyed by request hash)
- [x] Tools sandboxed to `workspace/`
- [x] `Codebase/RepoMap` (Roslyn signatures), `ImpactAnalysis`, `ICodebaseIndex`
- [x] Nine agents (eight LLM-backed + deterministic `VerifierAgent`), each: load prompt from `prompts/`, build context, call LLM, parse typed output
- [x] Re-plan wiring: verify/review failure → `rerun_from: implement` with feedback; human `revise` re-runs the stage with notes
- [x] Tests: replay round-trip, a prompt file exists for every agent, parser rejects malformed output

### Phase 5 — Scenarios + recordings
- [x] `workflows/*.yaml` for greenfield, brownfield, ambiguous
- [ ] Run each live with interactive approvals; commit recordings; snapshot greenfield output
- [ ] Replay tests: all three scenarios pass with no key, deterministically
- [ ] Confirm the recordings contain at least one real retry, one real policy block, one real human rejection

### Phase 6 — Documentation
- [ ] `docs/architecture.md`, ADRs, three scenario walkthroughs (decomposition / orchestration / validation each)
- [ ] `docs/testing.md`, `docs/tradeoffs.md` (RAG deferred, Kafka optional, …), `docs/runbook.md`
- [ ] `docs/engineering-summary.md`: plan/rationale, artifacts, risks, assumptions, limitations, **how AI was used and what was overridden**
- [ ] `README.md`: setup in ≤ 5 commands

### Phase 7 — Final verification
- [ ] Fresh clone, no env vars, `dotnet test` green, `sdlc run` for all three scenarios
- [ ] `docker compose --profile real up` path verified once
- [ ] Readability pass: every folder name matches this plan; no file over ~200 lines without a reason

## 4. Out of scope (stated, not hidden)
GCP deployment, Kubernetes manifests, real Jira/Confluence integration, embeddings/RAG, multi-region, authentication on the shortener API.
