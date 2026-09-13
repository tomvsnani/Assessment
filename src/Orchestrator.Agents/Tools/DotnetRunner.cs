using System.Diagnostics;
using System.Text;

namespace Orchestrator.Agents.Tools;

/// <summary>
/// Runs <c>dotnet</c> inside the workspace with a timeout and bounded output. Only the verbs the
/// tools expose can reach it; agents never get a general shell.
/// </summary>
public static class DotnetRunner
{
    private const int MaxOutputChars = 12_000;

    public sealed record Result(int ExitCode, string Output)
    {
        public bool Succeeded => ExitCode == 0;
    }

    public static async Task<Result> RunAsync(string workingDirectory, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start dotnet.");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        var stdout = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return new Result(-1, $"TIMEOUT after {timeout.TotalSeconds:0}s running: dotnet {arguments}");
        }

        var output = Trim(await stdout + "\n" + await stderr);
        return new Result(process.ExitCode, output);
    }

    /// <summary>Keeps the head and the tail; the interesting lines (errors, summary) live at both ends.</summary>
    private static string Trim(string output)
    {
        var cleaned = new StringBuilder();
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Length > 0 && !trimmed.StartsWith("  Determining projects to restore", StringComparison.Ordinal))
            {
                cleaned.AppendLine(trimmed);
            }
        }

        var text = cleaned.ToString();
        if (text.Length <= MaxOutputChars)
        {
            return text;
        }

        var half = MaxOutputChars / 2;
        return text[..half] + "\n... (output truncated) ...\n" + text[^half..];
    }
}
