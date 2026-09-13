using System.Text.RegularExpressions;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Core.Governance.Policies;

/// <summary>
/// Change control for the database: any DDL an agent writes must name a table that the
/// human-approved design already mentions. An agent cannot introduce a table on its own, and
/// a design that was never approved cannot authorise one.
/// </summary>
public sealed partial class SchemaChangeNeedsApprovalPolicy : IPolicy
{
    public const string DesignArtifact = "design";
    public const string DesignStage = "design";

    public string Name => "schema-change-needs-approval";

    public PolicyVerdict Evaluate(PolicyContext context)
    {
        var tables = context.ChangedFiles
            .SelectMany(f => Ddl().Matches(f.Content).Select(m => (Table: m.Groups["table"].Value, f.Path)))
            .DistinctBy(t => t.Table, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tables.Count == 0)
        {
            return PolicyVerdict.Pass;
        }

        if (!context.AllArtifacts.TryGetValue(DesignArtifact, out var design))
        {
            return PolicyVerdict.Block($"schema change to '{tables[0].Table}' but no design artifact exists");
        }

        var designApproved = context.Decisions.Any(d => d.StageId == DesignStage && d.Kind == DecisionKind.Approved);
        if (!designApproved)
        {
            return PolicyVerdict.Block($"schema change to '{tables[0].Table}' but the design was never approved");
        }

        foreach (var (table, path) in tables)
        {
            if (!design.Content.Contains(table, StringComparison.OrdinalIgnoreCase))
            {
                return PolicyVerdict.Block($"table '{table}' in {path} is not mentioned in the approved design");
            }
        }

        return PolicyVerdict.Pass;
    }

    [GeneratedRegex(@"\b(?:CREATE|ALTER|DROP)\s+TABLE\s+(?:IF\s+(?:NOT\s+)?EXISTS\s+)?(?<table>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase)]
    private static partial Regex Ddl();
}
