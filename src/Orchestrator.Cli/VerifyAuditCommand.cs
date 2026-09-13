using Orchestrator.Core.Governance;

namespace Orchestrator.Cli;

/// <summary>Re-computes the audit hash chain of a run directory and reports whether it is intact.</summary>
public static class VerifyAuditCommand
{
    public static int Execute(CliOptions options)
    {
        var file = Directory.Exists(options.Target) ? Path.Combine(options.Target, "audit.jsonl") : options.Target;
        if (!File.Exists(file))
        {
            throw new FileNotFoundException($"No audit.jsonl at {file}");
        }

        var entries = AuditLog.ReadFile(file);
        var broken = AuditLog.Verify(entries);
        if (broken is { } seq)
        {
            Console.WriteLine($"TAMPERED: chain breaks at entry seq={seq} of {entries.Count}");
            return 1;
        }

        Console.WriteLine($"INTACT: {entries.Count} audit entries, chain verified");
        foreach (var decision in entries.Where(e => e.Action == "ApprovalDecided"))
        {
            Console.WriteLine($"  {decision.At:u} {decision.StageId,-14} {decision.Outcome,-18} by {decision.Actor}: {decision.Detail}");
        }

        return 0;
    }
}
