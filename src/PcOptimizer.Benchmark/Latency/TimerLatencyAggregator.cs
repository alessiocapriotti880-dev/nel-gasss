using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Statistics;

namespace PcOptimizer.Benchmark.Latency;

/// <summary>Accumula i ritardi di risveglio del timer in statistiche globali e per secondo.</summary>
internal sealed class TimerLatencyAggregator
{
    private readonly double _windowSeconds;
    private readonly Reservoir _sample = new(200_000, seed: 3);
    private readonly List<List<double>> _windows = [];

    public TimerLatencyAggregator(double windowSeconds = 1) => _windowSeconds = windowSeconds;

    public void Add(double elapsedSeconds, double latenessUs)
    {
        if (!double.IsFinite(latenessUs) || elapsedSeconds < 0)
        {
            return;
        }

        // Un risveglio in anticipo di qualche µs è arrotondamento del contatore: vale 0.
        latenessUs = Math.Max(0, latenessUs);
        var window = (int)(elapsedSeconds / _windowSeconds);
        while (_windows.Count <= window)
        {
            _windows.Add([]);
        }

        _windows[window].Add(latenessUs);
        _sample.Add(latenessUs);
    }

    public TimerLatencyResult Build(double durationSeconds, double intervalMs)
    {
        // L'ultima finestra può essere incompleta: si tiene solo se ha almeno metà dei campioni attesi.
        var expected = _windowSeconds * 1000 / Math.Max(intervalMs, 0.001);
        var windows = _windows.Where((w, i) => i < _windows.Count - 1 || w.Count >= expected / 2).Where(w => w.Count > 0).ToList();
        return new TimerLatencyResult
        {
            DurationSeconds = durationSeconds,
            IntervalMs = intervalMs,
            Samples = _sample.Seen,
            LatenessUs = Descriptive.Summarize(_sample.Items),
            WindowP99Us = windows.Select(w => Descriptive.Percentile(w, 99)).ToArray(),
            WindowMeanUs = windows.Select(w => w.Average()).ToArray(),
        };
    }
}
