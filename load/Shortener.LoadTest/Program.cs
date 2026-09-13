using Shortener.LoadTest;

// Usage: dotnet run --project load/Shortener.LoadTest -- [baseUrl] [concurrency] [seconds]
// Creates one link, then hammers the redirect path and reports latency percentiles against the SLO.
var baseUrl = new Uri(args.Length > 0 ? args[0] : "http://localhost:8080");
var concurrency = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 32;
var seconds = args.Length > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 10;

var runner = new RedirectLoadRunner(baseUrl, concurrency, TimeSpan.FromSeconds(seconds));
var report = await runner.RunAsync();
Console.WriteLine(report.Render());
return report.MeetsSlo ? 0 : 1;
