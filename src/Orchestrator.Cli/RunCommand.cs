using System.Globalization;
using Orchestrator.Agents.Agents;
using Orchestrator.Agents.Codebase;
using Orchestrator.Agents.Llm;
using Orchestrator.Agents.Workspace;
using Orchestrator.Cli.Approvals;
using Orchestrator.Core.Contracts;
using Orchestrator.Core.Engine;
using Orchestrator.Core.Governance;
using Orchestrator.Core.Governance.Policies;
using Orchestrator.Core.Metrics;
using Orchestrator.Core.State;
using Orchestrator.Core.State.Projections;
using Orchestrator.Core.Workflow;

namespace Orchestrator.Cli;

/// <summary>
/// Composition root for one run: loads the workflow and requirement, materialises the workspace,
/// wires the engine to either a live provider or the committed recordings, runs it, and writes
/// everything a grader needs (events, audit, artifacts, metrics, lineage) to <c>runs/&lt;id&gt;/</c>.
/// </summary>
public sealed class RunCommand(CliOptions options)
{
    public async Task<int> ExecuteAsync(CancellationToken ctrlC)
    {
        var paths = RepositoryPaths.Locate();
        var workflow = WorkflowLoader.Load(paths.WorkflowFile(options.Target));
        var requirement = RequirementFile.Load(Path.Combine(paths.Root, workflow.RequirementPath), workflow.Kind);
        var recordingDir = paths.RecordingDirectory(workflow.Name);
        var runId = $"{workflow.Name}-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
        var runDir = Path.Combine(paths.Runs, runId);
        Directory.CreateDirectory(runDir);

        Console.WriteLine($"mode: {(options.Live ? $"LIVE via {options.Provider}" : "REPLAY from committed recordings")}   run: {runId}");
        Console.WriteLine($"workspace: {paths.WorkspaceDirectory(workflow.Name)}  baseline: {workflow.Baseline}");
        await BaselineMaterializer.MaterializeAsync(workflow.Baseline, paths.Root, paths.WorkspaceDirectory(workflow.Name), ctrlC);
        var workspace = new FileWorkspace(paths.WorkspaceDirectory(workflow.Name));
        var baselineCheckpoint = workspace.Checkpoint();

        using var events = new EventStore(runId, TimeProvider.System, Path.Combine(runDir, "events.jsonl"));
        using var audit = new AuditLog(events, Path.Combine(runDir, "audit.jsonl"));
        using var renderer = new ConsoleRenderer(events);
        void Log(string line) => Console.WriteLine($"          {line}");

        var llm = options.Live
            ? RecordingClient.Record(LlmClientFactory.Create(options.Provider, options.Model, new HttpClient()), Path.Combine(recordingDir, "llm"), Log)
            : RecordingClient.Replay(Path.Combine(recordingDir, "llm"), Log);
        var approver = BuildApprover(recordingDir);

        var deps = new AgentDependencies(llm, new PromptLibrary(paths.Prompts, paths.ClaudeMd), ws => new RoslynCodebaseIndex(ws), Log);
        var agents = new AgentRegistry(deps);

        var graph = new DependencyGraph(workflow.Stages);
        var state = new RunState();
        var saga = new Saga(events);
        using var safeStop = new SafeStop(events, ctrlC);
        var stageRoles = workflow.Stages.ToDictionary(s => s.Id, s => s.Agent, StringComparer.Ordinal);
        var policyGate = new PolicyGate(Policies(), events);
        var executor = new Executor(agents, policyGate, new ApprovalGate(approver, events), saga, events, stageRoles);
        var coordinator = new Coordinator(graph, state, saga, events);
        var scheduler = new Scheduler(workflow, graph, state, executor, coordinator, saga, safeStop, events);

        PrintGraph(graph);
        var outcome = await scheduler.RunAsync(requirement, workspace);

        var artifacts = state.ArtifactSnapshot();
        await RunOutput.WriteAsync(runDir, events.All, artifacts, workspace, baselineCheckpoint);
        Console.WriteLine();
        Console.WriteLine(ReliabilityMetrics.From(events.All).Render());
        Console.WriteLine($"replay fidelity: {llm.ExactMatches} exact, {llm.SequenceMatches} by sequence");
        Console.WriteLine($"outputs: {runDir}");

        if (options.Live)
        {
            await RunOutput.PublishRecordingAsync(runDir, recordingDir);
            Console.WriteLine($"recording updated: {recordingDir}");
        }

        Console.WriteLine(outcome.Succeeded ? "RESULT: success" : $"RESULT: {outcome.Summary}");
        return outcome.Succeeded ? 0 : 1;
    }

    private IApprover BuildApprover(string recordingDir)
    {
        var decisionsFile = Path.Combine(recordingDir, "decisions.jsonl");
        if (!options.Live)
        {
            return new ReplayApprover(RecordingApprover.Read(decisionsFile));
        }

        IApprover human = options.Unattended
            ? new UnattendedApprover()
            : new ConsoleApprover(Environment.GetEnvironmentVariable("SDLC_APPROVER") ?? Environment.UserName);
        return RecordingApprover.StartFresh(human, decisionsFile);
    }

    private static IEnumerable<IPolicy> Policies() =>
        [new NoSecretsPolicy(), new PiiInLogsPolicy(), new SchemaChangeNeedsApprovalPolicy(), new SegregationOfDutiesPolicy()];

    private static void PrintGraph(DependencyGraph graph)
    {
        Console.WriteLine("stage graph (parallel groups in braces):");
        Console.WriteLine("  " + string.Join(" -> ", graph.ParallelLevels().Select(level => level.Count == 1 ? level[0] : "{" + string.Join(", ", level) + "}")));
        Console.WriteLine();
    }
}
