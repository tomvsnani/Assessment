using System.Globalization;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Tests.Fakes;

/// <summary>Dictionary-backed workspace; checkpoints are full copies, which is fine for tests.</summary>
public sealed class InMemoryWorkspace : IWorkspace
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _checkpoints = new(StringComparer.Ordinal);
    private int _counter;

    public string RootPath => "memory://workspace";

    public IReadOnlyDictionary<string, string> Files => _files;

    public Task<string> ReadFileAsync(string relativePath, CancellationToken ct) =>
        Task.FromResult(_files.TryGetValue(relativePath, out var content) ? content : throw new FileNotFoundException(relativePath));

    public Task WriteFileAsync(string relativePath, string content, CancellationToken ct)
    {
        _files[relativePath] = content;
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string relativePath, CancellationToken ct)
    {
        _files.Remove(relativePath);
        return Task.CompletedTask;
    }

    public IReadOnlyList<string> ListFiles(string? subdirectory = null) =>
        _files.Keys.Where(k => subdirectory is null || k.StartsWith(subdirectory, StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();

    public IReadOnlyList<string> ChangedSince(WorkspaceCheckpoint checkpoint)
    {
        var then = _checkpoints[checkpoint.Id];
        return _files.Where(kv => !then.TryGetValue(kv.Key, out var old) || old != kv.Value).Select(kv => kv.Key).ToList();
    }

    public WorkspaceCheckpoint Checkpoint()
    {
        var id = (++_counter).ToString(CultureInfo.InvariantCulture);
        _checkpoints[id] = new Dictionary<string, string>(_files, StringComparer.Ordinal);
        return new WorkspaceCheckpoint(id, DateTimeOffset.UtcNow);
    }

    public Task RestoreAsync(WorkspaceCheckpoint checkpoint, CancellationToken ct)
    {
        _files.Clear();
        foreach (var (path, content) in _checkpoints[checkpoint.Id])
        {
            _files[path] = content;
        }

        return Task.CompletedTask;
    }
}
