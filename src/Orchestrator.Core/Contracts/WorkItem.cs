namespace Orchestrator.Core.Contracts;

/// <summary>
/// One unit of the plan, shaped like a Jira story so it could be exported without translation.
/// <see cref="DependsOn"/> is what turns the list into a schedule.
/// </summary>
public sealed record WorkItem(
    string Key,
    string Summary,
    string Description,
    IReadOnlyList<string> AcceptanceCriteriaIds,
    IReadOnlyList<string> DependsOn,
    RiskLevel Risk,
    IReadOnlyList<string> FilesLikelyTouched);

public enum RiskLevel
{
    Low,
    Medium,
    High,
}
