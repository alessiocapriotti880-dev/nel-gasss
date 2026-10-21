using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Windows;
using PcOptimizer.Tuning.Guides;
using PcOptimizer.Tuning.Safety;
using PcOptimizer.Tuning.Stability;

namespace PcOptimizer.Tuning.Windows;

/// <summary>
/// Esegue un test di stabilità sorvegliando i sensori ogni secondo: se un limite di sicurezza viene superato il test si
/// ferma subito. A fine test conta anche gli errori hardware registrati da Windows (WHEA).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StabilityRunner(ILoggerFactory loggers)
{
    private readonly ILogger _log = loggers.CreateLogger<StabilityRunner>();

    /// <param name="memoryBytes">RAM da testare (predefinito: il 70% di quella libera).</param>
    public static IStressWorkload CreateWorkload(StabilityTestKind kind, bool useWarp = false, long? memoryBytes = null) => kind switch
    {
        StabilityTestKind.Cpu => new CpuStressTest(),
        StabilityTestKind.Memory => new MemoryTest(memoryBytes ?? MemoryBudget()),
        StabilityTestKind.Gpu => new GpuStressTest(useWarp: useWarp),
        _ => new GpuStressTest(vramBytes: useWarp ? 256L * 1024 * 1024 : VramBudget(), useWarp: useWarp),
    };

    public async Task<StabilityReport> RunAsync(
        IStressWorkload workload, TimeSpan duration, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.Now;
        using var sensors = new SensorSampler(loggers.CreateLogger<SensorSampler>());
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var samples = new List<SensorSample>();
        string? abort = null;
        var clock = Stopwatch.StartNew();

        var test = workload.RunAsync(stop.Token);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (clock.Elapsed < duration && !test.IsCompleted && await timer.WaitForNextTickAsync(stop.Token))
            {
                samples.Add(sensors.Read(clock.Elapsed.TotalSeconds).Sample);
                abort = SafetyLimits.Check(samples);
                if (abort is not null)
                {
                    _log.LogWarning("Test {Kind} interrotto: {Reason}", workload.Kind, abort);
                    progress?.Report($"Interrotto per sicurezza: {abort}");
                    break;
                }

                if ((int)clock.Elapsed.TotalSeconds % 30 == 0)
                {
                    var s = samples[^1];
                    progress?.Report($"{clock.Elapsed:mm\\:ss} - CPU {Temp(s.CpuTempC)}, GPU {Temp(s.GpuTempC)}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        stop.Cancel();
        (long WorkUnits, long Errors, IReadOnlyList<string> Notes) result;
        try
        {
            result = await test;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or SharpGen.Runtime.SharpGenException or OutOfMemoryException)
        {
            // Un driver video che si resetta (TDR) o la memoria esaurita durante il test sono a loro volta segni di instabilità.
            _log.LogWarning(ex, "Test {Kind} terminato con errore", workload.Kind);
            result = (0, 1, [$"Il test si è interrotto con un errore: {ex.Message}"]);
        }

        var whea = WheaReader.CountSince(started);
        var report = new StabilityReport
        {
            Kind = workload.Kind,
            StartedAt = started,
            Duration = clock.Elapsed,
            WorkUnits = result.WorkUnits,
            Errors = result.Errors,
            WheaErrors = whea,
            AbortReason = abort,
            Cancelled = abort is null && cancellationToken.IsCancellationRequested,
            Sensors = samples,
            Notes = result.Notes,
        };
        _log.LogInformation("Test {Kind}: {Units} unità, {Errors} errori, {Whea} errori WHEA, interruzione: {Abort}",
            report.Kind, report.WorkUnits, report.Errors, report.WheaErrors, report.AbortReason ?? "no");
        return report;
    }

    /// <summary>Quantità di RAM da testare: il 70% della memoria fisica libera, senza far usare il file di paging.</summary>
    public static long MemoryBudget()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)(status.AvailPhys * 0.7) : 1L << 30;
    }

    /// <summary>VRAM da testare: l'80% della memoria dedicata della GPU principale (o 2 GB se non rilevabile).</summary>
    public static long VramBudget(long? dedicatedBytes = null) =>
        dedicatedBytes is > 0 ? (long)(dedicatedBytes.Value * 0.8) : 2L * 1024 * 1024 * 1024;

    private static string Temp(double? c) => c is { } v ? $"{v:0} °C" : "n/d";

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
