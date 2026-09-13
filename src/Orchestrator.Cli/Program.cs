using Orchestrator.Cli;

// sdlc run <scenario> [--live] [--provider anthropic|openai|gemini] [--model id] [--unattended]
// sdlc graph <scenario>
// sdlc verify-audit <runs/dir>
var options = CliOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine(CliOptions.Usage);
    return 2;
}

using var ctrlC = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // let the scheduler stop safely and roll back
    ctrlC.Cancel();
};

try
{
    return options.Command switch
    {
        "run" => await new RunCommand(options).ExecuteAsync(ctrlC.Token),
        "graph" => GraphCommand.Execute(options),
        "verify-audit" => VerifyAuditCommand.Execute(options),
        _ => throw new InvalidOperationException(),
    };
}
catch (Exception e) when (e is InvalidOperationException or FileNotFoundException or ArgumentException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}
