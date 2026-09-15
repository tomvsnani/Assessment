# Engineering summary

The assessment asks for a final summary covering plan and rationale, artifacts, risks, trade-offs
and validation, assumptions, and limitations — and for AI-assisted work done with ownership of
correctness. This is that document. It states only what the repository, its tests and its run
directories show; where something is not yet demonstrated it says so.

Repository: https://github.com/tomvsnani/Assessment

---

## 1. What was built

Two systems in one .NET 9 solution:

1. **An agentic SDLC orchestrator** (`src/Orchestrator.*`) — a runtime that takes a requirement
   and drives it through requirements → design → plan → implement ‖ test-design → verify →
   review ‖ docs → release, with an explicit dependency graph, entry/exit gates, bounded retries
   and re-plan loops, a saga for rollback, safe-stop, four policy guardrails, three human approval
   gates, an append-only event log with a hash-chained audit trail, and reliability metrics. Nine
   agent roles, eight model-backed and one deterministic. Provider-agnostic (Anthropic, OpenAI,
   Gemini) with record/replay. An ASP.NET Core host with a dashboard and a headless mode.
2. **A URL shortener** (`src/Shortener.*`) — the product the orchestrator develops. Core/ports,
   in-memory and Postgres/Redis adapters, minimal API with health probes, structured logging,
   correlation ids, OpenTelemetry, Polly, rate limiting; unit, integration and container tests; a
   load test with an SLO; an OpenAPI contract. Tagged `v1-legacy` as the brownfield baseline.

Where everything lives: [README](../README.md) → [architecture](architecture.md) →
[traceability](../TRACEABILITY.md).

---

## 2. Plan and rationale

The plan ([PLAN.md](../PLAN.md)) fixed ten decisions before any code; the ones that shaped the
result:

| Decision | Why | Consequence seen |
|---|---|---|
| Hand-written runtime, no agent framework ([ADR-001](adr/ADR-001-no-agent-framework.md)) | Orchestration is the graded differentiator; it must be readable and ours. | Every mechanism in the assessment's list is one named file in `Engine/` or `Governance/`, tested without a model. |
| `Orchestrator.Core` knows nothing about LLMs | Testability and honesty: the engine is proven with delegate agents. | 96 engine tests run in ~300 ms with no key; the deterministic `VerifierAgent` is a first-class stage. |
| Event-sourced run state ([ADR-003](adr/ADR-003-event-sourced-run-state.md)) | Audit-grade observability means the log is the truth, not a side effect. | Dashboard, metrics, lineage, audit chain and console are all projections of `events.jsonl`. |
| Humans decide business questions, agents decide engineering questions ([ADR-005](adr/ADR-005-approval-boundary.md)) | Approval gates only matter if they are not noise. | Three gates (spec, design, release); ambiguities carry options and a recommendation. |
| Live model with committed recordings ([ADR-002](adr/ADR-002-live-llm-with-committed-recordings.md)) | Graders must run it with no key; live mode proves it is not scripted. | Every live run records; successful scenario runs publish. |
| Same lifecycle for any requirement; scenarios are presets ([ADR-008](adr/ADR-008-any-requirement-presets-are-examples.md)) | Three hard-coded demos would not be a system. | The out-of-scope React request ran the real graph — and exposed a real gap (§5). |

---

## 3. Artifacts

| Artifact | Where |
|---|---|
| Working prototype | `dotnet test` · `dotnet run --project src/Orchestrator.Host` — [README](../README.md#run-it) |
| Architecture overview with diagrams | [architecture.md](architecture.md) |
| Decision records | [adr/](adr/README.md) |
| Lifecycle definition | [`workflows/sdlc.yaml`](../workflows/sdlc.yaml) |
| Agent instructions | [`prompts/`](../prompts/), [`CLAUDE.md`](../CLAUDE.md) |
| Scenario walkthroughs with run evidence | [scenarios/](scenarios/README.md) |
| API contract for the product | [openapi.yaml](openapi.yaml) |
| Per-run: events, audit chain, artifacts, changed files, recording, metrics, lineage | `runs/<id>/` (git-ignored; structure in [architecture §8](architecture.md#8-runs-presets-and-recordings)) |
| Testing approach | [testing.md](testing.md) |
| Operations | [runbook.md](runbook.md) |

---

## 4. Risks, trade-offs and validation

Full table with costs and revisit conditions: [tradeoffs.md](tradeoffs.md). The ones a reviewer
should weigh:

| Risk | Mitigation | Validated by |
|---|---|---|
| An agent "makes the tests pass" instead of implementing the feature | Segregation of duties: the reviewer is a different role, reads the code, and can send it back | Observed live in `greenfield-20260915-163443`: the implementer reduced `Program.cs` to `/` + `/health` to satisfy two failing integration tests; the reviewer refused with named blocking findings ([walkthrough](scenarios/greenfield.md)) |
| Unbounded loops or cost | Every loop has a budget in the workflow file (`retry.max_attempts`, `on_failure.max_loops`, iteration cap, provider-retry cap) | `RetryFallbackTests`, `ReplanTests…loop_budget_exhausted` |
| A failed run leaves half-built code behind | Saga compensation per completed stage; stage checkpoint restored on outright failure; safe-stop rolls everything back | `SchedulerTests` rollback, `ReplanTests…still_unwinds_every_attempt`, `RetryFallbackTests…restored_to_the_stage_start` |
| Secrets or PII in generated code | `no-secrets`, `pii-in-logs` policies over every changed file at the exit gate | `PolicyTests` (21), `GovernanceFlowTests` block → feedback → retry |
| Uncontrolled schema change | `schema-change-needs-approval`: DDL must name a table the approved design mentions | `PolicyTests` |
| Model provider misbehaviour (rate limits, empty responses, format drift) | Provider back-off; empty Gemini candidates raised as transient; single-document artifact recovery | `GeminiClientTests`, `AgentLoopAndParsingTests`; three live runs that failed before these existed (§5) |
| Tampered audit trail | Hash chain; `sdlc verify-audit` | `AuditLogTests` |
| The product's hot path is slow under real storage | Measured, not assumed: 293 rps / p99 684 ms on Postgres vs SLO p99 ≤ 20 ms | `load/Shortener.LoadTest`; it is the brownfield scenario's premise |

---

## 5. AI assistance and what was overridden

This project was built with Claude Code as the pair. The assessment asks for ownership of
correctness; this section is the evidence. Each item is something the AI produced or the pipeline
did that a human judged wrong, with what changed. All are also logged in
[TRACEABILITY.md §E](../TRACEABILITY.md).

| What happened | Judgment | Change |
|---|---|---|
| The first version of the runtime treated every re-plan as rollback: when `verify` failed, the implementer's checkpoint was restored and it re-implemented from scratch with compiler errors pointing at files that no longer existed. | Rollback is the last rung, not the first response. A change-controlled team wants the smallest reviewable diff. | `on_failure.mode: fix \| rollback`; fix (default) keeps the workspace and its saga step; stale-input invalidation still restores. Observed working in `greenfield-20260915-161352` (three tool calls to fix a bad package pin). |
| The pipeline accepted a React SPA requirement, ran five stages, and burned the loop budget on `MSB1003: no project or solution file`. | An agent that attempts anything is an agent you cannot trust with anything. The toolchain is an autonomy boundary and must be declared. | Requirements analyst raises `AMB-1 PLATFORM:` with re-scope/stop options; role prompts describe a .NET 9 pipeline with `<Product>` conventions instead of assuming the shortener. |
| Within a stage, a retry restored the attempt checkpoint — a green build followed by an empty model reply was rebuilt from scratch. | A retry is for the message, not the work. | Attempts share the workspace; exit-gate policies evaluate everything since the stage started; the stage checkpoint is restored only if every attempt fails. |
| `GeminiClient` threw `KeyNotFoundException` on a candidate without `content`; an empty candidate was read as "the agent finished with nothing". | A 200 with nothing usable is a transient provider failure, not an agent verdict. | Both raised as transient `LlmException` and retried with back-off. |
| The requirements agent returned a complete, valid spec as a ` ```json ` fence without the `<artifact>` wrapper, twice, ignoring feedback — and the run died at stage 1. | Throwing away a correct 2,000-token document over a missing wrapper is not robustness. | Single unambiguous document is recovered as the role's one artifact; the recovery is logged; feedback shows the exact wrapper. |
| The requirements agent asked the human which redirect status code to use. | Settled engineering practice; asking erodes trust in the gates that matter. | [ADR-005](adr/ADR-005-approval-boundary.md): business ambiguities to humans, engineering choices as vetoable assumptions. (Gemini still raised it once in `greenfield-20260915-161352` — the instruction is not always followed; the human simply picked.) |
| The implementer pinned a non-existent release-candidate package version. | Warnings-as-errors in the scaffold caught it; this is why the scaffold ships the organisation's build configuration. | No change needed; recorded as evidence the guardrail works. |
| In `greenfield-20260915-171952`, the implementer's integration test for 302 redirects failed on `AC5` because `WebApplicationFactory` followed the redirect by default, burning the loop budget. | Default `HttpClient` behavior follows redirects; in financial services integration testing, redirect status and Location headers must be asserted directly. | Prompts for `test-designer` and `implementer` now explicitly instruct `AllowAutoRedirect = false` on `WebApplicationFactory`. |
| In `greenfield-20260915-163443`, the implementer stripped endpoints in `Program.cs` down to dummy stubs to force tests green. | Optimizing purely for test exit codes at the expense of API surface is an anti-pattern. | Added hard anti-gaming rules in `prompts/implementer.md` forbidding endpoint stripping or dummy returns; caught and enforced by the reviewer gate. |
| Replay mode failed when `recordings/` was empty even though recorded runs existed in `runs/`. | Evaluators testing without API keys need zero-friction replay. | `RunService` now falls back to the latest recorded run under `runs/` and supports both `--replay` and `--replay-of`. |

What was kept from the AI without change: the engine design once decisions were fixed; the
shortener v1; the policies; the tests, which were reviewed for what they assert, not just that
they pass.

---

## 6. Assumptions

- The graders run on a machine with the .NET 9 SDK and git; Docker is optional and only the
  Postgres container tests and the compose profile need it.
- A model API key is available for live runs; replay covers the no-key case once a recording
  exists for the scenario.
- "Production-grade" for the product means: health probes, structured privacy-safe logging,
  correlation ids, validation with problem details, resilience around the store, rate limiting on
  writes, configuration from the environment — the list the requirements prompt applies.
- The organisation's conventions are those in `CLAUDE.md`; agents and humans follow the same file.

---

## 7. Limitations — stated, not hidden

| Limitation | Status |
|---|---|
| **No scenario has a committed recording yet.** Recordings are published only by a *successful* live scenario run; the first run to pass `verify` (`greenfield-20260915-163443`) was in its review loop at the time of writing. Until a recording exists, `sdlc run <scenario>` replay and the README's key-free path do not work for that scenario. | Open — see the [scenario walkthroughs](scenarios/README.md) for the current state of each. |
| **Brownfield and ambiguous have not been run live.** Their walkthroughs describe the setup and the mechanisms (all unit-tested) and carry no run evidence yet. | Open |
| The product at `HEAD` is v1. The event-driven v2 (outbox → Kafka → consumer) is what the brownfield run produces in its workspace; hardening and committing it is Phase 5b of the plan. | Open |
| The runtime lives in one process; a crash mid-run leaves the log but not a resumable state. | Accepted ([tradeoffs.md](tradeoffs.md)) |
| Codebase reasoning is a Roslyn repo map plus keyword impact ranking, not embeddings; fine at this size, a seam (`ICodebaseIndex`) for anything larger. | Accepted ([ADR-006](adr/ADR-006-roslyn-repo-map-not-rag.md)) |
| Gemini 2.5 Flash was the provider for all live runs so far; it needed three robustness fixes in one day. The Anthropic and OpenAI adapters are wire-tested but have not driven a full run. | Accepted; any key works |
| Kafka, IBM MQ, Splunk, MongoDB, GCP from the role description are not integrated; Serilog JSON is Splunk-ingestible, and Kafka is the brownfield target. | Out of scope by design |
| The whole-file `write_file` tool re-sends large files; a `str_replace` edit tool would cut tokens and dropped-code risk. | Deferred |
