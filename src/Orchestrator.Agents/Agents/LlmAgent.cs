using Orchestrator.Agents.Codebase;
using Orchestrator.Agents.Llm;
using Orchestrator.Agents.Tools;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Agents;

/// <summary>
/// Base for every LLM-backed role. A subclass says which upstream artifacts it reads, which
/// tools it may use, and how to turn the model's final message into typed artifacts.
/// The base handles the prompt, the loop and error translation.
/// </summary>
public abstract class LlmAgent(AgentDependencies deps) : IStageAgent
{
    protected AgentDependencies Deps { get; } = deps;

    public abstract string Role { get; }

    /// <summary>Artifact names rendered into the user message, in this order.</summary>
    protected abstract IReadOnlyList<string> Reads { get; }

    protected virtual bool CanWrite => false;

    protected virtual bool CanBuild => false;

    protected virtual int MaxIterations => AgentToolLoop.DefaultMaxIterations;

    /// <summary>Extra context for the user message (repo map, impact analysis, ...). Null for none.</summary>
    protected virtual string? ExtraContext(StageContext ctx) => null;

    protected abstract StageResult Parse(string finalText, StageContext ctx, IReadOnlyDictionary<string, string> artifacts);

    public async Task<StageResult> ExecuteAsync(StageContext context)
    {
        var tools = Tools(context.Workspace);
        var system = Deps.Prompts.SystemPromptFor(Role);
        var user = ContextRenderer.Render(context, Reads, ExtraContext(context));

        var loop = new AgentToolLoop(Deps.Llm, Deps.Trace, Deps.Log);
        var outcome = await loop.RunAsync(context.Stage.Id, Role, system, user, tools, context.CancellationToken, MaxIterations);
        Deps.Log($"{Role}: {outcome.Iterations} turns, {outcome.ToolCalls} tool calls, {outcome.InputTokens}+{outcome.OutputTokens} tokens");

        try
        {
            return Parse(outcome.FinalText, context, ArtifactParser.Extract(outcome.FinalText));
        }
        catch (AgentOutputException e)
        {
            return StageResult.Failure(e.Message,
                new Artifact("feedback", ArtifactKind.Feedback, $"Your previous final message could not be used: {e.Message}\nEnd with the required <artifact> block(s).", context.Stage.Id, []));
        }
    }

    private List<ITool> Tools(IWorkspace workspace)
    {
        var tools = new List<ITool> { new ListFilesTool(workspace), new ReadFileTool(workspace), new GrepTool(workspace) };
        if (CanWrite)
        {
            tools.Add(new WriteFileTool(workspace));
        }

        if (CanBuild)
        {
            tools.Add(new RunBuildTool(workspace));
            tools.Add(new RunTestsTool(workspace));
        }

        return tools;
    }

    protected static Artifact Artifact(string name, ArtifactKind kind, string content, StageContext ctx, params string[] derivedFrom) =>
        new(name, kind, content, ctx.Stage.Id, derivedFrom.Where(ctx.Artifacts.ContainsKey).ToList());

    /// <summary>Repo map + impact analysis, only when the workspace already has code (brownfield / ambiguous).</summary>
    protected string? CodebaseSection(StageContext ctx, IReadOnlyList<string> seedTerms)
    {
        var index = Deps.IndexFor(ctx.Workspace);
        var map = index.Map();
        if (map.Files.Count == 0)
        {
            return null;
        }

        var impact = index.Analyze(seedTerms);
        return "# Existing codebase (repo map)\n" + map.Render() + "\n# Impact analysis (deterministic, seeded from the requirement)\n" + impact.RenderMarkdown();
    }
}

/// <summary>What every agent needs; built once by the CLI.</summary>
public sealed record AgentDependencies(ILlmClient Llm, PromptLibrary Prompts, Func<IWorkspace, ICodebaseIndex> IndexFor, IRunTrace Trace, Action<string> Log);
