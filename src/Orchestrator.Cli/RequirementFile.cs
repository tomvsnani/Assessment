using System.Text.RegularExpressions;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Cli;

/// <summary>Reads <c>requirements/&lt;scenario&gt;.md</c>: a small YAML front matter (id, title) and the body.</summary>
public static partial class RequirementFile
{
    public static Requirement Load(string path, ScenarioKind kind)
    {
        var text = File.ReadAllText(path);
        var match = FrontMatter().Match(text);
        if (!match.Success)
        {
            throw new InvalidOperationException($"{path} needs a front matter with id and title.");
        }

        var fields = match.Groups["fields"].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

        return new Requirement(
            fields.GetValueOrDefault("id") ?? throw new InvalidOperationException($"{path}: front matter has no id."),
            fields.GetValueOrDefault("title") ?? throw new InvalidOperationException($"{path}: front matter has no title."),
            match.Groups["body"].Value.Trim(),
            kind);
    }

    [GeneratedRegex(@"\A---\s*\n(?<fields>.*?)\n---\s*\n(?<body>.*)\z", RegexOptions.Singleline)]
    private static partial Regex FrontMatter();
}
