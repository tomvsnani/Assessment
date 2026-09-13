namespace Orchestrator.Agents.Agents;

/// <summary>
/// Prompts are files, not string literals: <c>prompts/_shared.md</c> (rules every agent gets),
/// <c>prompts/&lt;role&gt;.md</c> (the role), and the repository's <c>CLAUDE.md</c> (the same
/// conventions a human contributor reads). Changing a prompt is a reviewable diff.
/// </summary>
public sealed class PromptLibrary(string promptsDirectory, string? claudeMdPath)
{
    public string SystemPromptFor(string role)
    {
        var shared = File.ReadAllText(Path.Combine(promptsDirectory, "_shared.md"));
        var rolePrompt = File.ReadAllText(RolePath(role));
        var conventions = claudeMdPath is not null && File.Exists(claudeMdPath)
            ? "\n\n# Repository conventions (CLAUDE.md)\n\n" + File.ReadAllText(claudeMdPath)
            : string.Empty;
        return shared + conventions + "\n\n# Your role\n\n" + rolePrompt;
    }

    public bool HasRole(string role) => File.Exists(RolePath(role));

    private string RolePath(string role) => Path.Combine(promptsDirectory, role + ".md");
}
