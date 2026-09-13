using Orchestrator.Core.State;
using Orchestrator.Core.State.Projections;

namespace Orchestrator.Tests.State;

public class EventStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdlc-events-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Given_events_appended_When_file_read_back_Then_same_events_in_same_order()
    {
        var path = Path.Combine(_dir, "events.jsonl");
        using (var store = new EventStore("run-1", TimeProvider.System, path))
        {
            store.Append(EventKind.RunStarted, null, ("workflow", "w"));
            store.Append(EventKind.StageStarted, "a", ("agent", "x"));
            store.Append(EventKind.ArtifactProduced, "a", ("name", "spec"), ("hash", "abc"), ("derivedFrom", ""));
        }

        var read = EventStore.ReadFile(path);

        read.Select(e => e.Seq).Should().Equal(1, 2, 3);
        read.Select(e => e.Kind).Should().Equal(EventKind.RunStarted, EventKind.StageStarted, EventKind.ArtifactProduced);
        read[2]["name"].Should().Be("spec");
        read[1].StageId.Should().Be("a");
    }

    [Fact]
    public void Given_event_log_When_projected_Then_status_and_lineage_are_rebuilt()
    {
        using var store = new EventStore("run-1", TimeProvider.System);
        store.Append(EventKind.StageScheduled, "a");
        store.Append(EventKind.StageScheduled, "b");
        store.Append(EventKind.StageStarted, "a");
        store.Append(EventKind.ArtifactProduced, "a", ("name", "spec"), ("hash", "h1"), ("derivedFrom", ""));
        store.Append(EventKind.ApprovalDecided, "a", ("decisionId", "D001"), ("actor", "ramu"), ("decision", "Approved"), ("rationale", "ok"), ("cites", "spec"));
        store.Append(EventKind.StageCompleted, "a");
        store.Append(EventKind.StageStarted, "b");
        store.Append(EventKind.ArtifactProduced, "b", ("name", "design"), ("hash", "h2"), ("derivedFrom", "spec"));
        store.Append(EventKind.StageCompleted, "b");
        store.Append(EventKind.RunCompleted);

        var status = RunStatus.From(store.All);
        var lineage = Lineage.From(store.All);

        status.Completed.Should().BeEquivalentTo("a", "b");
        status.RunSucceeded.Should().BeTrue();
        lineage.Ancestors("design").Should().Equal("spec");
        lineage.Decisions.Single().Cites.Should().Equal("spec");
        lineage.RenderMermaid().Should().Contain("spec --> design").And.Contain("Approved by ramu");
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
