namespace PcOptimizer.Benchmark.Statistics;

/// <summary>Riepilogo di una serie di misure. Percentili con interpolazione lineare (metodo R-7, come Excel).</summary>
public sealed record SampleSummary(
    int Count,
    double Mean,
    double Median,
    double P95,
    double P99,
    double Min,
    double Max,
    double StdDev)
{
    public static readonly SampleSummary Empty = new(0, 0, 0, 0, 0, 0, 0, 0);
}

public static class Descriptive
{
    public static SampleSummary Summarize(IEnumerable<double> values)
    {
        var sorted = values.Where(double.IsFinite).ToArray();
        if (sorted.Length == 0)
        {
            return SampleSummary.Empty;
        }

        Array.Sort(sorted);
        var mean = sorted.Average();
        var variance = sorted.Length > 1 ? sorted.Sum(v => (v - mean) * (v - mean)) / (sorted.Length - 1) : 0;
        return new SampleSummary(
            sorted.Length,
            mean,
            PercentileSorted(sorted, 50),
            PercentileSorted(sorted, 95),
            PercentileSorted(sorted, 99),
            sorted[0],
            sorted[^1],
            Math.Sqrt(variance));
    }

    /// <summary>Percentile (0-100) di una serie già ordinata in modo crescente.</summary>
    public static double PercentileSorted(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
        {
            return double.NaN;
        }

        var rank = Math.Clamp(percentile, 0, 100) / 100 * (sorted.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
    }

    public static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.Where(double.IsFinite).ToArray();
        Array.Sort(sorted);
        return PercentileSorted(sorted, percentile);
    }

    public static double Median(IEnumerable<double> values) => Percentile(values, 50);

    /// <summary>
    /// Riduce una serie a <paramref name="maxCount"/> punti per i grafici prendendo il massimo di ogni gruppo:
    /// i picchi (gli scatti che interessano) restano visibili.
    /// </summary>
    public static double[] DecimateMax(IReadOnlyList<double> values, int maxCount)
    {
        if (values.Count <= maxCount)
        {
            return values.ToArray();
        }

        var result = new double[maxCount];
        for (var i = 0; i < maxCount; i++)
        {
            var from = (int)((long)i * values.Count / maxCount);
            var to = (int)((long)(i + 1) * values.Count / maxCount);
            var max = double.MinValue;
            for (var j = from; j < to; j++)
            {
                max = Math.Max(max, values[j]);
            }

            result[i] = max;
        }

        return result;
    }
}
