using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Codebase;

/// <summary>Reads every .cs file in the workspace once per call; the workspace is small and changes between stages.</summary>
public sealed class RoslynCodebaseIndex(IWorkspace workspace) : ICodebaseIndex
{
    public RepoMap Map() => RepoMap.Build(Sources().Select(kv => (kv.Key, kv.Value)));

    public ImpactReport Analyze(IReadOnlyList<string> seedTerms)
    {
        var sources = Sources();
        return ImpactAnalysis.Analyze(RepoMap.Build(sources.Select(kv => (kv.Key, kv.Value))), seedTerms, sources);
    }

    private Dictionary<string, string> Sources() =>
        workspace.ListFiles()
            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p, p => workspace.ReadFileAsync(p, CancellationToken.None).GetAwaiter().GetResult(), StringComparer.Ordinal);
}
