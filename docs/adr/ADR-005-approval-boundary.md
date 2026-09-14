# ADR-005: Agents decide engineering questions; humans decide business questions

**Status**: accepted · **Date**: 2026-09-14

## Context
Early runs asked the human to choose the redirect status code. That is settled engineering
practice for a tracking shortener, and asking about it is noise that erodes trust in the gates
that matter.

## Decision
The requirements prompt instructs the agent to settle engineering choices itself and record them
as assumptions with a rationale (the human can veto), and to raise as ambiguities only questions
whose answer depends on business intent, each with options, trade-offs and a recommendation.
Approval gates exist at three high-impact points (spec, design, release), not per question.
Ambiguities are resolved at the spec gate.

## Consequences
- The second live spec asked two business questions (dedupe vs. new code per request; base URL
  from config vs. headers) and settled seven engineering choices itself.
- "Ambiguous" is not a scenario label handed to the agents; it is what the requirements agent
  finds. The ambiguous preset is presented as an ordinary request.
