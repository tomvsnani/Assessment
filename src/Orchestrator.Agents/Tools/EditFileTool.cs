using System.Text.Json;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Tools;

/// <summary>
/// Replace one exact occurrence of a string in an existing file. The alternative — re-sending the
/// whole file through <c>write_file</c> — costs the full file in output tokens every time and is how
/// code gets dropped; on a feedback re-run the fix is usually a few lines. The match must be
/// unique so a careless edit cannot land in the wrong place.
/// </summary>
public sealed class EditFileTool(IWorkspace workspace, FilePolicyCheck? policyCheck = null) : ITool
{
    public string Name => "edit_file";
    public string Description => "Replace one exact occurrence of old_string with new_string in an existing file. old_string must appear exactly once (include enough surrounding lines to make it unique). Prefer this over write_file for changes to existing files.";
    public string InputSchema => """{"type":"object","properties":{"path":{"type":"string"},"old_string":{"type":"string"},"new_string":{"type":"string"}},"required":["path","old_string","new_string"]}""";

    public async Task<string> InvokeAsync(JsonElement input, CancellationToken ct)
    {
        var path = input.Arg("path");
        var oldString = input.Arg("old_string");
        var newString = input.Arg("new_string");
        if (oldString.Length == 0)
        {
            return "ERROR: old_string is empty; use write_file to create a file.";
        }

        string content;
        try
        {
            content = await workspace.ReadFileAsync(path, ct);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return $"ERROR: file not found: {path}";
        }

        var first = content.IndexOf(oldString, StringComparison.Ordinal);
        if (first < 0)
        {
            return $"ERROR: old_string not found in {path}. Read the file and copy the text exactly.";
        }

        if (content.IndexOf(oldString, first + 1, StringComparison.Ordinal) >= 0)
        {
            return $"ERROR: old_string occurs more than once in {path}; include more surrounding lines so it is unique.";
        }

        var updated = string.Concat(content.AsSpan(0, first), newString, content.AsSpan(first + oldString.Length));
        await workspace.WriteFileAsync(path, updated, ct);
        var warning = policyCheck?.WarningFor(path, updated);
        return warning is null ? $"edited {path}" : $"edited {path}\n{warning}";
    }
}
