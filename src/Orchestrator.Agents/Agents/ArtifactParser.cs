using System.Text.RegularExpressions;

namespace Orchestrator.Agents.Agents;

/// <summary>
/// Agents end their turn with <c>&lt;artifact name="spec"&gt;...&lt;/artifact&gt;</c> blocks. This pulls
/// them out of the final message. Anything outside the tags is commentary and is discarded.
/// </summary>
public static partial class ArtifactParser
{
    public static IReadOnlyDictionary<string, string> Extract(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in ArtifactBlock().Matches(text))
        {
            result[match.Groups["name"].Value] = StripFence(match.Groups["body"].Value.Trim());
        }

        return result;
    }

    public static string Require(IReadOnlyDictionary<string, string> artifacts, string name) =>
        artifacts.TryGetValue(name, out var content)
            ? content
            : throw new AgentOutputException($"Final message did not contain <artifact name=\"{name}\">.");

    /// <summary>Models often wrap the body in ```json fences even when told not to; tolerate it.</summary>
    private static string StripFence(string body)
    {
        var match = Fenced().Match(body);
        return match.Success ? match.Groups["inner"].Value.Trim() : body;
    }

    [GeneratedRegex("<artifact\\s+name=\"(?<name>[^\"]+)\"[^>]*>(?<body>.*?)</artifact>", RegexOptions.Singleline)]
    private static partial Regex ArtifactBlock();

    [GeneratedRegex("^```[a-zA-Z]*\\s*\\n(?<inner>.*?)\\n```\\s*$", RegexOptions.Singleline)]
    private static partial Regex Fenced();
}

public sealed class AgentOutputException(string message) : Exception(message);
