using System.Security.Cryptography;
using System.Text;

namespace Orchestrator.Core.Contracts;

/// <summary>
/// Anything a stage produces: a spec, a design, a plan, a diff, a test report, a change record.
/// <see cref="DerivedFrom"/> names the artifacts it was built from — this is the lineage graph.
/// </summary>
public sealed record Artifact(
    string Name,
    ArtifactKind Kind,
    string Content,
    string ProducedBy,
    IReadOnlyList<string> DerivedFrom)
{
    // Computed, not cached: `with { Content = ... }` must never carry a stale hash.
    public string ContentHash => Hash(Content);

    public static string Hash(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..16];
}

public enum ArtifactKind
{
    Spec,
    Design,
    Plan,
    Code,
    Tests,
    TestReport,
    Review,
    Documentation,
    ChangeRecord,
    Feedback,
}
