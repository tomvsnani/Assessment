using Orchestrator.Core.Governance;
using Orchestrator.Core.State;

namespace Orchestrator.Tests.Governance;

public class AuditLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdlc-audit-" + Guid.NewGuid().ToString("N"));

    private string AuditPath => Path.Combine(_dir, "audit.jsonl");

    [Fact]
    public void Given_governance_events_When_appended_Then_audit_entries_carry_actor_outcome_and_chained_hashes()
    {
        using var events = new EventStore("run-1", TimeProvider.System);
        using var audit = new AuditLog(events, AuditPath);

        events.Append(EventKind.StageStarted, "design", ("agent", "architect"));        // not audited
        events.Append(EventKind.PolicyEvaluated, "design", ("policy", "no-secrets"), ("phase", "exit"), ("verdict", "Pass"), ("reason", "ok"));
        events.Append(EventKind.ApprovalDecided, "design", ("decisionId", "D001"), ("label", "approve-design"), ("actor", "ramu"), ("decision", "Approved"), ("rationale", "looks right"), ("cites", "design"));

        audit.Entries.Should().HaveCount(2);
        audit.Entries[0].Actor.Should().Be("orchestrator");
        audit.Entries[0].Outcome.Should().Be("Pass");
        audit.Entries[1].Actor.Should().Be("ramu");
        audit.Entries[1].Outcome.Should().Be("Approved");
        audit.Entries[1].Detail.Should().Contain("approve-design: looks right");
        audit.Entries[0].PreviousHash.Should().Be("genesis");
        audit.Entries[1].PreviousHash.Should().Be(audit.Entries[0].Hash);
        AuditLog.Verify(audit.Entries).Should().BeNull();
    }

    [Fact]
    public void Given_audit_file_When_a_line_is_edited_Then_verification_reports_the_broken_entry()
    {
        using (var events = new EventStore("run-2", TimeProvider.System))
        using (var audit = new AuditLog(events, AuditPath))
        {
            events.Append(EventKind.ApprovalDecided, "spec", ("decisionId", "D001"), ("actor", "ramu"), ("decision", "Approved"), ("rationale", "fine"), ("label", "approve-spec"), ("cites", "spec"));
            events.Append(EventKind.StageCompleted, "spec", ("attempts", "1"));
            events.Append(EventKind.RunCompleted);
        }

        var lines = File.ReadAllLines(AuditPath);
        lines[0] = lines[0].Replace("\"Approved\"", "\"Rejected\"", StringComparison.Ordinal);
        File.WriteAllLines(AuditPath, lines);

        var entries = AuditLog.ReadFile(AuditPath);
        AuditLog.Verify(entries).Should().Be(entries[0].Seq);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
