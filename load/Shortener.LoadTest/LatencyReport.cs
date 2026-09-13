using System.Globalization;
using System.Text;

namespace Shortener.LoadTest;

/// <summary>
/// The SLO for the redirect path. Stated here, in code, so the number in the docs and the number
/// the exit code enforces cannot drift apart.
/// </summary>
public sealed class LatencyReport(IReadOnlyList<double> samplesMs, int errors, TimeSpan duration, int concurrency)
{
    public const double SloP99Ms = 20.0;

    private readonly double[] _sorted = [.. samplesMs.Order()];

    public int Count => _sorted.Length;
    public double P50 => Percentile(0.50);
    public double P95 => Percentile(0.95);
    public double P99 => Percentile(0.99);
    public double RequestsPerSecond => Count / duration.TotalSeconds;
    public bool MeetsSlo => Count > 0 && errors == 0 && P99 <= SloP99Ms;

    private double Percentile(double p)
    {
        if (_sorted.Length == 0)
        {
            return double.NaN;
        }

        var index = (int)Math.Ceiling(p * _sorted.Length) - 1;
        return _sorted[Math.Clamp(index, 0, _sorted.Length - 1)];
    }

    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"redirect load: {concurrency} workers x {duration.TotalSeconds:0}s");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  requests : {Count} ({RequestsPerSecond:0} rps), errors {errors}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  p50 / p95 / p99 : {P50:0.00} / {P95:0.00} / {P99:0.00} ms");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  SLO p99 <= {SloP99Ms} ms : {(MeetsSlo ? "PASS" : "FAIL")}");
        return sb.ToString();
    }
}
