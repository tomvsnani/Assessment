using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Orchestrator.Agents.Codebase;

/// <summary>
/// One entry per C# file: declared types, their public members, and the identifiers the file
/// references. Built with Roslyn syntax trees (no compilation needed), so it works on any
/// workspace state, even one that does not build yet.
/// </summary>
public sealed record RepoMap(IReadOnlyList<RepoMap.FileEntry> Files)
{
    public sealed record FileEntry(
        string Path,
        IReadOnlyList<TypeEntry> Types,
        IReadOnlySet<string> ReferencedIdentifiers,
        int LineCount);

    public sealed record TypeEntry(string Kind, string Name, IReadOnlyList<string> Members);

    public static RepoMap Build(IEnumerable<(string Path, string Source)> files)
    {
        var entries = new List<FileEntry>();
        foreach (var (path, source) in files.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
            var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .Select(t => new TypeEntry(t.Keyword.ValueText, Signature(t), PublicMembers(t)))
                .ToList();
            var identifiers = root.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Select(i => i.Identifier.ValueText)
                .ToHashSet(StringComparer.Ordinal);
            entries.Add(new FileEntry(path, types, identifiers, source.Count(c => c == '\n') + 1));
        }

        return new RepoMap(entries);
    }

    public IEnumerable<string> DeclaredTypeNames() =>
        Files.SelectMany(f => f.Types).Select(t => t.Name.Split('(', '<', ' ')[0]);

    public string Render()
    {
        var sb = new StringBuilder();
        foreach (var file in Files)
        {
            sb.Append(file.Path).Append(" (").Append(file.LineCount).AppendLine(" lines)");
            foreach (var type in file.Types)
            {
                sb.Append("  ").Append(type.Kind).Append(' ').AppendLine(type.Name);
                foreach (var member in type.Members)
                {
                    sb.Append("    ").AppendLine(member);
                }
            }
        }

        return sb.ToString();
    }

    private static string Signature(TypeDeclarationSyntax type)
    {
        var name = type.Identifier.ValueText + (type.TypeParameterList?.ToString() ?? string.Empty);
        var parameters = type.ParameterList?.ToString() ?? string.Empty;
        var bases = type.BaseList is { } b ? " : " + string.Join(", ", b.Types.Select(t => t.ToString())) : string.Empty;
        return name + parameters + bases;
    }

    private static List<string> PublicMembers(TypeDeclarationSyntax type)
    {
        var members = new List<string>();
        foreach (var member in type.Members)
        {
            var isPublic = member.Modifiers.Any(SyntaxKind.PublicKeyword) || type is InterfaceDeclarationSyntax;
            if (!isPublic)
            {
                continue;
            }

            switch (member)
            {
                case MethodDeclarationSyntax m:
                    members.Add($"{m.ReturnType} {m.Identifier.ValueText}{m.ParameterList}");
                    break;
                case PropertyDeclarationSyntax p:
                    members.Add($"{p.Type} {p.Identifier.ValueText} {{ get; }}");
                    break;
                case FieldDeclarationSyntax f when f.Modifiers.Any(SyntaxKind.ConstKeyword):
                    members.Add($"const {f.Declaration.Type} {string.Join(", ", f.Declaration.Variables.Select(v => v.Identifier.ValueText))}");
                    break;
                case RecordDeclarationSyntax or ClassDeclarationSyntax:
                    break; // nested types are listed by the outer DescendantNodes pass
                default:
                    break;
            }
        }

        return members;
    }
}
