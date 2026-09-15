# Architecture

An agentic orchestrator that takes a plain-English requirement and drives it through a software
delivery lifecycle — requirements, design, planning, implementation, verification, review,
documentation, release readiness — with humans approving the high-impact steps. The system it
develops is a .NET 9 URL shortener; the orchestrator is itself an ASP.NET Core service.

Everything below is findable by name in the tree. When a concept appears here it is one file.

## 1. Components

```
┌──────────────────────────────── Orchestrator.Host (ASP.NET Core) ────────────────────────────────┐
│  REST + SSE API  ·  dashboard (wwwroot)  ·  RunService (composition root)  ·  approvers          │
│  headless mode: sdlc run <scenario> | --requirement <file> | --replay <id>                        │
└──────────────┬───────────────────────────────────────────────────────────┬───────────────────────┘
               │ IStageAgent / IWorkspace / IRunTrace (ports)              │
┌──────────────▼───────────────── Orchestrator.Core ────────────┐   ┌──────▼────── Orchestrator.Agents ──────┐
│ Workflow   WorkflowDefinition, StageDefinition, GateDefinition │   │ Llm      ILlmClient, Anthropic/OpenAI/  │
│            DependencyGraph, WorkflowLoader (YAML)              │   │          Gemini raw-HTTP adapters,      │
│ Engine     Scheduler, Executor, Coordinator, Saga, SafeStop,   │   │          RecordingClient (record/replay)│
│            RetryPolicy, RunState                               │   │ Agents   9 roles + AgentToolLoop        │
│ Governance IPolicy + 4 policies, PolicyGate, ApprovalGate,     │   │ Tools    read/write/list/grep/build/test│
│            AuditLog (hash chain)                               │   │ Codebase RepoMap (Roslyn), ImpactAnalysis│
│ State      EventStore (append-only), RunStatus, Lineage        │   │ Workspace FileWorkspace (sandbox),      │
│ Metrics    ReliabilityMetrics                                  │   │          BaselineMaterializer           │
│ Contracts  Requirement, Spec, WorkItem, Artifact, Decision …   │   └─────────────────────────────────────────┘
└───────────────────────────────────────────────────────────────┘
                              │ writes into
                    ┌─────────▼──────────┐          ┌────────────────────────────────────┐
                    │ workspace/<run>/   │          │ src/Shortener.* (the product)      │
                    │ sandboxed copy of  │  ◀────── │ Core · Infrastructure · Api        │
                    │ the baseline       │ baseline │ tagged v1-legacy for brownfield    │
                    └────────────────────┘          └────────────────────────────────────┘
```

`Orchestrator.Core` has no idea what an LLM is. It sees `IStageAgent.ExecuteAsync(StageContext)`
and nothing else, which is why the whole engine is testable with delegate agents (see
`tests/Orchestrator.Tests/Fakes`) and why the deterministic `VerifierAgent` (runs `dotnet test`)
is a first-class stage like the model-backed ones.

## 2. Orchestration model

### The graph
`workflows/sdlc.yaml` declares stages with `depends_on`, `entry`/`exit` gates, `retry`,
`fallback` and `on_failure`. `DependencyGraph` validates it (unknown deps, duplicates, cycles) and
computes topological order and parallel levels:

```
requirements → design → plan → { implement ‖ test-design } → verify → { review ‖ docs } → release
                 👤              👤                                                          👤
```

👤 marks a human approval gate. Braces are parallel branches; `verify` is a join.

### Control flow
```
Scheduler loop                     Executor (one stage)                        Coordinator
──────────────                     ────────────────────                        ───────────
ready = graph.Ready(state)   ──▶   StageStarted
dispatch ≤ max_parallel            entry gate: artifacts present? policies?
await Task.WhenAny                 ┌─ attempt n ─────────────────────────────┐
                                   │ checkpoint workspace                     │
Completed ──▶ MarkCompleted        │ agent.ExecuteAsync(context)              │
   └▶ ReactToArtifacts ─────────────┼──────────────────────────────────────────┼──▶ artifact hash changed
Failed ──▶ TryRerunFrom ───────────┼──────────────────────────────────────────┼──▶ under completed stages?
   │  (on_failure.rerun_from)      │ exit gate: required artifacts, policies  │      invalidate downstream,
   │                               │   block → feedback → retry               │      compensate, re-schedule
   └─ else SafeStop.Trigger        │ approval? → ApprovalGate                 │
Rejected ──▶ SafeStop.Trigger      │   approve → decisions, resolutions       │
                                   │   revise  → feedback → run again         │
Stopped                            │   reject  → Rejected                     │
                                   └──────────────────────────────────────────┘
loop until all complete            retries exhausted → fallback agent once → Failed
or SafeStop                        every step appends to EventStore
   └▶ Saga.RollbackAsync (newest-first compensations)
```

Non-linear paths that actually occur:
- **verify fails → implement re-runs** with the test output as feedback (`on_failure: rerun_from: implement, max_loops: 2, mode: fix`). Everything downstream of `implement` (docs, test-plan consumers) is invalidated and compensated first. The implementer's own files are **kept**: it re-runs seeing the code the compiler errors point at and is asked for the smallest change. Its compensation stays registered, so a later safe-stop still unwinds every attempt.
- **reviewer requests changes → implement re-runs** once (`max_loops: 1, mode: fix`), same rule.
- **`mode: rollback`** on a loop restores the earlier stage's checkpoint instead, so it starts over with only the feedback. Right when the earlier work is unusable, wrong when it is merely incomplete; it is not the default.
- **human sends a spec/design back** → same stage re-runs with the note as feedback; downstream stages have not started, so nothing to invalidate.
- **policy blocks an exit gate** → the stage retries with the block reason as feedback, inside its retry budget.
- **an artifact is re-produced with a different hash** while stages that consumed it are already complete → `Coordinator.ReactToArtifactsAsync` invalidates them. This path always restores: work built on a stale input is not worth keeping, unlike work that merely failed a test.

The escalation ladder for a failing implementation is therefore: retry inside the stage (transient or malformed output) → fix loop with feedback, files kept (a verdict) → loop budget exhausted → safe-stop, full saga rollback → human. Each rung is bounded in the workflow file; none is chosen by an agent.

### Governance
| Mechanism | Where | What it enforces |
|---|---|---|
| Platform boundary | `prompts/_shared.md`, `prompts/requirements.md` | The pipeline delivers .NET 9 solutions only. A requirement needing another toolchain becomes `AMB-1 PLATFORM:` with re-scope/stop options and halts at `approve-spec`, instead of failing at `verify` after five stages of work. |
| Approval gates | `workflows/sdlc.yaml` `approval:` + `ApprovalGate` | Humans sign off the spec, the design and the release. Decisions get ids and are cited downstream. |
| `no-secrets` | `Governance/Policies/NoSecretsPolicy.cs` | No credential-shaped strings in artifacts or changed files. |
| `pii-in-logs` | `PiiInLogsPolicy.cs` | No log statement interpolates a full URL, IP or user agent. |
| `schema-change-needs-approval` | `SchemaChangeNeedsApprovalPolicy.cs` | Any DDL must name a table the *approved* design mentions. |
| `segregation-of-duties` | `SegregationOfDutiesPolicy.cs` | The reviewer role ≠ the implementer role. |
| Sandbox | `Workspace/FileWorkspace.cs` | Agents cannot read or write outside `workspace/<run>/`. |
| Tool allow-list | `Tools/` | No shell; only read/write/list/grep and `dotnet build`/`dotnet test`. |
| Bounded everything | `RetryPolicy`, `on_failure.max_loops`, `AgentToolLoop` iteration cap, provider retry cap | No unbounded loops anywhere. |
| Safe stop | `Engine/SafeStop.cs` | One switch cancels agents, then `Saga` rolls back completed stages newest-first. |
| Audit | `Governance/AuditLog.cs` | Governance events written with a hash chain; `sdlc verify-audit` detects edits. |

### State and observability
The `EventStore` is the source of truth: append-only, written to `runs/<id>/events.jsonl`
before it is visible in memory. `RunStatus`, `Lineage`, `ReliabilityMetrics`, the audit log, the
dashboard and the console renderer are all projections of it. Agent activity (`ModelCallStarted`,
`AgentTurn`, `ToolStarted`, `ToolInvoked`, `ProviderRetry`) is in the same stream, which is how the
dashboard shows what each agent is doing while it does it.

Metrics per run: stage success rate, retries, fallbacks, re-plans, rollbacks, policy blocks, human
rejections/revisions, MTTR (first failed attempt → completion), per-stage and end-to-end latency.

## 3. Agents

| Role | Backed by | Reads | Produces | Tools |
|---|---|---|---|---|
| requirements | LLM | requirement (+ repo map & impact analysis when code exists) | `spec` (JSON: scope, Given/When/Then criteria, ambiguities with options, assumptions) | read-only |
| architect | LLM | spec | `design` (markdown: components, data flow, schema changes, ADRs, impacted files, risks) | read-only |
| planner | LLM | spec, design | `plan` (JSON work items: key, AC ids, depends-on, risk, files) — validated as a DAG | read-only |
| implementer | LLM | spec, design, plan (+ feedback) | `implementation` (summary + files changed, computed by the orchestrator) | read/write/build/test |
| test-designer | LLM | spec, design, plan | `test-plan` (independent test cases per AC, reviewer checklist) | read-only |
| verifier | **deterministic** | — | `test-report` from `dotnet build` + `dotnet test`; failure → feedback | build/test |
| reviewer | LLM | spec, design, implementation, test-plan, test-report | `review` ending in `VERDICT: APPROVE` or `REQUEST_CHANGES` | read-only |
| doc-writer | LLM | spec, design, implementation | `documentation` (feature doc + runbook written into the workspace) | read/write |
| release-manager | LLM | everything above | `change-record` (JSON: risk rating, blast radius, rollback plan, evidence, checklist) | read-only |

Every LLM role shares one loop (`AgentToolLoop`): send system prompt + conversation + tool
definitions, execute all requested tools, return results in one message, repeat until the model
stops or the iteration cap is hit. Prompts are files under `prompts/`, one per role, plus
`_shared.md` and the repository's own `CLAUDE.md` — the same conventions a human contributor reads.

Provider access is raw `HttpClient` against Anthropic, OpenAI and Gemini (no SDKs), behind
`ILlmClient`. `RecordingClient` wraps any of them: live runs write every exchange to
`recording/llm/`, replays match by request hash and fall back to sequence order (reported) when a
tool result drifted. Assistant turns are stored as the provider's raw content so thinking blocks
and function-call ids round-trip unchanged.

## 4. Codebase reasoning (brownfield)

No embeddings. `RepoMap` parses every `.cs` file with Roslyn syntax trees (no compilation) into
type signatures, public members and referenced identifiers. `ImpactAnalysis` scores files from
seed terms pulled out of the requirement (backticked identifiers weigh more than prose words) and
pulls in second-order files that reference the hit types. It is deterministic, so the walkthrough
is reproducible. Both sit behind `ICodebaseIndex`; for a large repository an embedding-backed index
would replace `RoslynCodebaseIndex` without touching any agent. See `tradeoffs.md`.

## 5. Runs, presets and recordings

A run is `RunRequest` → `RunSpec` (requirement text, baseline, workflow) → `runs/<id>/`:

```
runs/<id>/
  run.json          what was asked (so the run can be replayed by id)
  events.jsonl      the log; everything else derives from it
  audit.jsonl       governance events with the hash chain
  artifacts/        spec.json, design.md, plan.json, implementation.md, test-plan.md, …
  output/           every workspace file the agents created or changed
  recording/        llm/*.json exchanges + decisions.jsonl (live runs only)
  metrics.txt, lineage.mmd
```

Presets in `scenarios/` are the three assessment cases (requirement file + baseline). A live preset
run also publishes its recording to `recordings/<scenario>/`, which is committed so a grader can
replay it with no key. An ad-hoc requirement typed into the dashboard runs the same graph.

Baselines: `scaffold` (build configuration only — greenfield), `git:v1-legacy` (the shortener
with synchronous click counting — brownfield), `git:HEAD` or any tag.

## 6. The product: URL shortener

`Shortener.Core` (domain + ports, no packages) · `Shortener.Infrastructure` (in-memory, Postgres
via Npgsql/Dapper with a Polly pipeline, Redis) · `Shortener.Api` (minimal API: create, redirect,
stats, health probes; Serilog JSON, OpenTelemetry, correlation ids, rate limiting).
`docs/openapi.yaml` is the contract. v1 (`v1-legacy`) records clicks with a synchronous row update
on the redirect path — measured at 293 rps / p99 684 ms against real Postgres — which is the
problem the brownfield scenario asks the agents to fix with an outbox and a consumer.

## 7. Key decisions

See `PLAN.md` §0 (D1–D10) and `docs/adr/`.
