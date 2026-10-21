using PcOptimizer.Benchmark.Comparison;
using PcOptimizer.Benchmark.Model;

namespace PcOptimizer.Presentation.Charts;

/// <summary>Una serie di un grafico a linee: un valore per punto (es. un secondo di misura).</summary>
public sealed record ChartSeries(string Name, IReadOnlyList<double> Values, string ColorKey);

/// <summary>Barra di un confronto prima/dopo, con valori normalizzati 0-1 per il disegno.</summary>
public sealed record ComparisonBar(
    string Title,
    string Unit,
    double Before,
    double After,
    double BeforeRatio,
    double AfterRatio,
    string Change,
    ComparisonVerdict Verdict);

/// <summary>Conversione dei risultati del Benchmark in dati pronti per i grafici dell'interfaccia.</summary>
public static class ChartData
{
    public const string BeforeColor = "Before";
    public const string AfterColor = "After";
    public const string AccentColor = "Accent";

    public static IReadOnlyList<ChartSeries> Latency(BenchmarkRun? run, string colorKey = AccentColor) =>
        run?.DpcIsr is { WindowMaxDpcUs.Length: > 0 } dpc
            ? [new ChartSeries($"DPC più lungo al secondo (µs) - {Label(run)}", dpc.WindowMaxDpcUs, colorKey)]
            : run?.Timer is { WindowP99Us.Length: > 0 } timer
                ? [new ChartSeries($"Ritardo del timer P99 (µs) - {Label(run)}", timer.WindowP99Us, colorKey)]
                : [];

    public static IReadOnlyList<ChartSeries> BeforeAfter(BenchmarkRun? before, BenchmarkRun? after) =>
        [.. Latency(before, BeforeColor), .. Latency(after, AfterColor)];

    public static IReadOnlyList<ChartSeries> FrameTimes(BenchmarkRun? run) =>
        run?.Frames is { FrameTimesMs.Length: > 0 } f ? [new ChartSeries($"Frame time (ms) - {f.Application}", f.FrameTimesMs, AccentColor)] : [];

    /// <summary>Barre del confronto per le grandezze misurate in entrambe le sessioni.</summary>
    public static IReadOnlyList<ComparisonBar> Bars(IEnumerable<MetricComparison> comparisons) =>
        comparisons
            .Where(c => c.Verdict != ComparisonVerdict.Insufficient && double.IsFinite(c.BaselineMedian) && double.IsFinite(c.AfterMedian))
            .Select(c =>
            {
                var max = Math.Max(Math.Abs(c.BaselineMedian), Math.Abs(c.AfterMedian));
                return new ComparisonBar(c.Title, c.Unit, c.BaselineMedian, c.AfterMedian,
                    max == 0 ? 0 : Math.Abs(c.BaselineMedian) / max, max == 0 ? 0 : Math.Abs(c.AfterMedian) / max,
                    double.IsFinite(c.RelativeChange) ? $"{(c.RelativeChange >= 0 ? "+" : "")}{c.RelativeChange * 100:0.#}%" : "n/d",
                    c.Verdict);
            })
            .ToList();

    /// <summary>Punti di una serie scalati in un rettangolo (asse Y da 0 al massimo, con il 10% di margine).</summary>
    public static IReadOnlyList<(double X, double Y)> Scale(IReadOnlyList<double> values, double width, double height, double? maxY = null)
    {
        if (values.Count == 0 || width <= 0 || height <= 0)
        {
            return [];
        }

        var top = (maxY ?? values.Where(double.IsFinite).DefaultIfEmpty(0).Max()) * 1.1;
        if (top <= 0)
        {
            top = 1;
        }

        var step = values.Count > 1 ? width / (values.Count - 1) : 0;
        return values.Select((v, i) => (i * step, height - Math.Clamp(double.IsFinite(v) ? v : 0, 0, top) / top * height)).ToList();
    }

    private static string Label(BenchmarkRun run) => run.Label.Length > 0 ? run.Label : run.StartedAt.ToString("dd/MM HH:mm");
}
