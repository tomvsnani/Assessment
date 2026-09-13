using System.Text;
using Orchestrator.Core.Contracts;
using Orchestrator.Core.Engine;

namespace Orchestrator.Agents.Agents;

/// <summary>
/// Turns a <see cref="StageContext"/> into the user message an agent reads: the requirement,
/// the upstream artifacts it needs (by name, in a fixed order), decisions taken so far, and any
/// feedback from a previous attempt. Deterministic on purpose — replay depends on it.
/// </summary>
public static class ContextRenderer
{
    public static string Render(StageContext ctx, IReadOnlyList<string> artifactNames, string? extraSection = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Requirement");
        sb.AppendLine($"Id: {ctx.Requirement.Id}  Kind: {ctx.Requirement.Kind}");
        sb.AppendLine($"Title: {ctx.Requirement.Title}");
        sb.AppendLine();
        sb.AppendLine(ctx.Requirement.Text.Trim());

        foreach (var name in artifactNames)
        {
            if (ctx.Artifacts.TryGetValue(name, out var artifact))
            {
                sb.AppendLine();
                sb.AppendLine($"# Upstream artifact: {name} ({artifact.Kind}, produced by stage '{artifact.ProducedBy}')");
                sb.AppendLine(artifact.Content.Trim());
            }
        }

        var relevant = ctx.Decisions.Where(d => d.Kind is DecisionKind.OptionChosen or DecisionKind.Approved).ToList();
        if (relevant.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("# Decisions already taken (cite them by id where they shape your output)");
            foreach (var decision in relevant)
            {
                sb.AppendLine($"- {decision.Id} [{decision.Kind}] by {decision.Actor} at stage '{decision.StageId}': {decision.Rationale}");
            }
        }

        if (extraSection is not null)
        {
            sb.AppendLine();
            sb.AppendLine(extraSection.Trim());
        }

        if (ctx.Artifacts.TryGetValue(Executor.FeedbackArtifact, out var feedback))
        {
            sb.AppendLine();
            sb.AppendLine($"# Feedback on the previous attempt (this is attempt {ctx.Attempt})");
            sb.AppendLine(feedback.Content.Trim());
        }

        return sb.ToString();
    }
}
