using System.Text.RegularExpressions;

namespace Orchestrator.Core.Governance.Policies;

/// <summary>
/// Blocks any artifact or changed file that contains something that looks like a credential.
/// Patterns are deliberately high-precision: a false block stops a run and needs a human.
/// </summary>
public sealed partial class NoSecretsPolicy : IPolicy
{
    public string Name => "no-secrets";

    private static readonly (string Label, Regex Pattern)[] Patterns =
    [
        ("Anthropic API key", AnthropicKey()),
        ("OpenAI API key", OpenAiKey()),
        ("AWS access key", AwsKey()),
        ("private key block", PrivateKey()),
        ("hard-coded password", PasswordLiteral()),
    ];

    public PolicyVerdict Evaluate(PolicyContext context)
    {
        foreach (var file in context.ChangedFiles)
        {
            if (Scan(file.Content) is { } hit)
            {
                return PolicyVerdict.Block($"{hit} in {file.Path}");
            }
        }

        foreach (var artifact in context.Outputs)
        {
            if (Scan(artifact.Content) is { } hit)
            {
                return PolicyVerdict.Block($"{hit} in artifact '{artifact.Name}'");
            }
        }

        return PolicyVerdict.Pass;
    }

    private static string? Scan(string content)
    {
        foreach (var (label, pattern) in Patterns)
        {
            if (pattern.IsMatch(content))
            {
                return label;
            }
        }

        return null;
    }

    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_\-]{20,}")]
    private static partial Regex AnthropicKey();

    [GeneratedRegex(@"\bsk-(?:proj-)?[A-Za-z0-9]{32,}")]
    private static partial Regex OpenAiKey();

    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b")]
    private static partial Regex AwsKey();

    [GeneratedRegex(@"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----")]
    private static partial Regex PrivateKey();

    // password = "something-real" in source code; excludes placeholders and env-var lookups.
    [GeneratedRegex(@"(?i)\b(?:password|pwd|secret)\s*[:=]\s*""(?!\s*""|\$|\{|<|%)[^""]{8,}""")]
    private static partial Regex PasswordLiteral();
}
