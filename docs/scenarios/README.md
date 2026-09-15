# The three scenarios

The assessment asks for three scenarios — greenfield, brownfield and ambiguous — each showing
decomposition, orchestration and validation. They are not three products: all three run the same
lifecycle graph ([`workflows/sdlc.yaml`](../../workflows/sdlc.yaml)) against the same product, and
differ only in the requirement text and the starting workspace. That is deliberate: a grader can
compare how the *orchestration* behaves across the three, not three unrelated codebases.

| Scenario | Requirement | Starting workspace | What it exercises | Walkthrough |
|---|---|---|---|---|
| **Greenfield** | build the shortener from nothing | `scaffold` — build configuration only, no code | decomposition from a spec, the implement → verify → review loops, three approval gates | [greenfield.md](greenfield.md) |
| **Brownfield** | move click counting off the redirect path (outbox → Kafka → consumer) | `git:v1-legacy` — the shortener as it was, synchronous click `UPDATE` | codebase reasoning (repo map, impact analysis), change without breaking existing tests, schema-change policy | [brownfield.md](brownfield.md) |
| **Ambiguous** | "make analytics retention-compliant" — nothing else specified | `git:v1-legacy` | ambiguity detection, options with trade-offs, a human decision that every later stage cites | [ambiguous.md](ambiguous.md) |

Each walkthrough has the same shape so they read side by side:

1. **The requirement** — verbatim, with what makes it this kind of scenario.
2. **What to watch for** — the specific orchestration behaviours this scenario is designed to trigger.
3. **Run evidence** — what actually happened in the recorded run(s), by run id, with the events that prove it. Nothing in this section is asserted without a `runs/<id>/` directory behind it.
4. **Validation** — how the output was checked and what a human overrode.

## How to run one

```bash
dotnet run --project src/Orchestrator.Host                    # dashboard: pick the scenario, tick live
dotnet run --project src/Orchestrator.Host -- run greenfield --live --provider gemini   # headless
dotnet run --project src/Orchestrator.Host -- run greenfield  # replay the committed recording, no key
```

A live run needs a model API key in the environment ([runbook](../runbook.md)). Replay needs a
recording under `recordings/<scenario>/`, which a successful live run publishes automatically.

## A fourth, unplanned scenario: the out-of-scope requirement

During development a React single-page app was requested through the dashboard. The pipeline is a
.NET delivery pipeline (`dotnet build` / `dotnet test` are its only verification tools), so the
run went five stages deep and then failed at `verify` with *no project or solution file*, three
times, until the loop budget ran out (`runs/adhoc-20260915-040442`). That exposed a missing
boundary: the system did not know what it could not do. The requirements analyst now classifies
platform fit first and raises a non-.NET requirement as `AMB-1 PLATFORM:` with re-scope (API /
Blazor) or stop options, halting at the spec gate for a human. See
[tradeoffs.md](../tradeoffs.md) and [engineering-summary.md](../engineering-summary.md).
