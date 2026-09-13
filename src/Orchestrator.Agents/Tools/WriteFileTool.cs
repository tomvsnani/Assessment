using System.Text.Json;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Tools;

/// <summary>Create or overwrite a file in the workspace.</summary>
public sealed class WriteFileTool(IWorkspace workspace) : ITool
{
    public string Name => "write_file";
    public string Description => "Create or fully overwrite a text file in the workspace. Always write the complete file content.";
    public string InputSchema => """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}""";

    public async Task<string> InvokeAsync(JsonElement input, CancellationToken ct)
    {
        var path = input.Arg("path");
        await workspace.WriteFileAsync(path, input.Arg("content"), ct);
        return $"wrote {path}";
    }
}
