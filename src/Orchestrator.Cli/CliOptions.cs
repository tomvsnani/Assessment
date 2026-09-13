namespace Orchestrator.Cli;

/// <summary>Hand-rolled argument parsing; three commands do not justify a framework.</summary>
public sealed record CliOptions(
    string Command,
    string Target,
    bool Live,
    string Provider,
    string? Model,
    bool Unattended)
{
    public const string Usage = """
        usage:
          sdlc run <greenfield|brownfield|ambiguous> [--live] [--provider anthropic|openai|gemini] [--model <id>] [--unattended]
          sdlc graph <scenario>
          sdlc verify-audit <runs/dir>

        Without --live the scenario replays committed recordings and needs no API key.
        --live calls the provider (needs <PROVIDER>_API_KEY) and re-records the scenario.
        --unattended approves every gate automatically as "unattended-auto-approver"; decisions are labelled as such.
        """;

    public static CliOptions? Parse(string[] args)
    {
        if (args.Length < 2)
        {
            return null;
        }

        var command = args[0];
        if (command is not ("run" or "graph" or "verify-audit"))
        {
            return null;
        }

        var live = false;
        var unattended = false;
        var provider = Environment.GetEnvironmentVariable("LLM_PROVIDER") ?? "anthropic";
        var model = Environment.GetEnvironmentVariable("LLM_MODEL");

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--live":
                    live = true;
                    break;
                case "--unattended":
                    unattended = true;
                    break;
                case "--provider" when i + 1 < args.Length:
                    provider = args[++i];
                    break;
                case "--model" when i + 1 < args.Length:
                    model = args[++i];
                    break;
                default:
                    return null;
            }
        }

        return new CliOptions(command, args[1], live, provider, model, unattended);
    }
}
