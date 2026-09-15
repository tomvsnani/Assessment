using System.Text.Json;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Tools;

/// <summary>Create or overwrite a file in the workspace; warns at once if a file-content policy would block it.</summary>
public sealed class WriteFileTool(IWorkspace workspace, FilePolicyCheck? policyCheck = null) : ITool
{
    public string Name => "write_file";
    public string Description => "Create a new text file, or fully overwrite one. Always write the complete file content. For changes to an existing file prefer edit_file.";
    public string InputSchema => """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}""";

    public async Task<string> InvokeAsync(JsonElement input, CancellationToken ct)
    {
        var path = input.Arg("path");
        var content = input.Arg("content");
        await workspace.WriteFileAsync(path, content, ct);
        var warning = policyCheck?.WarningFor(path, content);
        return warning is null ? $"wrote {path}" : $"wrote {path}\n{warning}";
    }
}
