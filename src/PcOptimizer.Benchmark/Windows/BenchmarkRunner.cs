using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using PcOptimizer.Benchmark.Assessment;
using PcOptimizer.Benchmark.Catalog;
using PcOptimizer.Benchmark.Frames;
using PcOptimizer.Benchmark.Latency;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Network;
using PcOptimizer.Core;

namespace PcOptimizer.Benchmark.Windows;

/// <summary>
/// Esegue una sessione di misura completa. Ogni prova è indipendente: se una fallisce (privilegi, strumento
/// mancante, rete bloccata) le altre proseguono e il motivo finisce nelle note della sessione.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BenchmarkRunner
{
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _logger;
    private readonly KnownDriverCatalog _drivers;
    private readonly AppPaths _paths;

    public BenchmarkRunner(ILoggerFactory loggers, KnownDriverCatalog drivers, AppPaths paths)
    {
        _loggers = loggers;
        _logger = loggers.CreateLogger<BenchmarkRunner>();
        _drivers = drivers;
        _paths = paths;
    }

    public async Task<BenchmarkRun> RunAsync(BenchmarkOptions options, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();
        var started = DateTimeOffset.Now;
        using var sensors = new SensorSampler(_loggers.CreateLogger<SensorSampler>());
        var environment = EnvironmentReader.Read(sensors.GpuNames);
        var scenario = options.Frames is not null ? BenchmarkScenario.Game : options.CpuLoad ? BenchmarkScenario.CpuLoad : BenchmarkScenario.Idle;
        _logger.LogInformation("Sessione di misura \"{Label}\" ({Scenario})", options.Label, scenario);

        DpcIsrResult? dpc = null;
        TimerLatencyResult? timer = null;
        CpuActivityResult? cpu = null;
        SensorSeries? sensorSeries = null;
        FrameResult? frames = null;

        if (options.Frames is { } frameOptions)
        {
            (frames, cpu, sensorSeries) = await MeasureFramesAsync(frameOptions, sensors, notes, progress, cancellationToken);
        }
        else if (options.Latency)
        {
            (dpc, timer, cpu, sensorSeries) = await MeasureLatencyAsync(options, sensors, notes, progress, cancellationToken);
        }

        NetworkResult? network = null;
        if (options.Network && options.Frames is null)
        {
            network = await MeasureNetworkAsync(options, notes, progress, cancellationToken);
        }

        BootResult? boot = null;
        if (options.Boot && options.Frames is null)
        {
            boot = Try(() => BootTimeReader.Read(), "tempi di avvio", notes);
        }

        var run = new BenchmarkRun
        {
            StartedAt = started,
            Label = options.Label,
            Scenario = scenario,
            Environment = environment,
            DpcIsr = dpc,
            Timer = timer,
            Cpu = cpu,
            Sensors = sensorSeries,
            Frames = frames,
            Network = network,
            Boot = boot,
            Score = LatencyScorer.Compute(dpc, timer),
            Notes = notes,
        };
        return run with { Findings = BenchmarkFindings.Evaluate(run, _drivers) };
    }

    private async Task<(DpcIsrResult?, TimerLatencyResult?, CpuActivityResult?, SensorSeries?)> MeasureLatencyAsync(
        BenchmarkOptions options, SensorSampler sensors, List<string> notes, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var duration = options.LatencyDuration;
        progress?.Report($"Misura della latenza per {duration.TotalSeconds:0} s: non usare il PC durante la misura...");
        using var load = options.CpuLoad ? new SyntheticCpuLoad(SyntheticCpuLoad.DefaultThreads) : null;

        Task<DpcIsrResult>? dpcTask = null;
        if (EtwDpcIsrCollector.CanRun)
        {
            dpcTask = new EtwDpcIsrCollector(_loggers.CreateLogger<EtwDpcIsrCollector>()).RunAsync(duration, _drivers, cancellationToken);
        }
        else
        {
            notes.Add("DPC/ISR non misurati: serve l'esecuzione come amministratore.");
        }

        var timerTask = TimerLatencyProbe.RunAsync(duration, cancellationToken: cancellationToken);
        var cpuTask = CpuSampler.RunAsync(duration, cancellationToken);
        var sensorTask = sensors.RunAsync(duration, cancellationToken);

        var dpc = dpcTask is null ? null : await TryAsync(dpcTask, "DPC/ISR", notes);
        var timer = await TryAsync(timerTask, "ritardo del timer", notes);
        var cpu = await TryAsync(cpuTask, "uso CPU", notes);
        var sensorSeries = await TryAsync(sensorTask, "sensori", notes);
        if (sensorSeries is not null && !sensorSeries.Sources.Contains("HWiNFO"))
        {
            notes.Add("Temperatura e potenza della CPU non disponibili: avvia HWiNFO con \"Shared Memory Support\" attivo per leggerle.");
        }

        return (dpc, timer, cpu, sensorSeries);
    }

    private async Task<(FrameResult?, CpuActivityResult?, SensorSeries?)> MeasureFramesAsync(
        FrameCaptureOptions options, SensorSampler sensors, List<string> notes, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (options.CsvFile is { } csv)
        {
            var capture = PresentMonCsvParser.ParseFile(csv);
            var imported = FrameAnalyzer.Analyze(capture, options.ProcessName, $"{capture.Format} (importato: {Path.GetFileName(csv)})");
            if (imported is null)
            {
                notes.Add($"Nessun frame utilizzabile in {csv}{(options.ProcessName is null ? "" : $" per {options.ProcessName}")}.");
            }

            return (imported, null, null);
        }

        if (options.ProcessName is null)
        {
            throw new ArgumentException("Indica il processo del gioco o un file CSV da importare.");
        }

        var exe = PresentMonRunner.Locate(options.PresentMonPath, _paths.Root);
        if (exe is null)
        {
            notes.Add("PresentMon non trovato: scaricalo da github.com/GameTechDev/PresentMon/releases e copialo nella cartella \"tools\" " +
                      $"accanto all'app o in {Path.Combine(_paths.Root, "tools")}.");
            return (null, null, null);
        }

        var total = TimeSpan.FromSeconds(options.Seconds + options.DelaySeconds);
        progress?.Report($"Registrazione dei frame di {PresentMonArguments.ProcessName(options.ProcessName)} per {options.Seconds} s: gioca normalmente, ripetendo sempre la stessa scena...");
        var captureTask = new PresentMonRunner(_loggers.CreateLogger<PresentMonRunner>())
            .CaptureAsync(exe, options.ProcessName, options.Seconds, options.DelaySeconds, cancellationToken);
        var cpuTask = CpuSampler.RunAsync(total, cancellationToken);
        var sensorTask = sensors.RunAsync(total, cancellationToken);

        FrameResult? frames = null;
        try
        {
            var (capture, source) = await captureTask;
            frames = FrameAnalyzer.Analyze(capture, PresentMonArguments.ProcessName(options.ProcessName), source);
            if (frames is null)
            {
                notes.Add($"PresentMon non ha registrato frame di {options.ProcessName}: il gioco era in esecuzione e in primo piano?");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or System.ComponentModel.Win32Exception or IOException)
        {
            _logger.LogWarning(ex, "Registrazione dei frame non riuscita");
            notes.Add(ex.Message);
        }

        return (frames, await TryAsync(cpuTask, "uso CPU", notes), await TryAsync(sensorTask, "sensori", notes));
    }

    private async Task<NetworkResult> MeasureNetworkAsync(BenchmarkOptions options, List<string> notes, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var adapter = PingProbe.DetectPrimaryAdapter();
        var targets = new List<PingTargetResult>();
        progress?.Report($"Misura della rete ({options.PingCount} ping per destinazione)...");
        if (adapter?.Gateway is { } gateway)
        {
            targets.Add(await PingProbe.RunAsync(gateway.ToString(), "gateway", options.PingCount, options.PingInterval, cancellationToken));
        }
        else
        {
            notes.Add("Nessun router (gateway) rilevato: rete locale non misurata.");
        }

        if (options.InternetHost is { } host)
        {
            targets.Add(await PingProbe.RunAsync(host, "internet", options.PingCount, options.PingInterval, cancellationToken));
        }

        return new NetworkResult { AdapterName = adapter?.Name, AdapterType = adapter?.Type, Targets = targets };
    }

    private async Task<T?> TryAsync<T>(Task<T> task, string what, List<string> notes) where T : class
    {
        try
        {
            return await task;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Misura non riuscita: {What}", what);
            notes.Add($"Misura non riuscita ({what}): {ex.Message}");
            return null;
        }
    }

    private T? Try<T>(Func<T> read, string what, List<string> notes) where T : class
    {
        try
        {
            return read();
        }
        catch (System.Diagnostics.Eventing.Reader.EventLogNotFoundException)
        {
            notes.Add($"Non disponibile ({what}): il registro non esiste in questa edizione di Windows.");
            return null;
        }
        catch (Exception ex) when (ex is System.Diagnostics.Eventing.Reader.EventLogException or UnauthorizedAccessException)
        {
            _logger.LogInformation(ex, "Lettura non riuscita: {What}", what);
            notes.Add($"Non disponibile ({what}): {ex.Message}");
            return null;
        }
    }
}
