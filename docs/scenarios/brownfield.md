# Brownfield — move click counting off the redirect path

| | |
|---|---|
| Preset | [`scenarios/brownfield.yaml`](../../scenarios/brownfield.yaml) |
| Requirement | [`requirements/brownfield.md`](../../requirements/brownfield.md) — `REQ-BF-1` |
| Baseline | `git:v1-legacy` — the shortener with a synchronous `UPDATE links SET clicks = clicks + 1` on every redirect |
| Human gates | `approve-spec`, `approve-design`, `approve-release` |
| Policies that matter here | `schema-change-needs-approval` (an outbox table is new DDL), `pii-in-logs`, `no-secrets`, `segregation-of-duties` |

## 1. The requirement

> Production incident follow-up. Under load, `GET /{code}` latency climbs because every redirect
> does a synchronous `UPDATE … clicks + 1` before responding, and hot links serialise on that row.
> The redirect must not wait for analytics. … record the click as an **event** processed
> asynchronously … transactional outbox in the same database … drained by a background publisher
> to a Kafka topic (`clicks`). A consumer (`Shortener.Analytics`) … maintains per-code totals.
> Constraints: the redirect path may not make any network call other than the cache/repository
> read; `GET /links/{code}/stats` keeps its contract but may be eventually consistent; everything
> must still run with no Docker; existing `clicks` totals must not be lost; do not change `POST /links`.

What makes it brownfield: there is a working system with tests, a public contract and a measured
problem. The incident is real — `load/Shortener.LoadTest` against Postgres + Redis measured
**293 rps, p99 684 ms** on v1 (SLO: p99 ≤ 20 ms), against 37,946 rps / p99 3.6 ms in-memory. The
hot-row update is the difference. This is also the JD's "legacy → event-driven with Kafka"
modernization in miniature.

## 2. What to watch for

- **Codebase reasoning.** Before the spec is written the requirements agent and the architect
  receive a Roslyn **repo map** (every type, its public members, what it references) and a
  deterministic **impact analysis** seeded from the requirement's identifiers (`GET /{code}`,
  `clicks`, `ILinkRepository`, …). The design must name the impacted files and data flows, and
  confirm them with `read_file`/`grep` rather than trusting the keyword scan.
- **Change control.** The outbox is a new table. The design must declare it under
  `## Schema changes`; the `schema-change-needs-approval` policy blocks any DDL in the
  implementation that the *approved* design did not name.
- **Protecting what exists.** The 49 tests that ship with `v1-legacy` must keep passing; `verify`
  runs all of them, not just the new ones. `POST /links` may not change.
- **Eventual consistency, stated.** The doc-writer must document the stats lag; the release
  manager's change record must carry a backout plan (the synchronous path still exists behind
  the port until the consumer is proven).
- **The redirect path stays latency-critical.** The architect prompt forbids any network call on
  it beyond the cache/repository read; the outbox write is a local, same-transaction insert.

## 3. Run evidence

**Status: no live brownfield run has been completed yet.** The greenfield scenario was run first
because every later scenario depends on the same engine, and the fixes it forced (fix-mode
re-plan, attempt-level workspace retention, Gemini response handling, artifact recovery — see
[engineering-summary.md](../engineering-summary.md)) needed to land before a 15-stage brownfield
run had a fair chance. This section is filled from `runs/brownfield-<timestamp>/` when it runs;
nothing is asserted here ahead of that.

What already exists and is verifiable without a run:

- The baseline: `git show v1-legacy --stat`; `BaselineMaterializer` copies it into the workspace
  and writes a `Workspace.sln` so `dotnet test` works there (verified: 49 tests pass in the
  materialised workspace).
- The measured problem: `load/Shortener.LoadTest` and the numbers above.
- The codebase reasoning: `tests/Orchestrator.Tests/Agents/CodebaseIndexTests.cs` runs the repo
  map and impact ranking over real source and asserts the ranking is deterministic.
- The policy: `PolicyTests` cover `schema-change-needs-approval` block/pass cases.

## 4. Validation

- `verify` runs the full pre-existing test suite plus whatever the implementer added.
- `review` checks the design's impacted-files list against what actually changed.
- Policy `schema-change-needs-approval` is the change-control gate for the outbox table.
- The release change record must state blast radius (redirect path, stats endpoint, database)
  and the backout (feature flag back to synchronous counting, table left in place).
- Human: `approve-design` is where the outbox/Kafka shape is accepted or sent back.
