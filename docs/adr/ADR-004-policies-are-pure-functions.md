# ADR-004: Policies are pure functions over a context, evaluated at gates

**Status**: accepted · **Date**: 2026-09-13

## Context
Guardrails for security, compliance and change control must be reproducible, testable and visible
in the audit log; asking a model to police itself is none of those.

## Decision
`IPolicy.Evaluate(PolicyContext)` returns a `PolicyVerdict` and performs no I/O. Gates name policies
in YAML. Every verdict is an event. A `Block` at an exit gate becomes feedback to the agent and a
retry, within the stage's bounded budget.

## Consequences
- 21 unit tests cover the four policies with no model involved.
- Policies can only see what the context carries (artifacts, changed files, decisions, roles);
  that is deliberate. A policy that needs the network is not a guardrail.
