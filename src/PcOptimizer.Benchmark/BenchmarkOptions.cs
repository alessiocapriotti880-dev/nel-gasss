namespace PcOptimizer.Benchmark;

/// <summary>Cosa misurare in una sessione. I valori predefiniti sono quelli della misura "di sistema" standard.</summary>
public sealed record BenchmarkOptions
{
    public string Label { get; init; } = "";

    /// <summary>DPC/ISR, risveglio del timer, uso CPU e sensori a riposo (o sotto carico sintetico).</summary>
    public bool Latency { get; init; } = true;

    public TimeSpan LatencyDuration { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Misura la latenza con un carico CPU sintetico invece che a riposo.</summary>
    public bool CpuLoad { get; init; }

    public bool Network { get; init; } = true;

    public int PingCount { get; init; } = 200;

    public TimeSpan PingInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Host Internet per il ping; null per misurare solo la rete locale.</summary>
    public string? InternetHost { get; init; } = "1.1.1.1";

    public bool Boot { get; init; } = true;

    /// <summary>Se impostato, la sessione registra i frame di un gioco con PresentMon (scenario Gioco).</summary>
    public FrameCaptureOptions? Frames { get; init; }
}

/// <param name="ProcessName">Eseguibile del gioco, es. "cs2.exe" (il gioco va avviato dall'utente).</param>
/// <param name="CsvFile">In alternativa a PresentMon: importa un CSV già registrato (PresentMon, FrameView, CapFrameX).</param>
public sealed record FrameCaptureOptions(
    string? ProcessName,
    int Seconds = 60,
    int DelaySeconds = 0,
    string? PresentMonPath = null,
    string? CsvFile = null);
