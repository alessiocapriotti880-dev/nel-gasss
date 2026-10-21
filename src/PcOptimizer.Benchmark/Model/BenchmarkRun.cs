using PcOptimizer.Benchmark.Statistics;
using PcOptimizer.Core.Model;

namespace PcOptimizer.Benchmark.Model;

/// <summary>Condizione in cui è stata eseguita la misura: si confrontano solo misure con lo stesso scenario.</summary>
public enum BenchmarkScenario
{
    /// <summary>Sistema a riposo (desktop, nessun programma pesante aperto).</summary>
    Idle,
    /// <summary>Carico sintetico sulla CPU generato dall'app (metà dei processori logici).</summary>
    CpuLoad,
    /// <summary>Gioco in esecuzione: frame time con PresentMon.</summary>
    Game,
}

/// <summary>
/// Una sessione di misura completa, salvata in %ProgramData%\PcOptimizerPro\benchmarks.
/// Ogni sezione è facoltativa: una prova non eseguibile (es. DPC senza privilegi) lascia una nota.
/// </summary>
public sealed record BenchmarkRun
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset StartedAt { get; init; }

    public string Label { get; init; } = "";

    public BenchmarkScenario Scenario { get; init; }

    public required BenchmarkEnvironment Environment { get; init; }

    public DpcIsrResult? DpcIsr { get; init; }

    public TimerLatencyResult? Timer { get; init; }

    public CpuActivityResult? Cpu { get; init; }

    public SensorSeries? Sensors { get; init; }

    public FrameResult? Frames { get; init; }

    public NetworkResult? Network { get; init; }

    public BootResult? Boot { get; init; }

    public LatencyScore? Score { get; init; }

    /// <summary>Prove saltate o parziali, con il motivo.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    public IReadOnlyList<Finding> Findings { get; init; } = [];
}

/// <summary>Contesto della misura, per capire se due sessioni sono confrontabili.</summary>
public sealed record BenchmarkEnvironment
{
    public string? WindowsBuild { get; init; }

    public bool IsElevated { get; init; }

    public string? Cpu { get; init; }

    public IReadOnlyList<string> Gpus { get; init; } = [];

    public int LogicalProcessors { get; init; }

    public Guid? PowerScheme { get; init; }

    public string? PowerSchemeName { get; init; }

    /// <summary>Risoluzione attuale del timer di sistema in ms (15,625 di default, più bassa se un programma la richiede).</summary>
    public double? TimerResolutionMs { get; init; }

    /// <summary>null su desktop senza batteria o se non rilevabile.</summary>
    public bool? OnAcPower { get; init; }

    public double UptimeMinutes { get; init; }
}

/// <summary>Statistiche di un driver (modulo del kernel) durante la misura DPC/ISR.</summary>
public sealed record DriverLatency(
    string Module,
    string? Description,
    long DpcCount,
    long IsrCount,
    double MaxDpcUs,
    double MaxIsrUs,
    double TotalMs,
    long Over500UsCount);

/// <summary>
/// Tempi di esecuzione di DPC e ISR (Deferred Procedure Call e Interrupt Service Routine) letti con ETW dal
/// kernel, come fa LatencyMon. Le serie per finestra (1 s) servono al confronto statistico.
/// </summary>
public sealed record DpcIsrResult
{
    public double DurationSeconds { get; init; }

    public long DpcCount { get; init; }

    public long IsrCount { get; init; }

    public double MaxDpcUs { get; init; }

    public double MaxIsrUs { get; init; }

    /// <summary>Distribuzione delle durate (da un campione uniforme degli eventi).</summary>
    public SampleSummary Dpc { get; init; } = SampleSummary.Empty;

    public SampleSummary Isr { get; init; } = SampleSummary.Empty;

    /// <summary>DPC più lungo di ogni secondo, in µs.</summary>
    public double[] WindowMaxDpcUs { get; init; } = [];

    public double[] WindowMaxIsrUs { get; init; } = [];

    /// <summary>Quota di tempo CPU spesa in DPC+ISR in ogni secondo, in % della capacità totale.</summary>
    public double[] WindowLoadPercent { get; init; } = [];

    /// <summary>Driver ordinati per DPC/ISR più lungo.</summary>
    public IReadOnlyList<DriverLatency> Drivers { get; init; } = [];

    public int LostEvents { get; init; }
}

/// <summary>
/// Ritardo di risveglio di un timer ad alta risoluzione da 1 ms: quanto tarda un thread ad alta priorità a
/// ripartire dopo la scadenza. Riassume l'effetto di DPC lunghi, stati di risparmio energetico profondi e
/// carico in background su ciò che vede un gioco.
/// </summary>
public sealed record TimerLatencyResult
{
    public double DurationSeconds { get; init; }

    public double IntervalMs { get; init; }

    public long Samples { get; init; }

    public SampleSummary LatenessUs { get; init; } = SampleSummary.Empty;

    public double[] WindowP99Us { get; init; } = [];

    public double[] WindowMeanUs { get; init; } = [];
}

/// <summary>Uso CPU durante la misura e processi più attivi (senza aprire handle sui processi).</summary>
public sealed record CpuActivityResult
{
    public double[] WindowBusyPercent { get; init; } = [];

    public SampleSummary Busy { get; init; } = SampleSummary.Empty;

    public IReadOnlyList<ProcessActivity> TopProcesses { get; init; } = [];
}

/// <param name="CpuPercent">Percentuale della capacità totale (tutti i processori logici = 100%).</param>
public sealed record ProcessActivity(string Name, int ProcessId, double CpuPercent);

/// <summary>Una lettura dei sensori (una al secondo).</summary>
public sealed record SensorSample
{
    public double TimeSeconds { get; init; }

    public double? CpuTempC { get; init; }

    public double? CpuPowerW { get; init; }

    public double? CpuCoreVoltage { get; init; }

    public bool? CpuThermalThrottling { get; init; }

    public double? GpuTempC { get; init; }

    public double? GpuHotspotC { get; init; }

    public double? GpuPowerW { get; init; }

    public double? GpuClockMHz { get; init; }

    public double? GpuMemoryClockMHz { get; init; }

    public double? GpuUsagePercent { get; init; }

    public double? GpuFanRpm { get; init; }

    /// <summary>Motivi di limitazione delle frequenze GPU (solo NVIDIA): potenza, temperatura...</summary>
    public IReadOnlyList<string> GpuLimits { get; init; } = [];
}

/// <summary>Serie dei sensori con le fonti disponibili.</summary>
public sealed record SensorSeries
{
    /// <summary>Fonti usate: "HWiNFO", "NVML", "D3DKMT".</summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    public IReadOnlyList<SensorSample> Samples { get; init; } = [];

    public double? CpuTempMaxC => Max(s => s.CpuTempC);

    public double? GpuTempMaxC => Max(s => s.GpuTempC);

    public double? GpuPowerAvgW => Avg(s => s.GpuPowerW);

    public double? GpuClockAvgMHz => Avg(s => s.GpuClockMHz);

    public double? CpuPowerAvgW => Avg(s => s.CpuPowerW);

    private double? Max(Func<SensorSample, double?> selector)
    {
        var values = Values(selector);
        return values.Length == 0 ? null : values.Max();
    }

    private double? Avg(Func<SensorSample, double?> selector)
    {
        var values = Values(selector);
        return values.Length == 0 ? null : values.Average();
    }

    private double[] Values(Func<SensorSample, double?> selector) =>
        Samples.Select(selector).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
}

/// <summary>Frame time di un gioco misurati con PresentMon (o importati da un CSV di PresentMon/FrameView/CapFrameX).</summary>
public sealed record FrameResult
{
    public string Application { get; init; } = "";

    /// <summary>"PresentMon 1.x", "PresentMon 2.x" o il file importato.</summary>
    public string Source { get; init; } = "";

    public double DurationSeconds { get; init; }

    public int Frames { get; init; }

    public double AverageFps { get; init; }

    /// <summary>1% low come 1000 / 99° percentile del frame time.</summary>
    public double OnePercentLowFps { get; init; }

    /// <summary>0,1% low come 1000 / 99,9° percentile del frame time.</summary>
    public double PointOnePercentLowFps { get; init; }

    public SampleSummary FrameTimeMs { get; init; } = SampleSummary.Empty;

    /// <summary>Frame con tempo superiore al doppio della mediana dei 60 precedenti.</summary>
    public int StutterCount { get; init; }

    public double DroppedPercent { get; init; }

    public double? AverageDisplayLatencyMs { get; init; }

    public double? AverageClickToPhotonMs { get; init; }

    /// <summary>Quota di frame in cui la GPU è occupata per almeno il 90% del frame time (limite GPU).</summary>
    public double? GpuBoundShare { get; init; }

    /// <summary>Modalità di presentazione prevalente (es. "Hardware: Independent Flip", "Composed: Flip").</summary>
    public string? PresentMode { get; init; }

    public double PresentModeShare { get; init; }

    public bool? VSync { get; init; }

    public bool? AllowsTearing { get; init; }

    public double[] WindowMeanFrameTimeMs { get; init; } = [];

    public double[] WindowP99FrameTimeMs { get; init; } = [];

    /// <summary>Frame time per il grafico (ridotti a un massimo di punti mantenendo i picchi).</summary>
    public double[] FrameTimesMs { get; init; } = [];
}

/// <summary>Esito del ping verso una destinazione.</summary>
public sealed record PingTargetResult
{
    public string Target { get; init; } = "";

    /// <summary>"gateway" (rete locale) o "internet".</summary>
    public string Kind { get; init; } = "";

    public int Sent { get; init; }

    public int Received { get; init; }

    public double LossPercent { get; init; }

    public SampleSummary RttMs { get; init; } = SampleSummary.Empty;

    /// <summary>Jitter medio: media delle differenze assolute fra ping consecutivi.</summary>
    public double? JitterMs { get; init; }

    public double[] RttSamplesMs { get; init; } = [];

    public double[] JitterSamplesMs { get; init; } = [];
}

public sealed record NetworkResult
{
    public string? AdapterName { get; init; }

    /// <summary>"Ethernet", "Wi-Fi" o altro.</summary>
    public string? AdapterType { get; init; }

    public IReadOnlyList<PingTargetResult> Targets { get; init; } = [];
}

/// <summary>Un avvio registrato da Windows (evento 100 di Diagnostics-Performance).</summary>
public sealed record BootRecord(DateTimeOffset Time, double BootMs, double MainPathMs, double PostBootMs, bool AfterUpdate);

public sealed record BootResult
{
    /// <summary>Avvii dal più recente.</summary>
    public IReadOnlyList<BootRecord> Boots { get; init; } = [];
}

/// <summary>Componente del punteggio di latenza.</summary>
public sealed record ScoreComponent(string Name, double Value, string Unit, double Score, double Weight);

/// <summary>
/// Punteggio di latenza 0-100, indicativo: utile per confrontare misure sullo stesso PC, non PC diversi.
/// </summary>
public sealed record LatencyScore(int Score, bool Partial, IReadOnlyList<ScoreComponent> Components);
