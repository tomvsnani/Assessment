# ADR-007: The orchestrator is an ASP.NET Core service with a dashboard and a headless mode

**Status**: accepted · **Date**: 2026-09-14 (supersedes the console-only CLI)

## Context
Controlled autonomy and observability are graded; a console log shows them poorly. The target
role builds ASP.NET Core services.

## Decision
`Orchestrator.Host` exposes REST + Server-Sent Events, serves a vanilla-JS dashboard that shows
the stage graph, live agent activity, artifacts, policy verdicts, metrics, lineage and the audit
chain, and takes approval decisions in the browser (`WebApprover`). `RunService` is the single
composition root; `sdlc run ...` uses it headlessly for CI and for graders who prefer a terminal.

## Consequences
- Every run is inspectable live and after the fact from the same page.
- One more project and about 600 lines of front-end; no framework, no build step.
