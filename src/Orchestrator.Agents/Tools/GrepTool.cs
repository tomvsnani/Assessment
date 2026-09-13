using System.Text.Json;
using System.Text.RegularExpressions;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Tools;

/// <summary>Regex search across workspace files.</summary>
public sealed partial class GrepTool(IWorkspace workspace) : ITool
{
    private const int MaxHits = 200;

    public string Name => "grep";
    public string Description => "Search workspace files with a .NET regular expression. Returns path:line: text for each match.";
    public string InputSchema => """{"type":"object","properties":{"pattern":{"type":"string"},"glob":{"type":"string","description":"optional file suffix filter, e.g. .cs"}},"required":["pattern"]}""";

    public async Task<string> InvokeAsync(JsonElement input, CancellationToken ct)
    {
        Regex regex;
        try
        {
            regex = new Regex(input.Arg("pattern"), RegexOptions.None, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException e)
        {
            return $"ERROR: invalid regex: {e.Message}";
        }

        var suffix = input.OptionalArg("glob");
        var hits = new List<string>();
        foreach (var path in workspace.ListFiles().Where(p => suffix is null || p.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            var lines = (await workspace.ReadFileAsync(path, ct)).Split('\n');
            for (var i = 0; i < lines.Length && hits.Count < MaxHits; i++)
            {
                if (regex.IsMatch(lines[i]))
                {
                    hits.Add($"{path}:{i + 1}: {lines[i].TrimEnd()}");
                }
            }
        }

        return hits.Count == 0 ? "(no matches)" : string.Join("\n", hits);
    }
}
