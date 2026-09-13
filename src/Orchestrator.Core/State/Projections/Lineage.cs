using System.Text;

namespace Orchestrator.Core.State.Projections;

/// <summary>
/// Answers "where did this artifact come from and which decisions shaped it?" from the event log.
/// Rendered into every scenario walkthrough.
/// </summary>
public sealed class Lineage
{
    public sealed record ArtifactNode(string Name, string Hash, string ProducedBy, IReadOnlyList<string> DerivedFrom);

    public sealed record DecisionNode(string Id, string StageId, string Actor, string Kind, string Rationale, IReadOnlyList<string> Cites);

    private readonly Dictionary<string, ArtifactNode> _artifacts = new(StringComparer.Ordinal);
    private readonly List<DecisionNode> _decisions = [];

    public IReadOnlyDictionary<string, ArtifactNode> Artifacts => _artifacts;

    public IReadOnlyList<DecisionNode> Decisions => _decisions;

    public static Lineage From(IEnumerable<RunEvent> events)
    {
        var lineage = new Lineage();
        foreach (var evt in events)
        {
            switch (evt.Kind)
            {
                case EventKind.ArtifactProduced:
                    lineage._artifacts[evt["name"]] = new ArtifactNode(
                        evt["name"], evt["hash"], evt.StageId!, Split(evt["derivedFrom"]));
                    break;
                case EventKind.ApprovalDecided:
                    lineage._decisions.Add(new DecisionNode(
                        evt["decisionId"], evt.StageId!, evt["actor"], evt["decision"], evt["rationale"], Split(evt["cites"])));
                    break;
                default:
                    break;
            }
        }

        return lineage;
    }

    /// <summary>Artifacts that the named one transitively depends on, nearest first.</summary>
    public IReadOnlyList<string> Ancestors(string artifactName)
    {
        var seen = new List<string>();
        var queue = new Queue<string>([artifactName]);
        while (queue.TryDequeue(out var current))
        {
            if (!_artifacts.TryGetValue(current, out var node))
            {
                continue;
            }

            foreach (var parent in node.DerivedFrom.Where(p => !seen.Contains(p, StringComparer.Ordinal)))
            {
                seen.Add(parent);
                queue.Enqueue(parent);
            }
        }

        return seen;
    }

    public string RenderMermaid()
    {
        var sb = new StringBuilder("graph LR\n");
        foreach (var node in _artifacts.Values)
        {
            sb.Append(CultureInfo, $"  {Safe(node.Name)}[\"{node.Name}<br/>{node.ProducedBy}\"]\n");
            foreach (var parent in node.DerivedFrom)
            {
                sb.Append(CultureInfo, $"  {Safe(parent)} --> {Safe(node.Name)}\n");
            }
        }

        foreach (var decision in _decisions)
        {
            sb.Append(CultureInfo, $"  {Safe(decision.Id)}{{\"{decision.Kind} by {decision.Actor}\"}}\n");
            foreach (var cited in decision.Cites)
            {
                sb.Append(CultureInfo, $"  {Safe(cited)} -.-> {Safe(decision.Id)}\n");
            }
        }

        return sb.ToString();
    }

    private static readonly System.Globalization.CultureInfo CultureInfo = System.Globalization.CultureInfo.InvariantCulture;

    private static string Safe(string id) => id.Replace('-', '_').Replace('.', '_');

    private static string[] Split(string csv) =>
        string.IsNullOrWhiteSpace(csv) ? [] : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
