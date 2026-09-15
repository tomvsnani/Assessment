namespace Orchestrator.Core.Workflow;

/// <summary>
/// What happens to the workspace when a downstream verdict sends the run back to an earlier stage.
/// The escalation ladder is: retry inside the stage → <see cref="Fix"/> loop → safe-stop with full
/// rollback → human. <see cref="Rollback"/> collapses the middle rung: every loop starts from a clean
/// checkpoint, which is only right when the earlier work is unusable rather than merely wrong.
/// </summary>
public enum RerunMode
{
    /// <summary>Keep the earlier stage's files; it re-runs seeing its own work plus the feedback and is asked for the smallest change.</summary>
    Fix,

    /// <summary>Restore the earlier stage's checkpoint; it re-runs from scratch with only the feedback.</summary>
    Rollback,
}
