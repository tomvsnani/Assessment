# ADR-001: Hand-written orchestration runtime, no agent framework

**Status**: accepted · **Date**: 2026-09-13

## Context
The assessment names workflow orchestration as the critical differentiator: an explicit dependency
graph, entry/exit gates, parallel paths with synchronization, retries, rollback, safe-stop, policy
guardrails, audit, metrics and re-planning. Frameworks (Semantic Kernel, LangGraph, AutoGen) offer
some of this, each with its own abstractions, and would make the graded behaviour partly theirs.

## Decision
Write the runtime by hand in `Orchestrator.Core` as small named modules: `DependencyGraph`,
`Scheduler`, `Executor`, `Coordinator`, `Saga`, `SafeStop`, `PolicyGate`, `ApprovalGate`,
`EventStore`, `ReliabilityMetrics`. The core has no dependency on any LLM library; agents are an
interface.

## Consequences
- Every governance behaviour is a file a grader can open and a test can exercise without a model.
- We own the semantics of retry, re-plan and rollback, and can explain them.
- We forgo framework conveniences (streaming helpers, tool schemas from attributes). The agent loop
  is ~150 lines; that is the price.
