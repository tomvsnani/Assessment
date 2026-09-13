using System.Globalization;
using System.Text;

namespace Orchestrator.Agents.Codebase;

/// <summary>
/// Deterministic impact scoring. Seed terms (from the requirement) hit declared names and file
/// text; files that reference the types declared in hit files are pulled in as second-order
/// impact. No model involved, so the same requirement always yields the same candidate set —
/// which is what makes the brownfield walkthrough reproducible.
/// </summary>
public static class ImpactAnalysis
{
    private const int DeclaredNameHit = 5;
    private const int TextHit = 1;
    private const int IdentifierTextHit = 5; // `RecordClickAsync` in a requirement is a precise pointer, "click" is not
    private const int ReferenceHit = 2;

    public static ImpactReport Analyze(RepoMap map, IReadOnlyList<string> seedTerms, IReadOnlyDictionary<string, string> sources)
    {
        var terms = seedTerms.Where(t => t.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var scores = new Dictionary<string, (int Score, List<string> Reasons)>(StringComparer.Ordinal);

        foreach (var file in map.Files)
        {
            var score = 0;
            var reasons = new List<string>();
            foreach (var term in terms)
            {
                var declared = file.Types.Count(t => Contains(t.Name, term)) + file.Types.Sum(t => t.Members.Count(m => Contains(m, term)));
                if (declared > 0)
                {
                    score += declared * DeclaredNameHit;
                    reasons.Add($"declares '{term}' ({declared})");
                }

                var occurrences = CountOccurrences(sources.GetValueOrDefault(file.Path, string.Empty), term);
                if (occurrences > 0)
                {
                    score += Math.Min(occurrences, 10) * (LooksLikeIdentifier(term) ? IdentifierTextHit : TextHit);
                    reasons.Add($"mentions '{term}' x{occurrences}");
                }
            }

            if (score > 0)
            {
                scores[file.Path] = (score, reasons);
            }
        }

        // Second order: files referencing a type declared in a directly-hit file.
        var hitTypes = map.Files.Where(f => scores.ContainsKey(f.Path))
            .SelectMany(f => f.Types.Select(t => t.Name.Split('(', '<', ' ')[0]))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var file in map.Files.Where(f => !scores.ContainsKey(f.Path)))
        {
            var referenced = file.ReferencedIdentifiers.Intersect(hitTypes, StringComparer.Ordinal).ToList();
            if (referenced.Count > 0)
            {
                scores[file.Path] = (referenced.Count * ReferenceHit, [$"references {string.Join(", ", referenced.Take(4))}"]);
            }
        }

        var ranked = scores.OrderByDescending(kv => kv.Value.Score).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new ImpactReport.Entry(kv.Key, kv.Value.Score, kv.Value.Reasons))
            .ToList();
        return new ImpactReport(terms, ranked);
    }

    /// <summary>camelCase / PascalCase / snake_case / dotted names came from code, not prose.</summary>
    private static bool LooksLikeIdentifier(string term) =>
        term.Skip(1).Any(char.IsUpper) || term.Contains('_') || term.Contains('.');

    private static bool Contains(string haystack, string term) => haystack.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static int CountOccurrences(string text, string term)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += term.Length;
        }

        return count;
    }
}

public sealed record ImpactReport(IReadOnlyList<string> Terms, IReadOnlyList<ImpactReport.Entry> Entries)
{
    public sealed record Entry(string Path, int Score, IReadOnlyList<string> Reasons);

    public IEnumerable<string> TopPaths(int n) => Entries.Take(n).Select(e => e.Path);

    public string RenderMarkdown()
    {
        var sb = new StringBuilder();
        sb.Append("Seed terms: ").AppendLine(string.Join(", ", Terms));
        sb.AppendLine("| File | Score | Why |");
        sb.AppendLine("|------|------:|-----|");
        foreach (var entry in Entries)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {entry.Path} | {entry.Score} | {string.Join("; ", entry.Reasons)} |\n");
        }

        return sb.ToString();
    }
}
