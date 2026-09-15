using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Orchestrator.Core.Workflow;

/// <summary>Reads <c>workflows/*.yaml</c> into a validated <see cref="WorkflowDefinition"/>.</summary>
public static class WorkflowLoader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static WorkflowDefinition Load(string path) => Parse(File.ReadAllText(path));

    public static WorkflowDefinition Parse(string yaml)
    {
        var dto = Yaml.Deserialize<WorkflowDto>(yaml) ?? throw new WorkflowValidationException("Workflow file is empty.");
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new WorkflowValidationException("Workflow needs a name.");
        }

        var stages = dto.Stages.Select(ToStage).ToList();
        _ = new DependencyGraph(stages); // validates ids, dependencies and acyclicity

        return new WorkflowDefinition(dto.Name, dto.MaxParallel is > 0 ? dto.MaxParallel.Value : 4, stages);
    }

    private static StageDefinition ToStage(StageDto s)
    {
        if (string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Agent))
        {
            throw new WorkflowValidationException("Every stage needs an id and an agent.");
        }

        return new StageDefinition(
            s.Id,
            s.Agent,
            s.DependsOn ?? [],
            ToGate(s.Entry),
            ToGate(s.Exit),
            s.Retry is null
                ? RetryDefinition.None
                : new RetryDefinition(Math.Max(1, s.Retry.MaxAttempts), TimeSpan.FromSeconds(s.Retry.BaseDelaySeconds)),
            s.Fallback,
            s.OnFailure is null
                ? FailureHandling.StopRun
                : new FailureHandling(s.OnFailure.RerunFrom, Math.Max(0, s.OnFailure.MaxLoops), ToRerunMode(s.OnFailure.Mode)));
    }

    private static RerunMode ToRerunMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        null or "" or "fix" => RerunMode.Fix,
        "rollback" => RerunMode.Rollback,
        _ => throw new WorkflowValidationException($"on_failure.mode must be 'fix' or 'rollback', not '{mode}'."),
    };

    private static GateDefinition ToGate(GateDto? g) =>
        g is null ? GateDefinition.Open : new GateDefinition(g.Artifacts ?? [], g.Policies ?? [], g.Approval);

    // YAML shape. Underscored names in the file map to these PascalCase properties.
    private sealed class WorkflowDto
    {
        public string? Name { get; set; }
        public int? MaxParallel { get; set; }
        public List<StageDto> Stages { get; set; } = [];
    }

    private sealed class StageDto
    {
        public string? Id { get; set; }
        public string? Agent { get; set; }
        public List<string>? DependsOn { get; set; }
        public GateDto? Entry { get; set; }
        public GateDto? Exit { get; set; }
        public RetryDto? Retry { get; set; }
        public string? Fallback { get; set; }
        public OnFailureDto? OnFailure { get; set; }
    }

    private sealed class GateDto
    {
        public List<string>? Artifacts { get; set; }
        public List<string>? Policies { get; set; }
        public string? Approval { get; set; }
    }

    private sealed class RetryDto
    {
        public int MaxAttempts { get; set; } = 1;
        public double BaseDelaySeconds { get; set; }
    }

    private sealed class OnFailureDto
    {
        public string? RerunFrom { get; set; }
        public int MaxLoops { get; set; }
        public string? Mode { get; set; }
    }
}
