using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Orchestrator.Core.State;
using Orchestrator.Host.Runs;

namespace Orchestrator.Host.Endpoints;

/// <summary>
/// <c>GET /api/runs/{id}/events?after=N</c> as Server-Sent Events. For an active run: every event
/// with seq &gt; N, then live ones as they are appended, then an <c>end</c> event when the run
/// finishes. For a historical run: the file, then <c>end</c>. The page treats both identically.
/// </summary>
public static class EventStreamEndpoint
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void MapEventStream(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/runs/{id}/events", StreamAsync);

    private static async Task StreamAsync(string id, HttpContext http, RunRegistry registry, long after = 0)
    {
        var handle = registry.Active(id);
        var directory = registry.DirectoryOf(id);
        if (handle is null && directory is null)
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";
        var ct = http.RequestAborted;

        if (handle is null)
        {
            foreach (var evt in EventStore.ReadFile(Path.Combine(directory!, "events.jsonl")).Where(e => e.Seq > after))
            {
                await WriteAsync(http, evt, ct);
            }

            await WriteEndAsync(http, ct);
            return;
        }

        // Subscribe first, then replay history, so nothing appended in between is lost; dedupe by seq.
        var channel = Channel.CreateUnbounded<RunEvent>();
        void OnAppended(RunEvent e) => channel.Writer.TryWrite(e);
        handle.Events.Appended += OnAppended;
        var completion = handle.Completion.ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        try
        {
            var sent = after;
            foreach (var evt in handle.Events.All.Where(e => e.Seq > after))
            {
                await WriteAsync(http, evt, ct);
                sent = evt.Seq;
            }

            if (handle.IsFinished)
            {
                channel.Writer.TryComplete();
            }

            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                if (evt.Seq > sent)
                {
                    await WriteAsync(http, evt, ct);
                    sent = evt.Seq;
                }
            }

            await WriteEndAsync(http, ct);
        }
        catch (OperationCanceledException)
        {
            // client went away
        }
        finally
        {
            handle.Events.Appended -= OnAppended;
            await completion;
        }
    }

    private static async Task WriteAsync(HttpContext http, RunEvent evt, CancellationToken ct)
    {
        await http.Response.WriteAsync($"id: {evt.Seq}\ndata: {JsonSerializer.Serialize(evt, Json)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    private static async Task WriteEndAsync(HttpContext http, CancellationToken ct)
    {
        await http.Response.WriteAsync("event: end\ndata: {}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }
}
