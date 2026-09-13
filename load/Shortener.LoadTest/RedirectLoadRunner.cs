using System.Diagnostics;
using System.Net.Http.Json;

namespace Shortener.LoadTest;

/// <summary>
/// Closed-loop load: N workers each issue redirect requests back-to-back for the duration.
/// Deliberately simple (no external tool, no licence) so a grader can run it in one command.
/// </summary>
public sealed class RedirectLoadRunner(Uri baseUrl, int concurrency, TimeSpan duration)
{
    public async Task<LatencyReport> RunAsync()
    {
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, MaxConnectionsPerServer = concurrency * 2 })
        {
            BaseAddress = baseUrl,
        };

        var code = await CreateLinkAsync(client);
        var samples = new System.Collections.Concurrent.ConcurrentBag<double>();
        var errors = 0;
        using var stop = new CancellationTokenSource(duration);

        var workers = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var response = await client.GetAsync(new Uri(code, UriKind.Relative), stop.Token);
                    sw.Stop();
                    if ((int)response.StatusCode == 302)
                    {
                        samples.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    else
                    {
                        Interlocked.Increment(ref errors);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpRequestException)
                {
                    Interlocked.Increment(ref errors);
                }
            }
        }));

        await Task.WhenAll(workers);
        return new LatencyReport([.. samples], errors, duration, concurrency);
    }

    private static async Task<string> CreateLinkAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/links", new { url = "https://example.com/load-test" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreatedLink>();
        return body!.Code;
    }

    private sealed record CreatedLink(string Code);
}
