namespace Orchestrator.Host;

/// <summary>Finds the repository root (the directory holding AgenticSdlc.sln) from wherever the CLI runs.</summary>
public sealed class RepositoryPaths
{
    private RepositoryPaths(string root) => Root = root;

    public string Root { get; }

    public string Workflows => Path.Combine(Root, "workflows");
    public string Prompts => Path.Combine(Root, "prompts");
    public string ClaudeMd => Path.Combine(Root, "CLAUDE.md");
    public string Recordings => Path.Combine(Root, "recordings");
    public string Workspaces => Path.Combine(Root, "workspace");
    public string Runs => Path.Combine(Root, "runs");

    public string WorkflowFile(string scenario) => Path.Combine(Workflows, scenario + ".yaml");
    public string RecordingDirectory(string scenario) => Path.Combine(Recordings, scenario);
    public string WorkspaceDirectory(string scenario) => Path.Combine(Workspaces, scenario);

    public static RepositoryPaths Locate()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgenticSdlc.sln")))
            {
                return new RepositoryPaths(directory.FullName);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Run this from inside the repository (AgenticSdlc.sln not found in any parent directory).");
    }
}
