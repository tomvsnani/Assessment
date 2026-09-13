using Orchestrator.Core.Workflow;

namespace Orchestrator.Core.Engine;

/// <summary>
/// Bounded, jittered exponential backoff for stage attempts. The bound comes from the workflow
/// (<see cref="RetryDefinition.MaxAttempts"/>); this class only decides "again?" and "after how long?".
/// </summary>
public static class RetryPolicy
{
    private const double JitterFraction = 0.25;

    public static bool CanRetry(RetryDefinition retry, int attemptJustFailed) => attemptJustFailed < retry.MaxAttempts;

    public static TimeSpan DelayBefore(RetryDefinition retry, int nextAttempt)
    {
        if (retry.BaseDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var exponential = retry.BaseDelay * Math.Pow(2, nextAttempt - 2); // attempt 2 waits BaseDelay, 3 waits 2x, ...
        var jitter = 1 + ((Random.Shared.NextDouble() * 2 - 1) * JitterFraction);
        return exponential * jitter;
    }
}
