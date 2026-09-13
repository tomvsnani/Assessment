using Orchestrator.Core.Contracts;
using Orchestrator.Core.Engine;
using Orchestrator.Core.Governance;
using Orchestrator.Core.Governance.Policies;
using Orchestrator.Core.State;
using Orchestrator.Core.Workflow;

namespace Orchestrator.Tests.Fakes;

/// <summary>Wires the whole engine around fakes so a test reads as "given this workflow and these agents, when run, then ...".</summary>
public sealed class TestRun : IDisposable
{
    public static readonly Requirement Requirement = new("REQ-1", "test", "do the thing", ScenarioKind.Greenfield);

    public TestRun(string workflowYaml, FakeAgents agents, IApprover? approver = null, IEnumerable<IPolicy>? policies = null)
    {
        Workflow = WorkflowLoader.Parse(workflowYaml);
        Events = new EventStore("run-test", TimeProvider.System);
        Workspace = new InMemoryWorkspace();
        Approver = approver ?? new ScriptedApprover();

        var graph = new DependencyGraph(Workflow.Stages);
        var state = new RunState();
        var saga = new Saga(Events);
        SafeStop = new SafeStop(Events);
        var stageRoles = Workflow.Stages.ToDictionary(s => s.Id, s => s.Agent, StringComparer.Ordinal);
        var policyGate = new PolicyGate(policies ?? DefaultPolicies(), Events);
        var executor = new Executor(agents, policyGate, new ApprovalGate(Approver, Events), saga, Events, stageRoles);
        var coordinator = new Coordinator(graph, state, saga, Events);
        Scheduler = new Scheduler(Workflow, graph, state, executor, coordinator, saga, SafeStop, Events);
    }

    public WorkflowDefinition Workflow { get; }
    public EventStore Events { get; }
    public InMemoryWorkspace Workspace { get; }
    public IApprover Approver { get; }
    public SafeStop SafeStop { get; }
    public Scheduler Scheduler { get; }

    public Task<RunOutcome> RunAsync() => Scheduler.RunAsync(Requirement, Workspace);

    public IEnumerable<RunEvent> EventsOfKind(EventKind kind) => Events.All.Where(e => e.Kind == kind);

    public IEnumerable<string> StageOrder(EventKind kind = EventKind.StageCompleted) => EventsOfKind(kind).Select(e => e.StageId!);

    public static IEnumerable<IPolicy> DefaultPolicies() =>
        [new NoSecretsPolicy(), new PiiInLogsPolicy(), new SchemaChangeNeedsApprovalPolicy(), new SegregationOfDutiesPolicy()];

    public void Dispose()
    {
        SafeStop.Dispose();
        Events.Dispose();
    }
}
