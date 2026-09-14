# ADR-006: Brownfield codebase reasoning uses a Roslyn repo map, not embeddings

**Status**: accepted · **Date**: 2026-09-13

## Context
The shortener is about 2,000 lines. A vector index over it would be ceremony; the assessment asks
for codebase reasoning, impact analysis and architectural understanding, which need precision more
than recall.

## Decision
`RepoMap` parses every C# file with Roslyn syntax trees into type signatures, public members and
referenced identifiers. `ImpactAnalysis` scores files deterministically from terms in the
requirement (identifiers weigh more than prose) and adds second-order files that reference hit
types. Both are behind `ICodebaseIndex`; agents also have `grep` and `read_file` to confirm.

## Consequences
- The impact table in the brownfield walkthrough is reproducible.
- For a monorepo this would not scale; `RoslynCodebaseIndex` is the class to replace.
