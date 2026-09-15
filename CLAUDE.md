# Working conventions for this repository

These instructions are read by Claude Code when a human works here, and are injected into every
orchestrator agent's system prompt (see `prompts/_shared.md`). Same rules for people and agents.

## What this repo is
An agentic SDLC orchestrator (`src/Orchestrator.*`) that drives the development of a URL shortener
(`src/Shortener.*`). `PLAN.md` explains the decisions; `TRACEABILITY.md` maps assessment
requirements to code and evidence. Read both before changing anything structural.

## Code rules
- .NET 9, nullable on, warnings are errors. `dotnet build` must be clean before any commit.
- One concept per file. A file named `Scheduler.cs` contains `Scheduler` and nothing else.
- One folder per concern. Do not add a `Helpers/` or `Utils/` folder; name the concern.
- `Program.cs` only wires services; behaviour lives in named classes.
- Records for data, classes for behaviour. No mutable public setters on domain types.
- Ports (interfaces) live in `<Product>.Core/Ports` (`Shortener.Core/Ports` here). Core has zero package references.
- Every public behaviour has a test in the matching `tests/*Tests` project, named `Given_When_Then` style.
- Structured logging only (`logger.LogInformation("Created {Code}", code)`), never string interpolation.
- Configuration comes from environment variables / `appsettings.json`, never from code constants.

## Things agents must never do
- Write outside `workspace/` (agents) — the sandbox enforces it, but do not try.
- Put secrets, keys or tokens in any file. `NoSecretsPolicy` blocks the stage if you do.
- Log a full target URL, a raw IP address or a user agent string. `PiiInLogsPolicy` blocks it.
- Change a database schema without an approved change record.
- Review your own implementation. Reviewer and implementer are different agent roles.

## When a requirement is unclear
Do not guess silently. List the ambiguity, propose 2–3 options with trade-offs, and stop for a
human decision. The decision is recorded and cited by every later stage.
