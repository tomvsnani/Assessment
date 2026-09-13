using System.Text.Json;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Tools;

/// <summary>Read a file from the workspace.</summary>
public sealed class ReadFileTool(IWorkspace workspace) : ITool
{
    public string Name => "read_file";
    public string Description => "Read a text file from the workspace. Path is relative to the workspace root.";
    public string InputSchema => """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""";

    public async Task<string> InvokeAsync(JsonElement input, CancellationToken ct)
    {
        try
        {
            return await workspace.ReadFileAsync(input.Arg("path"), ct);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return $"ERROR: file not found: {input.Arg("path")}";
        }
    }
}
