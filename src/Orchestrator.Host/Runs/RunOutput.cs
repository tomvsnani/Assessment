using Orchestrator.Core.Contracts;
using Orchestrator.Core.Metrics;
using Orchestrator.Core.State;
using Orchestrator.Core.State.Projections;

namespace Orchestrator.Host.Runs;

/// <summary>
/// What a run leaves behind, and how a live run's results become the committed recording.
/// <c>runs/&lt;id&gt;/</c>: events.jsonl, audit.jsonl, artifacts/, metrics.txt, lineage.mmd, output/ (files the agents changed).
/// <c>recordings/&lt;scenario&gt;/</c>: llm/ and decisions.jsonl (written during the run) plus a copy of the above under run/.
/// </summary>
public static class RunOutput
{
    public static async Task WriteAsync(
        string runDir,
        IReadOnlyList<RunEvent> events,
        IReadOnlyDictionary<string, Artifact> artifacts,
        IWorkspace workspace,
        WorkspaceCheckpoint baseline)
    {
        var artifactDir = Path.Combine(runDir, "artifacts");
        Directory.CreateDirectory(artifactDir);
        foreach (var artifact in artifacts.Values.OrderBy(a => a.Name, StringComparer.Ordinal))
        {
            await File.WriteAllTextAsync(Path.Combine(artifactDir, artifact.Name + Extension(artifact.Kind)), artifact.Content);
        }

        await File.WriteAllTextAsync(Path.Combine(runDir, "metrics.txt"), ReliabilityMetrics.From(events).Render());
        await File.WriteAllTextAsync(Path.Combine(runDir, "lineage.mmd"), Lineage.From(events).RenderMermaid());

        var outputDir = Path.Combine(runDir, "output");
        foreach (var path in workspace.ChangedSince(baseline))
        {
            var target = Path.Combine(outputDir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, await workspace.ReadFileAsync(path, CancellationToken.None));
        }
    }

    public static Task PublishRecordingAsync(string runDir, string recordingDir)
    {
        var target = Path.Combine(recordingDir, "run");
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        CopyTree(runDir, target);
        return Task.CompletedTask;
    }

    private static string Extension(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Spec or ArtifactKind.Plan or ArtifactKind.ChangeRecord => ".json",
        _ => ".md",
    };

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
