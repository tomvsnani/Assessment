# ADR-002: Live LLM calls, provider-agnostic, with committed recordings for replay

**Status**: accepted · **Date**: 2026-09-13

## Context
A prototype whose agents are scripted proves nothing about agentic execution; one that needs an
API key to demonstrate anything cannot be evaluated by a grader without spending money.

## Decision
Agents call a real model through raw `HttpClient` adapters (`AnthropicClient`, `OpenAiClient`,
`GeminiClient`) behind `ILlmClient`. `RecordingClient` records every exchange of a live run and
replays it later by request hash, falling back to sequence order when an input drifted (and
reporting that it did). Human decisions are recorded the same way and replayed with the actor
suffixed `[recorded <date>]`. No staged failures, no scripted rationale.

## Consequences
- `sdlc run <scenario>` works with no key; `--live` works with any of three providers.
- Recordings are large JSON files in the repository; they are the evidence, so that is accepted.
- Replay is faithful only while prompts and the baseline are unchanged; the fidelity counter on
  the dashboard says how exact a replay was.
