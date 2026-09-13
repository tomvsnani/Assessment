namespace Orchestrator.Agents.Codebase;

/// <summary>
/// How agents learn about an existing codebase. The shipped implementation parses the workspace
/// with Roslyn and does deterministic keyword/reference scoring — right-sized for a repo of a few
/// thousand lines. For a large monorepo this is the seam where an embedding-backed index would go;
/// the agents would not change. See docs/tradeoffs.md ("RAG deferred").
/// </summary>
public interface ICodebaseIndex
{
    /// <summary>Compact listing of every file's types and public members, for the prompt.</summary>
    RepoMap Map();

    /// <summary>Files most likely affected by a change described by the given terms, with reasons.</summary>
    ImpactReport Analyze(IReadOnlyList<string> seedTerms);
}
