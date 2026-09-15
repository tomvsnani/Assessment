# Trade-offs, risks and limitations

Honest list. Each entry says what was chosen, what it costs, and what would change it.

## Orchestration

| Choice | Cost | When to revisit |
|---|---|---|
| Hand-written runtime, no framework (ADR-001) | We maintain the loop, retries, tool schemas ourselves. | If the graph needed durable execution across process restarts, a workflow engine (Temporal, Durable Task) would earn its place. Today a run lives in one process; a crash mid-run leaves the log but not a resumable state. |
| One process per host, runs in memory | No horizontal scaling of the orchestrator itself; two concurrent runs share the machine's `dotnet build`. | Fine for a team tool. A queue + worker split is the obvious next step. |
| Workspace checkpoints are directory copies | Simple and correct; O(size) per checkpoint. The shortener is small. | Switch to `git stash`/worktree checkpoints for a large repo. |
| Compensation = restore workspace + drop artifacts | Rollback undoes files and artifacts, not external side effects. | The lifecycle has no external side effects by design (no deploys, no PRs). Adding them means adding compensations. |
| Re-plan only along the graph (rerun_from / invalidate downstream) | The coordinator cannot *change* the graph, only re-run parts of it. | A model-planned graph would be more flexible and far less governable; the fixed graph is a feature for change control. |
| Verdict loops keep the implementer's files (`on_failure.mode: fix`) | A second attempt can carry a bad structure forward instead of rethinking it; the loop budget is what stops that. | The first version restored the checkpoint on every loop. Watching a run showed why that is wrong: the implementer got `error CS0246 in Foo.cs:12` as feedback with `Foo.cs` already deleted, and re-derived everything from the plan. Rewriting is how you get new bugs; a change-controlled team wants the smallest diff a reviewer can read. `mode: rollback` is still there for loops where the earlier work is genuinely unusable. |
| The pipeline builds and verifies with `dotnet` only; the requirements stage raises any other runtime as `AMB-1 PLATFORM:` and stops at the spec gate | A React/Python/mobile requirement cannot be delivered here, even though the LLM would happily write the files. | Observed before the rule existed: "build a React gym app" ran six stages and burned the whole verify→implement budget on `MSB1003: no project or solution file`. An agent that attempts anything is an agent you cannot trust with anything; the boundary is declared in `prompts/_shared.md`, enforced up front, and the human picks re-scope (API / Blazor) or stop. Widening it is a port (`IBuildRunner` with an `npm` adapter), not a prompt tweak, and out of scope for a .NET delivery pipeline. |
| Attempts within a stage share the workspace | Attempt 2 inherits attempt 1's half-finished files, good and bad; the exit gate therefore evaluates policies over everything changed since the stage started, not since the attempt. | Seen live: a green build followed by an empty model response was treated as a failed attempt, and the retry restored the checkpoint and rebuilt from scratch. A retry is for the message, not the work. |
| Gemini "200 with nothing usable" is a transient provider failure | We retry (with the provider back-off) instead of failing the stage; a persistently blocked prompt still fails after the provider-attempt budget. | Seen live twice in one run: a candidate with empty parts (agent "finished" without its artifact) and a candidate without `content` (`KeyNotFoundException`, stage failed, run rolled back). The other providers do not do this. |
| Bounded loops everywhere | An implementer that needs 70 turns gets cut at 60 and asked to finish. | Tune per role in `LlmAgent.MaxIterations`; the cap is what makes cost predictable. |

## Agents and models

| Choice | Cost | When to revisit |
|---|---|---|
| `write_file` for new files, `edit_file` (exact unique match) for changes | An edit whose `old_string` is not unique is refused; the model must quote more context. | Added after `greenfield-20260915-163443`: the implementer re-sent an 11 KB `Program.cs` whole on every fix. |
| Policies are pre-checked in the write tools, enforced at the exit gate | Two evaluations per file; the pre-check sees one file at a time so cross-file policies (schema change, segregation of duties) stay gate-only. | Same run: 46 turns of work, then `pii-in-logs` blocked at the gate with no retry left. Now the agent hears it in the next turn. |
| Implementer builds per project, not per work item | A compile error surfaces a few files later than it could. | Same run: the implementer spent 15.7 of 24 minutes in 147 model calls; every `run_build` is a turn with a 40–75K-token context. |
| No streaming of model output | The dashboard shows "calling the model, 40s" rather than tokens as they arrive. | Provider SSE streaming is a contained change in each client. |
| Model choice is cost-driven | Opus-class models produce better code; Flash/Sonnet-class models are 2–10x cheaper. | Per-role routing (cheap model for planner/doc-writer, strong model for architect/implementer/reviewer) is the next lever. |
| Free-tier keys | Daily request caps (20/day on some Gemini models) can stall a run mid-lifecycle. | Pay-as-you-go on any provider removes the stall; the run itself is unchanged. |
| Prompts are files, cached in the system prompt | Changing a prompt invalidates exact replay of old recordings (sequence fallback still works and is reported). | Accepted; prompts are versioned in git with the recordings they produced. |
| Replay matches by request hash, falls back by sequence | A drifted tool result (build timings, timestamps) makes replay approximate, not exact. | The fidelity counter is shown on every replay. Normalising volatile tool output would raise exactness. |

## Codebase reasoning

| Choice | Cost | When to revisit |
|---|---|---|
| Roslyn repo map + keyword impact analysis, no embeddings (ADR-006) | Recall depends on the requirement mentioning the right words; the agent compensates with `grep`. | A monorepo. `ICodebaseIndex` is the seam; nothing above it changes. |
| Syntax-only parsing (no compilation) | No semantic symbol resolution; references are identifier matches. | Full Roslyn compilation would give exact references at the cost of building the workspace first. |

## Governance

| Choice | Cost | When to revisit |
|---|---|---|
| Policies are regex/structure checks | A policy can only catch what a pattern can express; `no-secrets` is high-precision by design, so a novel secret format passes. | Add patterns as they are found; or add a scanner tool behind the same `IPolicy`. |
| Schema-change policy checks table names against the approved design text | A design that mentions a table in passing "authorises" it. | Make the architect emit a structured schema section and check that instead. |
| Segregation of duties by *role*, not by model | Reviewer and implementer are different prompts, possibly the same model. | Route the reviewer to a different model/provider — a one-line change in `AgentRegistry`. |
| Audit hash chain, no signing | Tamper-evident against edits of the file, not against someone regenerating the whole chain. | Anchor the chain head externally (a commit, a timestamping service). |
| Approvals via a local dashboard, no auth | Anyone who can reach `localhost:5100` can approve; the actor name is self-declared. | Put the host behind the company SSO; the `IApprover` port does not change. |

## The shortener

| Choice | Cost | When to revisit |
|---|---|---|
| v1 counts clicks synchronously on the redirect path | Measured: 293 rps / p99 684 ms against real Postgres. | This is deliberate: it is the brownfield scenario's problem. v2 (outbox → consumer) is what that run produces. |
| Schema created at startup with `CREATE TABLE IF NOT EXISTS` | No migration history. | One table does not justify a migration tool; the brownfield change (an outbox table) is where one would be introduced. |
| No authentication on the API | Internal tool assumption. | Out of scope by the requirement; the ambiguous preset touches retention, not auth. |
| Random base62 codes, not sequential/hashid | 7 chars and a collision retry loop instead of a counter. | Deliberate: sequential codes are enumerable. |

## Known limitations of this submission

The full, dated list is in [engineering-summary.md §7](engineering-summary.md#7-limitations--stated-not-hidden). In short:

- Recordings exist only for the scenarios that completed a live run; the
  [walkthroughs](scenarios/README.md) say which have one, which model produced it and how exact
  the replay is.
- The orchestrator has no persistence beyond the run directory; restarting the host loses
  in-flight runs (their logs remain and are inspectable).
- The dashboard is functional, not designed: vanilla JS, no framework, no tests.
- Cost was a real constraint during development (see the engineering summary); the runs were
  made on the cheapest capable models available at the time.
