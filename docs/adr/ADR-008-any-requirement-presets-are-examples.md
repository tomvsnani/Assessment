# ADR-008: Any requirement runs the same lifecycle; the three scenarios are presets

**Status**: accepted · **Date**: 2026-09-14

## Context
The assessment says "transforms a requirement into a reviewable engineering outcome"; three
hard-coded workflows would have demonstrated three demos, not a system.

## Decision
One lifecycle graph (`workflows/sdlc.yaml`). A run takes either a preset (`scenarios/*.yaml`:
requirement file + baseline) or an ad-hoc requirement plus a baseline. Kind (greenfield vs.
brownfield) is inferred from the baseline. Every run persists `run.json` and, when live, its own
recording, so any run can be replayed by id.

## Consequences
- The dashboard composer accepts arbitrary requirements against `scaffold`, any git tag or `HEAD`.
- Presets keep committed recordings for key-free replay; ad-hoc runs keep theirs in `runs/<id>/`.
