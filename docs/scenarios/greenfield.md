# Greenfield — build the URL shortener from nothing

| | |
|---|---|
| Preset | [`scenarios/greenfield.yaml`](../../scenarios/greenfield.yaml) |
| Requirement | [`requirements/greenfield.md`](../../requirements/greenfield.md) — `REQ-GF-1` |
| Baseline | `scaffold` — `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`; **no code** |
| Human gates | `approve-spec`, `approve-design`, `approve-release` |
| Loops | `verify → implement` (≤ 2, fix mode) · `review → implement` (≤ 1, fix mode) |

## 1. The requirement

> We need a URL shortener service for internal links. Engineers paste a long URL and get a short
> one back; anyone who opens the short link is redirected to the original. We also want to see how
> many times each short link was used. It must be a .NET 9 web service with a JSON API … reliable
> enough to put behind our internal load balancer … Start with an in-memory store … structure it so
> PostgreSQL can be plugged in without rewriting the endpoints. `POST /links`, `GET /{code}`,
> `GET /links/{code}/stats`. … Links must not be guessable from one another. Unit tests and an
> OpenAPI description are expected.

What makes it greenfield: the workspace holds only the organisation's build configuration. The
architect's first `list_files` returns three files. Everything else — solution, projects, ports,
adapters, endpoints, tests — must be decomposed from the approved spec and built.

## 2. What to watch for

- **Requirement understanding.** The spec must turn "not guessable" and "how many times" into
  testable acceptance criteria, and separate *business* ambiguities (which the human decides) from
  *engineering* choices (which the agent settles and lists as assumptions).
- **Decomposition.** A plan of work items with dependencies that form a DAG, each tied to
  acceptance-criteria ids, each with a risk rating and the files it will touch.
- **Parallelism.** `implement` and `test-design` run at the same time from the same plan; the
  test designer never sees the implementer's code.
- **The fix loop.** When `verify` fails, the implementer re-runs *with its files still present*
  and the compiler/test output as feedback, and makes the smallest change.
- **Segregation of duties.** The reviewer is a different role from the implementer and reads the
  code independently of the implementer's own summary.

## 3. Run evidence

Every claim below is backed by a directory under `runs/`. Run ids are timestamps.

### `greenfield-20260915-163443` — the first run to pass verification (live, Gemini 2.5 Flash)

| Stage | What happened | Events |
|---|---|---|
| requirements | Spec with **8** acceptance criteria, 14 in-scope / 7 out-of-scope items, 6 assumptions, **2 business ambiguities** (duplicate-URL handling; code length and alphabet). Human approved (`D001`) and chose options (`D002: AMB-1 → B`, `D003: AMB-2 → A`). The chosen options were written back into the spec. | `ApprovalRequested` → `ApprovalDecided` ×3 |
| design | Design approved (`D004`). | `ApprovalDecided` |
| plan | **20 work items** `WI-1 … WI-20`, dependency DAG validated, risk-rated (`WI-11/12/20` high). | `ArtifactProduced plan` |
| implement ‖ test-design | Ran in parallel; both completed. Implementer: 61 turns, build green before finishing. | overlapping `StageStarted` |
| verify #1 | Build succeeded; 26 unit tests passed; **2 integration tests failed**. | `StageFailed verify` |
| **re-plan** | `rerun from implement (1/2, fix)` — `implement invalidated (workspace kept)`. The implementer re-ran with its own files and the test output. | `ReplanTriggered`, `StageInvalidated` |
| verify #2 | **Build and tests green.** First `verify` pass in the project's history. | `StageCompleted verify` |
| docs | Completed. | `StageCompleted docs` |
| review | Attempt 1: malformed output (no artifact tag), retried. Attempt 2: **`REQUEST_CHANGES`** — the reviewer found that to make the two integration tests pass the implementer had reduced `Program.cs` to `/` and `/health` endpoints ("GetRoot_ReturnsHelloWorld") and reported "all tests passing". Blocking findings named the missing `POST /links`, `GET /{code}`, `GET /links/{code}/stats`. | `StageAttemptFailed` ×2 |
| **re-plan** | `rerun from implement (1/1, fix)` with the reviewer's findings; `docs`, `verify`, `implement` invalidated. | `ReplanTriggered` |
| … | *In progress at the time of writing; this section is updated from the run directory when it ends.* | |

Policy evaluations so far: 11, no blocks. Provider: 0 rate-limit retries.

What this run demonstrates that no earlier run did: the deterministic verifier can be gamed by an
implementer that optimises for "tests pass"; the independent reviewer — a different role, reading
the code, with segregation of duties enforced by policy — is what catches it. Controlled autonomy
is the *combination* of gates, not any one of them.

### `greenfield-20260915-161352` — the fix loop, first observed live

- `verify` failed on `NU1603`: the implementer had pinned `Microsoft.AspNetCore.OpenApi` to a
  release-candidate version that does not exist; warnings-as-errors turned it into a build failure.
- Re-plan `rerun from implement (1/2, fix)`, `workspace kept`. The implementer's first three tool
  calls were `read_file Directory.Packages.props` → `write_file Directory.Packages.props` →
  `run_build`. It iterated to **`BUILD SUCCEEDED`** without rewriting anything else.
- Then Gemini returned an empty candidate, and later a candidate with no `content`; the run
  failed. Both were fixed the same day ([tradeoffs.md](../tradeoffs.md), rows "Attempts within a
  stage share the workspace" and "Gemini 200 with nothing usable"). The run is kept as the evidence
  that motivated them.

### `greenfield-20260915-162943` — format drift

The requirements agent returned a complete, valid spec as a ` ```json ` fence with no
`<artifact>` wrapper, twice, ignoring the retry feedback. The run failed at stage 1. The parser now
recovers a single unambiguous document and logs that it did. Kept as evidence.

### Earlier runs (2026-09-14)

Five live runs failed on: a Gemini free-tier `429` quota, a malformed design artifact, a stop from
the dashboard, and — the furthest — `review` requesting changes twice on a build that had been
fixed by a from-scratch rewrite (the behaviour that led to fix mode). Their directories are under
`runs/` for inspection; none produced a recording because recordings are published only on success.

## 4. Validation

- **Automated, inside the lifecycle:** `verify` builds and tests the workspace; `review` reads the
  code against the spec, design, test plan and test report; policies check every changed file
  (`no-secrets`, `pii-in-logs`, `schema-change-needs-approval`) and the reviewer's identity
  (`segregation-of-duties`).
- **Human:** three approval gates. At `approve-spec` the human resolved the ambiguities the agent
  had refused to decide alone.
- **Reference comparison:** the human-owned shortener in `src/Shortener.*` is the yardstick for
  what "production-grade" means here; agent output lives in `workspace/greenfield/` and is
  snapshotted under `runs/<id>/output/`, never committed as the product.
