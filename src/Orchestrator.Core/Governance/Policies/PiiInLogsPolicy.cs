using System.Text.RegularExpressions;

namespace Orchestrator.Core.Governance.Policies;

/// <summary>
/// Blocks C# log statements that interpolate a full URL, a client IP or a user agent.
/// A shortener's target URLs and visitor addresses are personal data; they belong in the
/// analytics store under a retention rule, never in application logs.
/// </summary>
public sealed partial class PiiInLogsPolicy : IPolicy
{
    public string Name => "pii-in-logs";

    public PolicyVerdict Evaluate(PolicyContext context)
    {
        foreach (var file in context.ChangedFiles.Where(f => f.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var match = LogCallWithPii().Match(file.Content);
            if (match.Success)
            {
                return PolicyVerdict.Block($"log statement includes '{match.Groups["prop"].Value}' in {file.Path}");
            }
        }

        return PolicyVerdict.Pass;
    }

    // Log<Level>( ... {TargetUrl} ... ) — the placeholder name reveals what is being logged.
    [GeneratedRegex(@"Log(?:Trace|Debug|Information|Warning|Error|Critical)\s*\((?:[^;]|\n)*?\{(?<prop>TargetUrl|FullUrl|Url|OriginalUrl|IpAddress|RemoteIp|ClientIp|Ip|UserAgent)\}", RegexOptions.Singleline)]
    private static partial Regex LogCallWithPii();
}
