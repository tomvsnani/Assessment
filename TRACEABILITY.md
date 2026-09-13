# Traceability — Assessment Requirements → Implementation → Evidence

Legend: `[ ]` not started · `[~]` in progress · `[x]` done and verified.
"Evidence" is what a grader can open or run to check the claim. Updated at the end of every phase in `PLAN.md`.

## A. Core requirements (assignment §4)

| # | Requirement | Where it lives | Evidence | Status |
|---|-------------|----------------|----------|--------|
| A1 | Requirement understanding — interpret intent, find ambiguity, normalize | `Orchestrator.Agents/Agents/RequirementsAgent.cs`, `prompts/requirements.md`, `Contracts/Spec.cs` | `docs/scenarios/ambiguous.md`; recorded spec with ambiguity list | [ ] |
| A2 | Task decomposition — actionable tasks, dependencies, sequencing | `Agents/PlannerAgent.cs`, `Contracts/WorkItem.cs` (Jira-shaped: key, AC, depends-on, risk) | `recordings/*/plan.json`; `Orchestrator.Tests/PlannerOutputTests.cs` | [ ] |
| A3 | Codebase reasoning (brownfield) — impacted modules, APIs, data flows | `Orchestrator.Agents/Codebase/RepoMap.cs`, `ImpactAnalysis.cs`, `ICodebaseIndex.cs` | `docs/scenarios/brownfield.md` impact table; `Orchestrator.Tests/ImpactAnalysisTests.cs` | [ ] |
| A4a | Explicit dependency graph with entry/exit gates | `Orchestrator.Core/Workflow/DependencyGraph.cs`, `Stage.cs`, `Gate.cs`; `workflows/*.yaml` | `Orchestrator.Tests/DependencyGraphTests.cs` | [x] |
| A4b | Sequential + parallel paths with synchronization | `Orchestrator.Core/Engine/Scheduler.cs` | `SchedulerTests.Given_diamond_workflow_When_run_Then_branches_overlap_and_join_waits_for_both`, `..._max_parallel_one_...` | [x] |
| A4c | Cross-stage context and decision lineage | `Orchestrator.Core/State/EventStore.cs`, `Projections/Lineage.cs` | `runs/<id>/events.jsonl`; `Lineage` projection + Mermaid render (`EventStoreTests`); walkthroughs in Phase 6 | [~] |
| A4d | Human approval checkpoints for high-impact actions | `Orchestrator.Core/Governance/ApprovalGate.cs`; `approval:` entries in `workflows/*.yaml` | `GovernanceFlowTests` (reject→stop, revise→re-run, ambiguity resolution written into spec); recordings in Phase 5 | [~] |
| A4e | Bounded retries, fallback, rollback, safe-stop | `Engine/RetryPolicy.cs`, `Engine/Saga.cs`, `Engine/SafeStop.cs` | `RetryFallbackTests` (4), `SchedulerTests` rollback + Ctrl+C tests; a real retry in recordings (Phase 5) | [x] |
| A4f | Policy guardrails — security, compliance, change control | `Governance/Policies/NoSecretsPolicy.cs`, `PiiInLogsPolicy.cs`, `SchemaChangeNeedsApproval.cs`, `SegregationOfDuties.cs` | `PolicyTests` (21), `GovernanceFlowTests` block→feedback→retry; a real policy block in recordings (Phase 5) | [x] |
| A4g | Audit-grade observability and traceability | `Governance/AuditLog.cs` (JSONL), `State/EventStore.cs` | `runs/<id>/audit.jsonl` with hash chain; `AuditLogTests` (chain verifies, tamper detected) | [x] |
| A4h | Reliability metrics — success rate, retry/rollback frequency, MTTR, E2E latency | `Orchestrator.Core/Metrics/ReliabilityMetrics.cs` | `ReliabilityMetrics.Render()` at end of each run; asserted in `RetryFallbackTests`, `ReplanTests` | [x] |
| A4i | Dynamic re-planning when upstream outputs change | `Engine/Coordinator.cs` | `ReplanTests` (verify-fails→implement re-run with feedback, loop budget, upstream-artifact-changed invalidation) | [x] |
| A5 | Engineering output — production code, API/schema, tests, docs | `src/Shortener.*`, `docs/openapi.yaml`, `tests/Shortener.*` | `dotnet test` green; CI badge | [~] |
| A6 | Validation and risk control — risks, trade-offs, failure scenarios, guardrails | `docs/tradeoffs.md`, `docs/engineering-summary.md` §Risks; policies above | walkthroughs each have a "Validation" section | [ ] |
| A7 | Controlled autonomy — agents execute, humans approve | `ApprovalGate.cs`, `Orchestrator.Cli` interactive approver | recordings show human decisions labelled as recorded | [ ] |
| A8 | Final engineering summary | `docs/engineering-summary.md` | — | [ ] |

## B. Deliverables (assignment §5)

| # | Deliverable | Where | Status |
|---|-------------|-------|--------|
| B1 | Working prototype, runnable end-to-end without a key | `README.md` quick start; `sdlc run <scenario>` replays recordings | [ ] |
| B2 | Architecture overview — components, orchestration model, control flow, key decisions | `docs/architecture.md`, `docs/adr/` | [ ] |
| B3 | Greenfield scenario — decomposition, orchestration, validation | `workflows/greenfield.yaml`, `recordings/greenfield/`, `docs/scenarios/greenfield.md` | [ ] |
| B4 | Brownfield scenario — same | `workflows/brownfield.yaml`, `recordings/brownfield/`, `docs/scenarios/brownfield.md` | [ ] |
| B5 | Ambiguous scenario — same | `workflows/ambiguous.yaml`, `recordings/ambiguous/`, `docs/scenarios/ambiguous.md` | [ ] |
| B6 | Setup instructions | `README.md` | [ ] |
| B7 | Testing approach, limitations, trade-offs | `docs/testing.md`, `docs/tradeoffs.md` | [ ] |

## C. Evaluation criteria (assignment §6) — where each is demonstrated

| Criterion | Primary evidence |
|-----------|------------------|
| Effectiveness of agentic orchestration | A4a–A4i above; replay of three scenarios in `Orchestrator.Tests` |
| Architecture / system design quality | `docs/architecture.md`, ADRs, `src/` layout matching `PLAN.md` §1 |
| Depth of decomposition and execution quality | `recordings/*/plan.json`, generated code snapshot vs reference comparison |
| Realism / quality of outputs | Shortener passes its own tests and load SLO; agent output reviewed by a human |
| Validation and risk management rigor | Policies, tests, `docs/tradeoffs.md`, load test |
| Clarity and defensibility of decisions | `PLAN.md` §0 decisions table, ADRs, audit log |
| Modular, testable, reliable, secure, scalable, safe change management | Core/Infra/Api split, Polly, outbox, no-secrets policy, approval gates |
| Engineering judgment | `docs/engineering-summary.md` §"AI assistance and what was overridden" |

## D. JD-driven items (not in the assignment text, but what the graders live in)

| # | JD signal | What we do about it | Where | Status |
|---|-----------|---------------------|-------|--------|
| D1 | .NET, high-volume services | Whole solution in .NET 9 | `AgenticSdlc.sln` | [x] shortener; orchestrator pending |
| D2 | Event-driven, Kafka | Click events via outbox → Kafka → consumer; in-memory bus for no-Docker runs. **v1 deliberately does not have this** — it is what the brownfield scenario adds | `Shortener.Infrastructure/Outbox`, `/Kafka`, `Shortener.Analytics` (v2) | [ ] |
| D3 | Ultra-low latency | Cache-first redirect, p99 SLO measured. v1 measured 37,946 rps, p99 3.62 ms in-memory (32 workers, Debug build) — but the sync click write is still on the hot path; v2 removes it | `Endpoints/RedirectEndpoint.cs`, `Core/Links/LinkResolver.cs`, `load/LatencyReport.cs` | [x] |
| D4 | Resiliency patterns | Polly timeout / retry / circuit breaker, idempotency key, health endpoints, graceful shutdown | `Shortener.Api/Program.cs` wiring, `Middleware/` | [x] |
| D5 | Observability / Splunk | Serilog JSON, correlation id, OpenTelemetry; orchestrator audit log in the same JSONL shape | `Shortener.Api`, `Governance/AuditLog.cs` | [x] |
| D6 | Twelve-Factor, Docker, CI/CD | Env-only config, Dockerfile, compose profile, GitHub Actions | root files, `.github/workflows/ci.yml` | [x] |
| D7 | PostgreSQL / caching | Npgsql repository + Redis cache behind ports | `Shortener.Infrastructure/Postgres`, `/Redis` | [x] |
| D8 | TDD / BDD | Acceptance criteria as Given/When/Then, approved before implementation | `Contracts/Spec.cs`, `prompts/requirements.md` | [ ] |
| D9 | Spec-driven dev, custom instructions, prompt engineering | Versioned spec artifact; `CLAUDE.md`; one prompt file per agent | `CLAUDE.md`, `prompts/` | [ ] |
| D10 | Jira / Confluence | Ticket-shaped work items; Confluence-shaped docs (ADR, runbook, environment) | `Contracts/WorkItem.cs`, `docs/runbook.md` | [ ] |
| D11 | Change control / production readiness | Release stage emits change record: risk rating, blast radius, backout plan | `Agents/ReleaseManagerAgent.cs`, `docs/runbook.md` | [ ] |
| D12 | Modernization (legacy → event-driven) | Brownfield scenario is exactly this migration | `docs/scenarios/brownfield.md` | [ ] |
| D13 | Responsible AI-assisted delivery, ownership of correctness | Engineering summary documents what AI produced and what was rejected | `docs/engineering-summary.md` | [ ] |

## E. Done so far

| Date | Item | Notes |
|------|------|-------|
| 2026-09-13 | Standalone git repo initialised on `main` | Folder was previously inside the home-directory repo |
| 2026-09-13 | `PLAN.md` written — decisions, layout, orchestration model, phases | Awaiting confirmation of D1 (language) and the LLM provider for recordings |
| 2026-09-13 | `TRACEABILITY.md` written — this matrix | All implementation rows `[ ]` |
| 2026-09-13 | Phase 0 bootstrap: solution, `Directory.Build.props` (warnings-as-errors, analyzers, central packages), `CLAUDE.md`, CI, compose | D6 |
| 2026-09-13 | Phase 2+3 orchestrator core: contracts, YAML workflow + `DependencyGraph`, `Scheduler` (bounded parallel, joins), `Executor` (gates, bounded retry w/ jitter, fallback agent, human revisions), `Coordinator` (rerun-from loops, upstream-change invalidation), `Saga` rollback, `SafeStop`, event-sourced `EventStore` + `RunStatus`/`Lineage` projections, `ReliabilityMetrics`, 4 policies, `ApprovalGate`, hash-chained `AuditLog`. 58 tests green | A4a–A4i |
| 2026-09-13 | Phase 1 shortener v1: Core (7 files), Infrastructure (InMemory / Postgres+Dapper+Polly / Redis), Api (minimal API, Serilog JSON, OTel, correlation id, rate limit, health probes), 39 unit + 10 integration tests green, 4 Postgres Testcontainers tests written (need Docker running), load tool, `openapi.yaml`. Tagged `v1-legacy` | A5 partial, D1, D3–D7 |
