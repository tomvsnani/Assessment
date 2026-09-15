# Testing approach

Two systems are tested: the orchestrator (does the runtime do what the assessment says it must?)
and the shortener (is the product production-grade?). Both run with `dotnet test`, no key, no
Docker; Docker-backed tests opt in.

## Orchestrator — `tests/Orchestrator.Tests` (96 tests)

The engine is tested **without any model**: `FakeAgents` are delegates that script exactly what a
stage does, `ScriptedApprover` plays the human, `InMemoryWorkspace` is the sandbox. `TestRun` wires
the real `Scheduler`, `Executor`, `Coordinator`, `Saga`, `SafeStop`, `PolicyGate`, `ApprovalGate`
and `EventStore` around those fakes, so every test reads as "given this workflow and these agents,
when run, then these events happened".

| Assessment claim | Test(s) that prove it |
|---|---|
| Explicit dependency graph, cycle/unknown-dependency validation | `DependencyGraphTests` (9) |
| YAML workflow parsing (gates, retry, fallback, on_failure incl. `mode`) | `WorkflowLoaderTests` (6) |
| Sequential + parallel execution with synchronization | `SchedulerTests`: branches provably overlap; `max_parallel: 1` never overlaps; join waits for both |
| Cross-stage context flows downstream | `SchedulerTests.Given_linear_workflow…artifacts_flow_downstream` |
| Bounded retries with feedback, fallback agent, exhaustion | `RetryFallbackTests` (6) |
| A retried attempt keeps the previous attempt's files; a stage that fails outright restores its checkpoint | `RetryFallbackTests…next_attempt_sees_those_files`, `…restored_to_the_stage_start` |
| Rollback on failure and on Ctrl+C; workspace restored | `SchedulerTests` rollback + safe-stop tests |
| Human rejection stops the run; revision re-runs with notes; revisions do not consume retries | `GovernanceFlowTests` |
| Ambiguity resolutions are written into the spec and cited as decisions | `GovernanceFlowTests…resolves_it_at_approval…` |
| Policy block becomes feedback and a retry; persistent block fails the run | `GovernanceFlowTests` secret tests |
| Safe stop while a human approval is pending ends cleanly | `SafeStopDuringApprovalTests` |
| Re-planning: verify fails then implement re-runs with test output; docs rebuilt; loop budget respected | `ReplanTests` (5) |
| Invalidation when an upstream artifact changes under completed stages | `ReplanTests…coordinator_invalidates_them` |
| Fix-mode loop keeps the implementer's files; rollback-mode restores them; exhausted fix loops still unwind on safe-stop | `ReplanTests…previous_files_still_present`, `…from_a_clean_checkpoint`, `…still_unwinds_every_attempt` |
| Four policies: block/pass cases, placeholders not flagged | `PolicyTests` (21) |
| Audit hash chain verifies and detects tampering | `AuditLogTests` |
| Event log round-trips; projections rebuild status and lineage | `EventStoreTests` |
| Metrics: retries, fallbacks, re-plans, MTTR | asserted inside `RetryFallbackTests`, `ReplanTests` |
| Record/replay: exact match, sequence fallback reported, missing recording fails loudly | `RecordingClientTests` |
| Agent loop: tool results fed back, unknown tool is an error not a crash, iteration cap | `AgentLoopAndParsingTests` |
| Artifact tag parsing, fence stripping; recovery of a tag-less single document, refusal of prose/empty/multi-fence | `AgentLoopAndParsingTests` |
| Anthropic wire format, thinking blocks echoed, 429 is transient | `AnthropicClientTests` |
| Gemini wire format; a 200 with no candidates / no content / empty parts is a transient failure | `GeminiClientTests` |
| Roslyn repo map, deterministic impact ranking | `CodebaseIndexTests` |
| Sandbox refuses paths outside the root; checkpoint/restore; build outputs ignored | `FileWorkspaceTests` |

Not unit-tested, verified by running: the dashboard (manual + browser), SSE streaming, the live
provider path (run directories and recordings prove it; see the [scenario walkthroughs](scenarios/README.md)).

## Shortener — `tests/Shortener.UnitTests` (39) and `tests/Shortener.IntegrationTests` (14, 4 need Docker)

- Unit: `ShortCode` validation, `LinkPolicy` (SSRF hosts, schemes, length), `CreateLinkHandler`
  (collisions, aliases, idempotency, TTL), `LinkResolver` (cache hit/miss, expiry, click recorded),
  `RandomShortCodeGenerator` (uniqueness over 10k, bounds).
- Integration: the real API in-process via `WebApplicationFactory` with in-memory storage —
  201/302/404/409/400, idempotency replay, correlation-id echo, health probes.
- Postgres: `Testcontainers` against `postgres:16`, gated by `RUN_DOCKER_TESTS=1` so the default
  run needs no Docker. These caught a real bug (Dapper column mapping) that in-memory tests could
  not.
- Load: `load/Shortener.LoadTest` hammers the redirect path and enforces the SLO (p99 ≤ 20 ms) via
  exit code. Measured: in-memory 37,946 rps / p99 3.6 ms; real Postgres 293 rps / p99 684 ms — the
  brownfield motivation.

## Verification inside the lifecycle

The orchestrator's `verify` stage is itself a test run: the deterministic `VerifierAgent` builds
and tests the workspace the implementer produced. A failure is routed back to the implementer
with the output as feedback (`on_failure: rerun_from: implement`). The `test-designer` runs in
parallel with the implementer and never sees its code, so its test plan is an independent check
the reviewer works through.

## CI

`.github/workflows/ci.yml`: restore, `dotnet format --verify-no-changes`, build with warnings as
errors, `dotnet test`, upload `.trx` results.

## Running

```bash
dotnet test                                                   # everything that needs no Docker
RUN_DOCKER_TESTS=1 dotnet test tests/Shortener.IntegrationTests  # + Postgres in a container
dotnet run --project src/Orchestrator.Host -- run greenfield  # replay a recorded lifecycle end to end (needs recordings/greenfield)
```
