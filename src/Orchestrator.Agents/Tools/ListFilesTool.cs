using System.Text.Json;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Tools;

/// <summary>List workspace files, optionally under a subdirectory.</summary>
public sealed class ListFilesTool(IWorkspace workspace) : ITool
{
    public string Name => "list_files";
    public string Description => "List all files in the workspace (or under a subdirectory), relative paths, build outputs excluded.";
    public string InputSchema => """{"type":"object","properties":{"directory":{"type":"string"}}}""";

    public Task<string> InvokeAsync(JsonElement input, CancellationToken ct)
    {
        var files = workspace.ListFiles(input.OptionalArg("directory"));
        return Task.FromResult(files.Count == 0 ? "(no files)" : string.Join("\n", files));
    }
}
