using Npgsql;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Shortener.Infrastructure.Postgres;

/// <summary>
/// One resilience pipeline for every database call: timeout, bounded retry on transient errors,
/// circuit breaker so a dead database fails fast instead of piling up threads.
/// Numbers are conservative for a redirect path; tune with load-test evidence, not guesses.
/// </summary>
public static class PostgresResilience
{
    public static ResiliencePipeline Build() =>
        new ResiliencePipelineBuilder()
            .AddTimeout(new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(2) })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(50),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<NpgsqlException>(e => e.IsTransient),
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 20,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromSeconds(5),
                ShouldHandle = new PredicateBuilder().Handle<NpgsqlException>().Handle<TimeoutRejectedException>(),
            })
            .Build();
}
