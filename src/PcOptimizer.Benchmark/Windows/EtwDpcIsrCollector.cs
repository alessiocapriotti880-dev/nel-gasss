using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using PcOptimizer.Benchmark.Catalog;
using PcOptimizer.Benchmark.Latency;
using PcOptimizer.Benchmark.Model;

namespace PcOptimizer.Benchmark.Windows;

/// <summary>
/// Misura i tempi di esecuzione di DPC e ISR con una sessione ETW del kernel, come LatencyMon e WPR.
/// Sola osservazione: nessun driver installato, nessuna interazione con altri processi. Richiede amministratore.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EtwDpcIsrCollector
{
    /// <summary>Nome dedicato: da Windows 8 più sessioni kernel convivono, così non si interrompe LatencyMon o WPR.</summary>
    public const string SessionName = "PcOptimizerPro-Latency";

    /// <summary>Primo intervallo scartato: l'avvio della sessione e l'elenco dei moduli generano un picco di eventi.</summary>
    private static readonly TimeSpan Warmup = TimeSpan.FromSeconds(1.5);

    private readonly ILogger _logger;

    public EtwDpcIsrCollector(ILogger logger) => _logger = logger;

    public static bool CanRun => TraceEventSession.IsElevated() == true;

    public async Task<DpcIsrResult> RunAsync(TimeSpan duration, KnownDriverCatalog drivers, CancellationToken cancellationToken = default)
    {
        if (!CanRun)
        {
            throw new UnauthorizedAccessException("La misura DPC/ISR richiede l'esecuzione come amministratore.");
        }

        var aggregator = new DpcIsrAggregator(Warmup.TotalMilliseconds, Environment.ProcessorCount);
        using var session = StartSession();

        var kernel = session.Source.Kernel;
        kernel.PerfInfoDPC += d => aggregator.AddDpc(d.TimeStampRelativeMSec, d.ElapsedTimeMSec * 1000, d.Routine);
        kernel.PerfInfoTimerDPC += d => aggregator.AddDpc(d.TimeStampRelativeMSec, d.ElapsedTimeMSec * 1000, d.Routine);
        kernel.PerfInfoISR += i => aggregator.AddIsr(i.TimeStampRelativeMSec, i.ElapsedTimeMSec * 1000, i.Routine);
        kernel.ImageDCStart += image => AddModule(aggregator, image);
        kernel.ImageLoad += image => AddModule(aggregator, image);

        var processing = Task.Factory.StartNew(() => session.Source.Process(), TaskCreationOptions.LongRunning);
        try
        {
            await Task.Delay(Warmup + duration, cancellationToken);
        }
        finally
        {
            session.Stop(noThrow: true);
            await processing.ConfigureAwait(false);
        }

        aggregator.AddLost(session.EventsLost);
        if (aggregator.Modules.Count == 0)
        {
            _logger.LogInformation("ETW non ha fornito l'elenco dei moduli: uso psapi");
            KernelModules.AddTo(aggregator.Modules);
        }

        var result = aggregator.Build(duration.TotalSeconds, drivers);
        _logger.LogInformation("DPC/ISR: {Dpc} DPC, {Isr} ISR, massimo DPC {MaxDpc:0} µs, ISR {MaxIsr:0} µs, moduli noti {Modules}, eventi persi {Lost}",
            result.DpcCount, result.IsrCount, result.MaxDpcUs, result.MaxIsrUs, aggregator.Modules.Count, result.LostEvents);
        return result;
    }

    /// <summary>
    /// Avvia la sessione con il nome dedicato; se il sistema non accetta una seconda sessione kernel (alcune
    /// edizioni e macchine virtuali) usa "NT Kernel Logger", senza però interrompere un altro strumento che la sta usando.
    /// </summary>
    private TraceEventSession StartSession()
    {
        const KernelTraceEventParser.Keywords flags =
            KernelTraceEventParser.Keywords.DeferedProcedureCalls |
            KernelTraceEventParser.Keywords.Interrupt |
            KernelTraceEventParser.Keywords.ImageLoad;

        TraceEventSession? dedicated = null;
        try
        {
            dedicated = new TraceEventSession(SessionName) { StopOnDispose = true, BufferSizeMB = 256 };
            dedicated.EnableKernelProvider(flags);
            return dedicated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            dedicated?.Dispose();
            _logger.LogInformation(ex, "Sessione kernel dedicata non disponibile: provo NT Kernel Logger");
        }

        var kernel = new TraceEventSession(
            KernelTraceEventParser.KernelSessionName,
            TraceEventSessionOptions.Create | TraceEventSessionOptions.NoRestartOnCreate) { StopOnDispose = true, BufferSizeMB = 256 };
        try
        {
            kernel.EnableKernelProvider(flags);
            return kernel;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            kernel.Dispose();
            throw new InvalidOperationException(
                "Impossibile avviare la sessione di misura del kernel: potrebbe essere già usata da un altro strumento " +
                $"(LatencyMon, Process Monitor, WPR). Chiudilo e ripeti la misura. Dettagli: {ex.Message}", ex);
        }
    }

    /// <summary>Solo i moduli del kernel (driver), cioè quelli nello spazio di sistema (processo 0).</summary>
    private static void AddModule(DpcIsrAggregator aggregator, ImageLoadTraceData image)
    {
        if (image.ProcessID is 0 or -1 || image.ImageBase >= 0xFFFF800000000000)
        {
            aggregator.Modules.Add(image.ImageBase, (ulong)Math.Max(0, image.ImageSize), image.FileName);
        }
    }
}
