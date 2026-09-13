namespace Orchestrator.Core.Contracts;

/// <summary>
/// The sandboxed directory a run works in. Agents can only touch files through this port,
/// which is what makes "agents write outside the workspace" impossible rather than merely forbidden.
/// </summary>
public interface IWorkspace
{
    string RootPath { get; }

    Task<string> ReadFileAsync(string relativePath, CancellationToken ct);

    Task WriteFileAsync(string relativePath, string content, CancellationToken ct);

    Task DeleteFileAsync(string relativePath, CancellationToken ct);

    IReadOnlyList<string> ListFiles(string? subdirectory = null);

    /// <summary>Files created or modified since the given checkpoint. Used by policies and compensation.</summary>
    IReadOnlyList<string> ChangedSince(WorkspaceCheckpoint checkpoint);

    WorkspaceCheckpoint Checkpoint();

    /// <summary>Restores the workspace to the checkpoint. Compensation for a failed or invalidated stage.</summary>
    Task RestoreAsync(WorkspaceCheckpoint checkpoint, CancellationToken ct);
}

/// <summary>Opaque snapshot token; how it is implemented (git stash, file copy, hashes) is the adapter's business.</summary>
public sealed record WorkspaceCheckpoint(string Id, DateTimeOffset TakenAt);
