using PcOptimizer.Benchmark.Model;

namespace PcOptimizer.Benchmark.Comparison;

/// <summary>
/// Una grandezza confrontabile fra due serie di misure.
/// I campioni sono per finestra (un valore al secondo) per avere osservazioni il più possibile indipendenti.
/// </summary>
/// <param name="MinRelativeChange">Variazione relativa minima perché conti (sotto è rumore di misura, anche se "significativa").</param>
/// <param name="MinAbsoluteChange">Variazione assoluta minima nella stessa unità (evita "+50%" su valori minuscoli).</param>
/// <param name="MinSamples">Campioni minimi per ciascuna serie.</param>
public sealed record MetricDefinition(
    string Id,
    string Title,
    string Unit,
    bool LowerIsBetter,
    double MinRelativeChange,
    double MinAbsoluteChange,
    int MinSamples,
    Func<BenchmarkRun, double[]?> Samples);

/// <summary>Grandezze misurate dal modulo Benchmark. Gli interventi indicano quali usare per il verdetto.</summary>
public static class MetricCatalog
{
    public const string DpcMax = "dpc.max";
    public const string IsrMax = "isr.max";
    public const string DpcIsrLoad = "dpcisr.load";
    public const string TimerP99 = "timer.p99";
    public const string TimerMean = "timer.mean";
    public const string CpuBusy = "cpu.busy";
    public const string FrameMean = "frame.mean";
    public const string FrameP99 = "frame.p99";
    public const string PingGateway = "ping.gateway";
    public const string JitterGateway = "jitter.gateway";
    public const string PingInternet = "ping.internet";
    public const string JitterInternet = "jitter.internet";
    public const string BootTime = "boot.time";

    public static IReadOnlyList<MetricDefinition> All { get; } =
    [
        new(DpcMax, "DPC più lungo al secondo", "µs", true, 0.10, 20, 10, r => r.DpcIsr?.WindowMaxDpcUs),
        new(IsrMax, "ISR più lungo al secondo", "µs", true, 0.10, 10, 10, r => r.DpcIsr?.WindowMaxIsrUs),
        new(DpcIsrLoad, "Tempo CPU in DPC/ISR", "%", true, 0.10, 0.02, 10, r => r.DpcIsr?.WindowLoadPercent),
        new(TimerP99, "Ritardo del timer (P99 al secondo)", "µs", true, 0.10, 5, 10, r => r.Timer?.WindowP99Us),
        new(TimerMean, "Ritardo medio del timer", "µs", true, 0.05, 2, 10, r => r.Timer?.WindowMeanUs),
        new(CpuBusy, "Uso CPU durante la misura", "%", true, 0.10, 0.5, 10, r => r.Cpu?.WindowBusyPercent),
        new(FrameMean, "Frame time medio al secondo", "ms", true, 0.02, 0.05, 10, r => r.Frames?.WindowMeanFrameTimeMs),
        new(FrameP99, "Frame lenti (P99 al secondo)", "ms", true, 0.03, 0.1, 10, r => r.Frames?.WindowP99FrameTimeMs),
        new(PingGateway, "Ping verso il router", "ms", true, 0.05, 0.2, 20, r => Ping(r, "gateway")?.RttSamplesMs),
        new(JitterGateway, "Jitter verso il router", "ms", true, 0.10, 0.1, 20, r => Ping(r, "gateway")?.JitterSamplesMs),
        new(PingInternet, "Ping verso Internet", "ms", true, 0.05, 0.5, 20, r => Ping(r, "internet")?.RttSamplesMs),
        new(JitterInternet, "Jitter verso Internet", "ms", true, 0.10, 0.2, 20, r => Ping(r, "internet")?.JitterSamplesMs),
        // Un solo avvio per sessione: i campioni vengono da più sessioni (gli avvii più recenti di ciascuna).
        new(BootTime, "Tempo di avvio", "s", true, 0.05, 1, 3, r => r.Boot?.Boots.Take(1).Where(b => !b.AfterUpdate).Select(b => b.BootMs / 1000).ToArray()),
    ];

    public static MetricDefinition? Find(string id) => All.FirstOrDefault(m => m.Id == id);

    private static PingTargetResult? Ping(BenchmarkRun run, string kind) =>
        run.Network?.Targets.FirstOrDefault(t => t.Kind == kind && t.Received > 0);
}
