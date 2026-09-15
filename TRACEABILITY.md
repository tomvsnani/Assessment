# Traceability — Assessment Requirements → Implementation → Evidence

Legend: `[ ]` not demonstrated · `[~]` partly demonstrated · `[x]` demonstrated and verified.
"Evidence" is what a grader can open or run to check the claim. A status is `[x]` only when the
evidence named exists in this repository or in a run directory at the time of the last update
(2026-09-15). Where the evidence is a live run, the run id is given.

Start from the [README](README.md); this file is the cross-reference.

## A. Core requirements (assignment §4)

| # | Requirement | Where it lives | Evidence | Status |
|---|-------------|----------------|----------|--------|
| A1 | Requirement understanding — interpret intent, find ambiguity, normalize | `Orchestrator.Agents/Agents/RequirementsAgent.cs`, `prompts/requirements.md`, `Contracts/Spec.cs` | Live: `greenfield-20260915-163443` spec — 8 Given/When/Then criteria, 14 in / 7 out of scope, 6 assumptions, 2 business ambiguities with options and a recommendation; platform-fit rule for out-of-scope requirements. [scenarios/greenfield.md](docs/scenarios/greenfield.md) | [x] |
| A2 | Task decomposition — actionable tasks, dependencies, sequencing | `Agents/PlannerAgent.cs`, `Contracts/WorkItem.cs` (Jira-shaped: key, AC ids, depends-on, risk, files) | Live: same run — 20 work items `WI-1…WI-20`, DAG validated, risk-rated. `PlannerAgent` rejects cycles. | [x] |
| A3 | Codebase reasoning (brownfield) — impacted modules, APIs, data flows | `Orchestrator.Agents/Codebase/RepoMap.cs`, `ImpactAnalysis.cs`, `ICodebaseIndex.cs` | `CodebaseIndexTests` (deterministic ranking over real source). **No live brownfield run yet** — [scenarios/brownfield.md](docs/scenarios/brownfield.md) | [~] |
| A4a | Explicit dependency graph with entry/exit gates | `Orchestrator.Core/Workflow/DependencyGraph.cs`, `StageDefinition.cs`, `GateDefinition.cs`; `workflows/sdlc.yaml` | `DependencyGraphTests` (9), `WorkflowLoaderTests` (6); `sdlc graph` | [x] |
| A4b | Sequential + parallel paths with synchronization | `Orchestrator.Core/Engine/Scheduler.cs` | `SchedulerTests` (branches overlap; `max_parallel: 1` never overlaps; join waits). Live: `implement ‖ test-design` overlapped in `greenfield-20260915-163443` | [x] |
| A4c | Cross-stage context and decision lineage | `State/EventStore.cs`, `Projections/Lineage.cs`, `Contracts/Decision.cs` | `runs/<id>/events.jsonl`, `lineage.mmd`; decisions `D001–D004` cited downstream in the live run; `EventStoreTests` | [x] |
| A4d | Human approval checkpoints for high-impact actions | `Governance/ApprovalGate.cs`; `approval:` on requirements, design, release | `GovernanceFlowTests` (reject → stop, revise → re-run, ambiguity resolution written into the spec). Live: `ApprovalRequested`/`ApprovalDecided` at spec and design gates | [x] |
| A4e | Bounded retries, fallback, rollback, safe-stop | `Engine/RetryPolicy.cs`, `Executor.cs`, `Saga.cs`, `SafeStop.cs`, `Workflow/RerunMode.cs` | `RetryFallbackTests` (6), `ReplanTests` (5), `SchedulerTests` rollback + Ctrl+C. Live: retries, fix-mode re-plans, safe-stop rollback all observed (run ids in [engineering-summary §5](docs/engineering-summary.md#5-ai-assistance-and-what-was-overridden)) | [x] |
| A4f | Policy guardrails — security, compliance, change control | `Governance/Policies/NoSecretsPolicy.cs`, `PiiInLogsPolicy.cs`, `SchemaChangeNeedsApprovalPolicy.cs`, `SegregationOfDutiesPolicy.cs`; platform boundary in prompts | `PolicyTests` (21), `GovernanceFlowTests` block → feedback → retry. Live: 11 policy evaluations in `greenfield-20260915-163443`; segregation of duties caught a gamed verifier (see A7) | [x] |
| A4g | Audit-grade observability and traceability | `Governance/AuditLog.cs` (hash-chained JSONL), `State/EventStore.cs`, dashboard SSE | `runs/<id>/audit.jsonl`; `AuditLogTests` (chain verifies, tamper detected); `sdlc verify-audit` | [x] |
| A4h | Reliability metrics — success rate, retry/rollback frequency, MTTR, E2E latency | `Orchestrator.Core/Metrics/ReliabilityMetrics.cs` | `runs/<id>/metrics.txt`; asserted in `RetryFallbackTests`, `ReplanTests` | [x] |
| A4i | Dynamic re-planning when upstream outputs change, with governance | `Engine/Coordinator.cs`, `Workflow/RerunMode.cs` | `ReplanTests` (verify-fails → fix loop; rollback mode; loop budget; upstream-artifact-changed invalidation). Live: two fix-mode re-plans in `greenfield-20260915-163443` | [x] |
| A5 | Engineering output — production code, API/schema, tests, docs | `src/Shortener.*`, `docs/openapi.yaml`, `tests/Shortener.*` | `dotnet test`: 39 unit + 10 integration green (4 container tests need Docker); load test with SLO; CI | [x] |
| A6 | Validation and risk control — risks, trade-offs, failure scenarios, guardrails | `docs/tradeoffs.md`, `docs/engineering-summary.md` §4, policies above | Each scenario walkthrough has a Validation section; §4 of the summary maps risk → mitigation → test/run | [x] |
| A7 | Controlled autonomy — agents execute, humans approve | `ApprovalGate.cs`, dashboard approvals, `SegregationOfDutiesPolicy.cs` | Live: human decisions `D001–D004` recorded with actor; reviewer refused an implementation that had gamed the verifier and sent it back ([scenarios/greenfield.md](docs/scenarios/greenfield.md)) | [x] |
| A8 | Final engineering summary | `docs/engineering-summary.md` | Plan/rationale, artifacts, risks, assumptions, limitations, AI-assistance overrides | [x] |

## B. Deliverables (assignment §5)

| # | Deliverable | Where | Status |
|---|-------------|-------|--------|
| B1 | Working prototype, runnable end-to-end | `dotnet test` green (134); dashboard and headless host run; live runs reach `review` and loop back correctly. **Key-free replay needs a published recording; none exists yet** | [~] |
| B2 | Architecture overview — components, orchestration model, control flow, key decisions | `docs/architecture.md` (Mermaid diagrams), `docs/adr/` | [x] |
| B3 | Greenfield scenario — decomposition, orchestration, validation | `docs/scenarios/greenfield.md` with evidence from `runs/greenfield-20260915-163443` and predecessors | [~] run in progress at last update |
| B4 | Brownfield scenario — same | `docs/scenarios/brownfield.md` — setup, mechanisms, tests; **no live run yet** | [~] |
| B5 | Ambiguous scenario — same | `docs/scenarios/ambiguous.md` — setup, mechanism evidenced on other runs; **no live run yet** | [~] |
| B6 | Setup instructions | `README.md` § Run it, `docs/runbook.md` | [x] |
| B7 | Testing approach, limitations, trade-offs | `docs/testing.md`, `docs/tradeoffs.md`, `docs/engineering-summary.md` §7 | [x] |

## C. Evaluation criteria (assignment §6) — where each is demonstrated

| Criterion | Primary evidence |
|-----------|------------------|
| Effectiveness of agentic orchestration | A4a–A4i; the live greenfield run's two fix-mode loops and the reviewer catching a gamed verifier |
| Architecture / system design quality | `docs/architecture.md`, ADRs, one-concept-per-file layout, `Orchestrator.Core` with no LLM dependency |
| Depth of decomposition and execution quality | 20-item risk-rated DAG plan; implementer builds as it goes; deterministic verifier |
| Realism / quality of outputs | Shortener passes its tests and has a measured SLO gap; agent output reviewed by an independent role and by a human |
| Validation and risk management rigor | Policies, 134 tests, `docs/tradeoffs.md`, load test, `engineering-summary.md` §4 |
| Clarity and defensibility of decisions | `PLAN.md` §0, ADRs, audit log with decision ids |
| Modular, testable, reliable, secure, scalable, safe change management | Core/Infra/Api split, ports, Polly, no-secrets and schema-change policies, approval gates, saga rollback |
| Engineering judgment | `engineering-summary.md` §5 — seven concrete overrides of AI/pipeline behaviour, each with the run that motivated it |

## D. JD-driven items (not in the assignment text, but what the graders live in)

| # | JD signal | What we do about it | Where | Status |
|---|-----------|---------------------|-------|--------|
| D1 | .NET, high-volume services | Whole solution in .NET 9 | `AgenticSdlc.sln` | [x] |
| D2 | Event-driven, Kafka | Click events via outbox → Kafka → consumer; in-memory bus for no-Docker runs. **v1 deliberately does not have this** — it is what the brownfield scenario adds | `requirements/brownfield.md`; produced in the brownfield workspace | [ ] no live run yet |
| D3 | Ultra-low latency | Cache-first redirect, p99 SLO measured. In-memory: 37,946 rps, p99 3.62 ms. Real Postgres+Redis: 293 rps, p99 683.77 ms, SLO FAIL — the synchronous click `UPDATE` on one hot row, which is the measured motivation for the brownfield scenario | `Endpoints/RedirectEndpoint.cs`, `Core/Links/LinkResolver.cs`, `load/` | [x] |
| D4 | Resiliency patterns | Polly timeout / retry / circuit breaker, idempotency key, health endpoints, graceful shutdown | `Shortener.Api/Program.cs`, `Middleware/` | [x] |
| D5 | Observability / Splunk | Serilog JSON, correlation id, OpenTelemetry; orchestrator audit log in the same JSONL shape | `Shortener.Api`, `Governance/AuditLog.cs` | [x] |
| D6 | Twelve-Factor, Docker, CI/CD | Env-only config, Dockerfile, compose profile, GitHub Actions | root files, `.github/workflows/ci.yml` | [x] |
| D7 | PostgreSQL / caching | Npgsql repository + Redis cache behind ports | `Shortener.Infrastructure/Postgres`, `/Redis` | [x] |
| D8 | TDD / BDD | Acceptance criteria as Given/When/Then, approved before implementation; test designer works from the spec, blind to the code | `Contracts/Spec.cs`, `prompts/requirements.md`, `prompts/test-designer.md` | [x] |
| D9 | Spec-driven dev, custom instructions, prompt engineering | Versioned spec artifact; `CLAUDE.md` injected into every agent; one prompt file per role; platform boundary declared | `CLAUDE.md`, `prompts/` | [x] |
| D10 | Jira / Confluence | Ticket-shaped work items; Confluence-shaped docs (ADR, runbook, environment) | `Contracts/WorkItem.cs`, `docs/runbook.md` | [x] |
| D11 | Change control / production readiness | Release stage emits change record: risk rating, blast radius, backout plan; schema-change policy | `Agents/ReleaseManagerAgent.cs`, `docs/runbook.md` | [~] no run has reached `release` yet |
| D12 | Modernization (legacy → event-driven) | Brownfield scenario is exactly this migration | `docs/scenarios/brownfield.md` | [~] |
| D13 | Responsible AI-assisted delivery, ownership of correctness | Engineering summary documents what AI produced and what was rejected, with run ids | `docs/engineering-summary.md` §5 | [x] |

## E. Done so far

| Date | Item | Notes |
|------|------|-------|
| 2026-09-13 | Standalone git repo initialised on `main` | Folder was previously inside the home-directory repo |
| 2026-09-13 | `PLAN.md` written — decisions, layout, orchestration model, phases | Awaiting confirmation of D1 (language) and the LLM provider for recordings |
| 2026-09-15 | Implementer efficiency and shift-left policy feedback. Run `greenfield-20260915-163443` (24 min; first to pass `verify`; reviewer caught a gamed verifier) ended when `pii-in-logs` blocked `LogWarning("Invalid URL submitted: {Url}")` at the exit gate after 46 turns with no retry left. Measured: implementer 15.7 min / 147 model calls / 5.7M input tokens; 56 `read_file` calls on the review re-run; whole-file rewrites of an 11 KB `Program.cs`. Changes: `write_file`/`edit_file` pre-check `no-secrets` + `pii-in-logs` and warn in the tool result; new `edit_file` (exact unique replace); implementer builds per project and batches independent writes in one turn; `implement` retry 2 → 3; prompt forbids stubbing endpoints to pass tests. 5 tests; 103 green | A4e, A4f, D13 |
| 2026-09-15 | Documentation pass: README rewritten as the entry point with the reviewer's tour; `docs/architecture.md` diagrams converted to Mermaid (lifecycle, components, control flow, projections — all render on GitHub); `docs/scenarios/{README,greenfield,brownfield,ambiguous}.md` written with run evidence by id and explicit "not yet run" status where true; `docs/engineering-summary.md` written; this matrix re-verified line by line. Artifact recovery extended to markdown reports (implementer report without a wrapper in `greenfield-20260915-163443`); 98 tests green | A6, A8, B2–B7 |
| 2026-09-15 | Artifact recovery. Run `greenfield-20260915-162943`: the requirements agent returned a complete, valid spec as one ```json fence with no `<artifact>` wrapper, twice, ignoring the retry feedback; the run failed at stage 1. Every role owns one artifact, so `LlmAgent` now recovers it when the final message is unambiguously a single document (one fence, or bare JSON), logs the recovery, and the retry feedback shows the exact wrapper. 5 tests; 96 green | A4e, D13 |
| 2026-09-15 | Retry rung keeps the workspace; Gemini empty responses are transient. Live run `greenfield-20260915-161352`: the fix loop worked (implementer patched `Directory.Packages.props` instead of rewriting), then Gemini returned an empty candidate after a green build → attempt failed → checkpoint restored → rebuilt; then a candidate without `content` → `KeyNotFoundException` → stage failed → run rolled back. Executor now keeps files across attempts (policies evaluated since stage start; stage checkpoint restored only if every attempt fails); `GeminiClient` raises no-candidate/no-content/empty responses as transient `LlmException` so the tool loop retries. 6 new tests; 91 green | A4e, D13 |
| 2026-09-15 | Platform boundary made explicit and early. Role prompts no longer assume the product is the shortener (`_shared.md` describes a .NET 9 delivery pipeline; `<Product>.Core/Ports` conventions instead of `Shortener.*`), and `requirements.md` raises a non-.NET requirement as `AMB-1 PLATFORM:` with re-scope (API / Blazor) or stop options, so the run halts at the spec gate for a human instead of at `verify`. Motivated by the React gym-app run (`runs/adhoc-20260915-040442`) | A1, A7, D9, D13 |
| 2026-09-14 | `on_failure.mode: fix \| rollback`. Observed in a live run: verify failed on a compiler error, the loop restored the implementer's checkpoint, and the second attempt re-derived everything from the plan with feedback pointing at files that no longer existed. Verdict loops now keep the implementer's files by default (saga step retained so safe-stop still unwinds); stale-input invalidation still restores. 3 new `ReplanTests`, 2 loader tests; 85 green | A4e, A4i, D13 (judgment override recorded in `docs/tradeoffs.md`) |
| 2026-09-13 | `TRACEABILITY.md` written — this matrix | All implementation rows `[ ]` |
| 2026-09-13 | Phase 0 bootstrap: solution, `Directory.Build.props` (warnings-as-errors, analyzers, central packages), `CLAUDE.md`, CI, compose | D6 |
| 2026-09-14 | Docker verification: 4 Postgres Testcontainers tests now run (found and fixed a Dapper column-mapping bug); API smoke-tested against compose Postgres+Redis (ready 200, 302, stats, cache key present); load test against Postgres shows the legacy hot-path problem: 293 rps, p99 684 ms | A5, D3, D7 |
| 2026-09-13 | Phase 4 LLM + agents: provider-agnostic `ILlmClient` with raw-HTTP Anthropic/OpenAI/Gemini adapters, `RecordingClient` (record/replay by request hash, sequence fallback reported), sandboxed `FileWorkspace` + tools (read/write/list/grep/build/test), Roslyn `RepoMap` + deterministic `ImpactAnalysis` behind `ICodebaseIndex`, nine agents with prompts in `prompts/`, CLI (`sdlc run|graph|verify-audit`) with console/recording/replay approvers, three workflow YAMLs, three requirement files. 76 orchestrator tests green. Baseline materialisation from `git:v1-legacy` verified (workspace builds, 49 tests pass) | A1–A3, A7, D8–D11 scaffolding |
| 2026-09-13 | Phase 2+3 orchestrator core: contracts, YAML workflow + `DependencyGraph`, `Scheduler` (bounded parallel, joins), `Executor` (gates, bounded retry w/ jitter, fallback agent, human revisions), `Coordinator` (rerun-from loops, upstream-change invalidation), `Saga` rollback, `SafeStop`, event-sourced `EventStore` + `RunStatus`/`Lineage` projections, `ReliabilityMetrics`, 4 policies, `ApprovalGate`, hash-chained `AuditLog`. 58 tests green | A4a–A4i |
| 2026-09-13 | Phase 1 shortener v1: Core (7 files), Infrastructure (InMemory / Postgres+Dapper+Polly / Redis), Api (minimal API, Serilog JSON, OTel, correlation id, rate limit, health probes), 39 unit + 10 integration tests green, 4 Postgres Testcontainers tests written (need Docker running), load tool, `openapi.yaml`. Tagged `v1-legacy` | A5 partial, D1, D3–D7 |
