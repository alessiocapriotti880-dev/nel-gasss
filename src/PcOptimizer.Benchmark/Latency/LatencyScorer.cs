using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Statistics;

namespace PcOptimizer.Benchmark.Latency;

/// <summary>
/// Punteggio di latenza 0-100. Ogni componente va da 100 (valore "buono") a 0 (valore "pessimo") su scala
/// logaritmica, perché le latenze si distribuiscono su ordini di grandezza. Soglie dalle indicazioni comuni
/// per l'audio in tempo reale e il gioco (LatencyMon: DPC/ISR sotto 500 µs accettabili, oltre 1000 µs problemi).
/// Per i DPC/ISR si usa il 95° percentile dei massimi per secondo: un singolo picco non decide il punteggio.
/// </summary>
public static class LatencyScorer
{
    public static LatencyScore? Compute(DpcIsrResult? dpc, TimerLatencyResult? timer)
    {
        var components = new List<ScoreComponent>();
        if (dpc is not null && dpc.WindowMaxDpcUs.Length > 0)
        {
            components.Add(Component("DPC (P95 dei massimi al secondo)", Descriptive.Percentile(dpc.WindowMaxDpcUs, 95), "µs", 100, 2000, 0.35));
            components.Add(Component("ISR (P95 dei massimi al secondo)", Descriptive.Percentile(dpc.WindowMaxIsrUs, 95), "µs", 50, 1000, 0.15));
            components.Add(Component("Tempo CPU in DPC/ISR", dpc.WindowLoadPercent.DefaultIfEmpty(0).Average(), "%", 0.2, 5, 0.15));
        }

        if (timer is not null && timer.Samples > 0)
        {
            components.Add(Component("Ritardo del timer (P99)", timer.LatenessUs.P99, "µs", 50, 2000, 0.35));
        }

        if (components.Count == 0)
        {
            return null;
        }

        var weight = components.Sum(c => c.Weight);
        var score = components.Sum(c => c.Score * c.Weight) / weight;
        return new LatencyScore((int)Math.Round(score), Partial: components.Count < 4, components);
    }

    internal static ScoreComponent Component(string name, double value, string unit, double good, double bad, double weight) =>
        new(name, value, unit, Scale(value, good, bad), weight);

    /// <summary>100 sotto <paramref name="good"/>, 0 sopra <paramref name="bad"/>, interpolazione logaritmica fra i due.</summary>
    internal static double Scale(double value, double good, double bad)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        if (value <= good)
        {
            return 100;
        }

        if (value >= bad)
        {
            return 0;
        }

        return 100 * (1 - Math.Log(value / good) / Math.Log(bad / good));
    }
}
