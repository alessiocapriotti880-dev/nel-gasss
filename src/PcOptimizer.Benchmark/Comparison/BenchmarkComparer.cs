using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Statistics;

namespace PcOptimizer.Benchmark.Comparison;

public enum ComparisonVerdict
{
    /// <summary>Miglioramento statisticamente significativo e oltre la soglia minima.</summary>
    Improved,
    /// <summary>Peggioramento statisticamente significativo e oltre la soglia minima.</summary>
    Worse,
    /// <summary>Differenza non distinguibile dal rumore di misura.</summary>
    Unchanged,
    /// <summary>Dati mancanti o troppo pochi per decidere.</summary>
    Insufficient,
}

/// <param name="RelativeChange">(dopo - prima) / prima sulle mediane; negativo = valore più basso.</param>
/// <param name="NoiseFloor">Variazione fra le sessioni "prima" ripetute: un effetto più piccolo non è credibile.</param>
public sealed record MetricComparison(
    string MetricId,
    string Title,
    string Unit,
    bool LowerIsBetter,
    int BaselineSamples,
    int AfterSamples,
    double BaselineMedian,
    double AfterMedian,
    double RelativeChange,
    double PValue,
    double NoiseFloor,
    ComparisonVerdict Verdict);

/// <summary>
/// Confronto prima/dopo: test di Mann-Whitney sui campioni per secondo, più due soglie pratiche
/// (variazione minima della grandezza e variazione osservata fra sessioni "prima" ripetute).
/// Un intervento si considera utile solo se supera tutte e tre le verifiche: nessun risultato "a sentimento".
/// </summary>
public static class BenchmarkComparer
{
    public const double DefaultAlpha = 0.05;

    public static IReadOnlyList<MetricComparison> Compare(
        IReadOnlyList<BenchmarkRun> baseline,
        IReadOnlyList<BenchmarkRun> after,
        IEnumerable<string>? metricIds = null,
        double alpha = DefaultAlpha)
    {
        var metrics = metricIds is null
            ? MetricCatalog.All
            : metricIds.Select(id => MetricCatalog.Find(id) ?? throw new ArgumentException($"Grandezza sconosciuta: {id}", nameof(metricIds))).ToList();
        return metrics.Select(m => Compare(m, baseline, after, alpha)).ToList();
    }

    public static MetricComparison Compare(MetricDefinition metric, IReadOnlyList<BenchmarkRun> baseline, IReadOnlyList<BenchmarkRun> after, double alpha = DefaultAlpha)
    {
        var baselineRuns = baseline.Select(metric.Samples).Where(s => s is { Length: > 0 }).Select(s => s!).ToList();
        var afterRuns = after.Select(metric.Samples).Where(s => s is { Length: > 0 }).Select(s => s!).ToList();
        var a = baselineRuns.SelectMany(s => s).Where(double.IsFinite).ToArray();
        var b = afterRuns.SelectMany(s => s).Where(double.IsFinite).ToArray();

        if (a.Length < metric.MinSamples || b.Length < metric.MinSamples)
        {
            return new MetricComparison(metric.Id, metric.Title, metric.Unit, metric.LowerIsBetter, a.Length, b.Length,
                a.Length > 0 ? Descriptive.Median(a) : double.NaN, b.Length > 0 ? Descriptive.Median(b) : double.NaN,
                double.NaN, double.NaN, 0, ComparisonVerdict.Insufficient);
        }

        var medianA = Descriptive.Median(a);
        var medianB = Descriptive.Median(b);
        var delta = medianB - medianA;
        var relative = medianA != 0 ? delta / Math.Abs(medianA) : (delta == 0 ? 0 : Math.Sign(delta) * double.PositiveInfinity);
        var p = MannWhitney.Test(a, b).PValue;
        var noise = NoiseFloor(baselineRuns);
        var threshold = Math.Max(metric.MinRelativeChange, noise);

        var verdict = ComparisonVerdict.Unchanged;
        if (p < alpha && Math.Abs(relative) >= threshold && Math.Abs(delta) >= metric.MinAbsoluteChange)
        {
            var better = metric.LowerIsBetter ? delta < 0 : delta > 0;
            verdict = better ? ComparisonVerdict.Improved : ComparisonVerdict.Worse;
        }

        return new MetricComparison(metric.Id, metric.Title, metric.Unit, metric.LowerIsBetter, a.Length, b.Length,
            medianA, medianB, relative, p, noise, verdict);
    }

    /// <summary>Scarto relativo massimo fra le mediane delle singole sessioni di riferimento (0 con una sola sessione).</summary>
    internal static double NoiseFloor(IReadOnlyList<double[]> runs)
    {
        if (runs.Count < 2)
        {
            return 0;
        }

        var medians = runs.Select(r => Descriptive.Median(r)).ToArray();
        var center = Descriptive.Median(medians);
        return center == 0 ? 0 : (medians.Max() - medians.Min()) / Math.Abs(center);
    }
}
