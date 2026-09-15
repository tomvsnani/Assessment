# Runbook — orchestrator host

## Start

```bash
dotnet run --project src/Orchestrator.Host            # dashboard + API on http://localhost:5100
dotnet run --project src/Orchestrator.Host -- run greenfield   # headless replay, exit code 0/1
dotnet run --project src/Orchestrator.Host -- graph            # print the stage graph and gates
```

Configuration is environment only:

| Variable | Effect | Default |
|---|---|---|
| `ANTHROPIC_API_KEY` / `OPENAI_API_KEY` / `GEMINI_API_KEY` | Enables live runs on that provider. Never written to disk. | none — replay only |
| `LLM_PROVIDER` | Which provider a live run uses when the request does not say. | first provider with a key |
| `LLM_MODEL` | Model override for that provider. | provider default (`claude-opus-5`, `gpt-5`, `gemini-2.5-flash`) |
| `SDLC_APPROVER` | Actor name recorded for console approvals. | OS user name |
| `urls` (appsettings) | Listen address. | `http://localhost:5100` |

## Health

| Signal | Meaning |
|---|---|
| `GET /health/live` 200 | Process is up. |
| `GET /api/runs` | Lists runs; a 500 here means a run directory is unreadable (see below). |
| Dashboard "Right now" panel | Per running stage: calling the model (turn N), running `dotnet test`, waiting for a human, or waiting out a provider rate limit. If it shows nothing while status is *running*, the scheduler is between stages. |
| Console/JSON log lines `provider 429; retrying in Ns (attempt k/8)` | The provider is rate-limiting. The run continues automatically; eight attempts then the stage fails and the executor's own retry applies. |

## Common failures

| Symptom | Cause | Remedy |
|---|---|---|
| Run fails at `requirements` with `HTTP 400 … credit balance` (Anthropic) or `429 … free_tier_requests, limit: N` (Gemini) | No paid quota on the key | Add credits / enable billing, or start with `provider: openai\|gemini` and a key that has quota. Nothing on disk needs cleaning. |
| `Live mode needs X_API_KEY in the environment` | Started without a key for the chosen provider | Set the variable in the host's environment and restart the host. |
| `No recording to replay at …` | Replaying a preset that was never run live | Run it live once, or replay a finished run by id. |
| `No recording left for '<role>' … input drifted` during replay | Prompts or baseline changed since the recording | Re-record with a live run. The fidelity counter shows how much was replayed by sequence. |
| Stage stuck at "waiting for a human decision" | An approval gate is open | Decide in the dashboard; nothing proceeds until then. Safe stop aborts and rolls back. |
| Dashboard blank | `GET /api/runs` failing | Check the host log for the failing run directory; delete it under `runs/` if corrupt. |
| `dotnet build` fails while the host runs | The host locks its DLLs | Stop the host (Ctrl+C, or `taskkill /IM sdlc.exe /F` on Windows) before rebuilding; `dotnet run` without `--no-build` rebuilds on start. |
| Run fails at a stage with `Final message did not contain <artifact …>` twice | The model answered with prose or several documents and no wrapper | A single fenced/JSON document is recovered automatically; otherwise re-run, or switch model. The retry feedback shows the model the exact wrapper. |

## Stopping and rolling back

- **Safe stop** (dashboard button, `POST /api/runs/{id}/stop`, or Ctrl+C in headless mode):
  running agents are cancelled at the next await, completed stages are compensated newest-first
  (workspace restored to each stage's checkpoint, artifacts dropped), `RollbackCompleted` and
  `RunFailed` are written. The run directory stays for inspection.
- Nothing outside `workspace/<run>/` and `runs/<id>/` is ever written by a run.

## Audit

```bash
dotnet run --project src/Orchestrator.Host -- verify-audit runs/<id>
```

Prints `INTACT` with every human decision, or `TAMPERED: chain breaks at entry seq=N`.

## Housekeeping

`runs/` and `workspace/` are git-ignored and safe to delete between sessions. `recordings/` is
committed and is the replay evidence; a live preset run that **succeeds** overwrites its
scenario's recording, a failed one does not.
