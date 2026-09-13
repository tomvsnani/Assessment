using Orchestrator.Agents.Workspace;

namespace Orchestrator.Tests.Agents;

public class FileWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sdlc-ws-" + Guid.NewGuid().ToString("N"), "scenario");
    private readonly FileWorkspace _workspace;

    public FileWorkspaceTests() => _workspace = new FileWorkspace(_root);

    [Fact]
    public async Task Given_path_outside_root_When_accessed_Then_refused()
    {
        var act = () => _workspace.WriteFileAsync("../escape.txt", "x", CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "escape.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Given_checkpoint_When_files_change_Then_changed_since_lists_them_and_restore_undoes_them()
    {
        await _workspace.WriteFileAsync("src/A.cs", "a", CancellationToken.None);
        await _workspace.WriteFileAsync("src/B.cs", "b", CancellationToken.None);
        var checkpoint = _workspace.Checkpoint();

        await _workspace.WriteFileAsync("src/B.cs", "b2", CancellationToken.None);
        await _workspace.WriteFileAsync("src/C.cs", "c", CancellationToken.None);
        await _workspace.DeleteFileAsync("src/A.cs", CancellationToken.None);

        _workspace.ChangedSince(checkpoint).Should().Equal("src/B.cs", "src/C.cs");
        await _workspace.RestoreAsync(checkpoint, CancellationToken.None);

        _workspace.ListFiles().Should().Equal("src/A.cs", "src/B.cs");
        (await _workspace.ReadFileAsync("src/B.cs", CancellationToken.None)).Should().Be("b");
    }

    [Fact]
    public async Task Given_build_output_directories_When_listed_Then_they_are_ignored()
    {
        await _workspace.WriteFileAsync("src/A.cs", "a", CancellationToken.None);
        await _workspace.WriteFileAsync("src/bin/Debug/A.dll", "binary", CancellationToken.None);
        await _workspace.WriteFileAsync("src/obj/project.assets.json", "{}", CancellationToken.None);

        _workspace.ListFiles().Should().Equal("src/A.cs");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        var parent = Path.GetDirectoryName(_root)!;
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }
}
