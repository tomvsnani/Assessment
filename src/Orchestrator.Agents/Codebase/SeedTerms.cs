using System.Text.RegularExpressions;

namespace Orchestrator.Agents.Codebase;

/// <summary>
/// Pulls search terms out of a requirement: anything in backticks verbatim, plus distinctive
/// words (4+ letters, not in a small stop list). Deliberately dumb; the scoring tolerates noise.
/// </summary>
public static partial class SeedTerms
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "that", "this", "with", "from", "have", "will", "should", "must", "when", "then", "given", "which", "their",
        "there", "about", "into", "each", "every", "also", "than", "them", "they", "been", "being", "were", "what",
        "where", "while", "would", "could", "your", "make", "made", "need", "needs", "want", "using", "used", "uses",
        "currently", "current", "service", "system", "feature", "requirement", "please", "because", "still", "does",
        "under", "over", "after", "before", "same", "only", "more", "most", "some", "such", "very", "just", "like",
        "path", "code", "file", "files", "data", "time", "user", "users", "well", "keep", "kept", "table", "call", "calls",
    };

    public static IReadOnlyList<string> FromRequirement(string text)
    {
        var quoted = Backticked().Matches(text).Select(m => m.Groups[1].Value.Trim()).Where(t => t.Length >= 3);
        var words = Word().Matches(text).Select(m => m.Value)
            .Where(w => w.Length >= 4 && !StopWords.Contains(w))
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key)
            .Take(12);
        return quoted.Concat(words).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Backticked();

    [GeneratedRegex("[A-Za-z][A-Za-z0-9]+")]
    private static partial Regex Word();
}
