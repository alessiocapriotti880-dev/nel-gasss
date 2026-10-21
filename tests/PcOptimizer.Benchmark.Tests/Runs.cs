using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Statistics;

namespace PcOptimizer.Benchmark.Tests;

/// <summary>Sessioni di misura sintetiche per i test di confronto e osservazioni.</summary>
internal static class Runs
{
    public static BenchmarkEnvironment Env => new() { LogicalProcessors = 8, UptimeMinutes = 60, OnAcPower = null };

    public static BenchmarkRun Latency(double dpcCenter, double timerCenter, int seed, int windows = 60, double spread = 0.1)
    {
        var random = new Random(seed);
        double Noisy(double center) => center * (1 + (random.NextDouble() - 0.5) * 2 * spread);
        var dpc = Enumerable.Range(0, windows).Select(_ => Noisy(dpcCenter)).ToArray();
        var timer = Enumerable.Range(0, windows).Select(_ => Noisy(timerCenter)).ToArray();
        return new BenchmarkRun
        {
            StartedAt = DateTimeOffset.UnixEpoch.AddMinutes(seed),
            Label = $"prova {seed}",
            Environment = Env,
            DpcIsr = new DpcIsrResult
            {
                WindowMaxDpcUs = dpc,
                WindowMaxIsrUs = dpc.Select(v => v / 4).ToArray(),
                WindowLoadPercent = dpc.Select(v => v / 1000).ToArray(),
                MaxDpcUs = dpc.Max(),
            },
            Timer = new TimerLatencyResult { Samples = windows * 1000, WindowP99Us = timer, WindowMeanUs = timer.Select(v => v / 3).ToArray(), LatenessUs = Descriptive.Summarize(timer) },
        };
    }
}
