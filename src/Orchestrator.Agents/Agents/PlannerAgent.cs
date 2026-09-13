using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Agents;

/// <summary>Decomposes the design into Jira-shaped work items with dependencies; validates that they form a DAG.</summary>
public sealed class PlannerAgent(AgentDependencies deps) : LlmAgent(deps)
{
    public const string ArtifactName = "plan";

    public override string Role => "planner";

    protected override IReadOnlyList<string> Reads => [RequirementsAgent.ArtifactName, ArchitectAgent.ArtifactName];

    protected override int MaxIterations => 10;

    protected override StageResult Parse(string finalText, StageContext ctx, IReadOnlyDictionary<string, string> artifacts)
    {
        var json = ArtifactParser.Require(artifacts, ArtifactName);
        List<WorkItem> items;
        try
        {
            items = ArtifactJson.Deserialize<List<WorkItem>>(json);
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new AgentOutputException($"plan is not a valid WorkItem[] JSON array: {e.Message}");
        }

        if (items.Count == 0)
        {
            throw new AgentOutputException("plan has no work items");
        }

        var keys = items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
        var unknown = items.SelectMany(i => i.DependsOn).Where(d => !keys.Contains(d)).Distinct(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            throw new AgentOutputException($"plan depends on unknown work item(s): {string.Join(", ", unknown)}");
        }

        if (HasCycle(items))
        {
            throw new AgentOutputException("plan dependencies contain a cycle");
        }

        return StageResult.Success(Artifact(ArtifactName, ArtifactKind.Plan, ArtifactJson.Serialize(items), ctx,
            RequirementsAgent.ArtifactName, ArchitectAgent.ArtifactName));
    }

    private static bool HasCycle(List<WorkItem> items)
    {
        var deps = items.ToDictionary(i => i.Key, i => i.DependsOn, StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 = visiting, 2 = done

        bool Visit(string key)
        {
            if (state.TryGetValue(key, out var s))
            {
                return s == 1;
            }

            state[key] = 1;
            if (deps[key].Any(Visit))
            {
                return true;
            }

            state[key] = 2;
            return false;
        }

        return items.Any(i => Visit(i.Key));
    }
}
