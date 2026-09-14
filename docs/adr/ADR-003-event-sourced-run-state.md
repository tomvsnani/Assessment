# ADR-003: The run is an append-only event log; everything else is a projection

**Status**: accepted · **Date**: 2026-09-13

## Context
The assessment asks for cross-stage context, decision lineage, audit-grade traceability and
reliability metrics. Keeping those as separate mutable structures invites drift.

## Decision
`EventStore` appends immutable `RunEvent`s and writes each to `events.jsonl` before it is visible.
`RunStatus`, `Lineage`, `ReliabilityMetrics`, the `AuditLog` (hash-chained subset), the console
renderer and the dashboard are all projections. Agent activity (model calls, tool calls, provider
retries) is in the same stream.

## Consequences
- A finished run can be inspected, replayed and audited from its directory alone.
- Metrics are identical for live and replayed runs.
- `RunState` exists as an in-memory convenience for the scheduler; it holds nothing that is not
  also in the log.
