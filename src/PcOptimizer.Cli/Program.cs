using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PcOptimizer.Analysis;
using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Evaluation;
using PcOptimizer.Analysis.Windows;
using PcOptimizer.Benchmark;
using PcOptimizer.Benchmark.Catalog;
using PcOptimizer.Benchmark.Comparison;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Storage;
using PcOptimizer.Benchmark.Windows;
using PcOptimizer.Cli;
using PcOptimizer.Core;
using PcOptimizer.Core.Logging;
using PcOptimizer.Hardware.Assessment;
using PcOptimizer.Hardware.Database;
using PcOptimizer.Hardware.Windows;

Console.OutputEncoding = Encoding.UTF8;

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "scan";
var options = args.Skip(1).ToArray();
switch (command)
{
    case "scan":
        return await ScanAsync(options);
    case "analyze":
        return await AnalyzeAsync(options);
    case "benchmark":
        return await BenchmarkAsync(options);
    case "compare":
        return Compare(options);
    case "help" or "--help" or "-h" or "/?":
        PrintHelp();
        return 0;
    default:
        Console.Error.WriteLine($"Comando sconosciuto: {command}");
        PrintHelp();
        return 2;
}

static void PrintHelp()
{
    Console.WriteLine("""
        PC Optimizer Pro - strumento a riga di comando (non modifica le impostazioni del sistema)

        pcopt scan [--json <file>] [--data <cartella>]
            Rileva l'hardware, calcola la fascia e mostra le osservazioni.

        pcopt analyze [--json <file>] [--data <cartella>]
            Analizza la configurazione di Windows: servizi, avvio, attività, driver, alimentazione,
            timer, interrupt, impostazioni di gioco, sicurezza e modifiche lasciate da altri ottimizzatori.
            Per il quadro completo va eseguito come amministratore.

        pcopt benchmark [--duration <s>] [--load] [--label <testo>] [--ping <host>|none] [--pings <n>]
                        [--no-network] [--no-boot] [--json <file>]
            Misura latenza (DPC/ISR via ETW, ritardo del timer), uso CPU, sensori, rete (ping/jitter) e tempi di
            avvio. Predefinito: 60 s a riposo; --load misura sotto un carico CPU sintetico. DPC/ISR e avvio
            richiedono un terminale come amministratore. Il risultato viene salvato e si confronta con "compare".

        pcopt benchmark frames --process <gioco.exe> [--seconds <s>] [--delay <s>] [--presentmon <exe>] [--label <testo>]
        pcopt benchmark frames --csv <file> [--process <gioco.exe>]
            Registra i frame di un gioco già avviato con PresentMon (da scaricare da GitHub e mettere in "tools")
            oppure importa un CSV di PresentMon/FrameView/CapFrameX: FPS medi, 1%/0,1% low, scatti, limite CPU/GPU.

        pcopt benchmark list
            Elenca le misure salvate.

        pcopt compare --before <id>[,<id>...] --after <id>[,<id>...]
            Confronta misure (id dall'elenco o percorsi di file): verdetto per ogni grandezza solo se la
            differenza è statisticamente significativa e supera la soglia minima.

            --json   salva tutti i dati raccolti e la valutazione in un file JSON
            --data   cartella con tabelle aggiornate (cpu-architectures.json, gpus.json, known-software.json, ...)
        """);
}

static async Task<int> ScanAsync(string[] options)
{
    using var loggerFactory = CreateLoggerFactory();
    Console.WriteLine("Scansione dell'hardware in corso...");
    var profile = await new WindowsHardwareScanner(loggerFactory.CreateLogger<WindowsHardwareScanner>()).ScanAsync();
    var assessment = new HardwareAnalyzer(HardwareDatabase.Load(DataDirectories(options))).Assess(profile);

    HardwareReportPrinter.Print(profile, assessment);
    await SaveJsonAsync(options, new { profile, assessment });
    return 0;
}

static async Task<int> AnalyzeAsync(string[] options)
{
    using var loggerFactory = CreateLoggerFactory();
    var dataDirs = DataDirectories(options);

    Console.WriteLine("Scansione dell'hardware in corso...");
    var hardware = await new WindowsHardwareScanner(loggerFactory.CreateLogger<WindowsHardwareScanner>()).ScanAsync();
    var hardwareAssessment = new HardwareAnalyzer(HardwareDatabase.Load(dataDirs)).Assess(hardware);

    Console.WriteLine("Analisi della configurazione di Windows in corso...");
    var snapshot = await new WindowsSystemCollector(loggerFactory.CreateLogger<WindowsSystemCollector>()).CollectAsync();
    var report = new SystemAnalyzer(KnownSoftwareCatalog.Load(dataDirs))
        .Analyze(snapshot, AnalysisContext.From(hardware, hardwareAssessment));

    ConsoleOutput.Heading("Analisi del sistema");
    ConsoleOutput.Line("Fascia", $"{TierClassifier.TierName(hardwareAssessment.Tier.Tier)} " +
                                   $"(punteggio {ConsoleOutput.Num(hardwareAssessment.Tier.Score)}/100: dettagli con \"pcopt scan\")");
    SystemReportPrinter.Print(snapshot, report);
    await SaveJsonAsync(options, new { hardware, hardwareAssessment, snapshot, report });
    return 0;
}

static async Task<int> BenchmarkAsync(string[] options)
{
    var paths = new AppPaths();
    var store = new BenchmarkStore(paths);
    var sub = options.FirstOrDefault()?.ToLowerInvariant();
    if (sub == "list")
    {
        BenchmarkReportPrinter.PrintList(store.List());
        return 0;
    }

    var benchmarkOptions = new BenchmarkOptions
    {
        Label = Option(options, "--label") ?? "",
        LatencyDuration = TimeSpan.FromSeconds(IntOption(options, "--duration") ?? 60),
        CpuLoad = Flag(options, "--load"),
        Network = !Flag(options, "--no-network"),
        Boot = !Flag(options, "--no-boot"),
        PingCount = IntOption(options, "--pings") ?? 200,
        InternetHost = Option(options, "--ping") is { } host ? (host.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : host) : "1.1.1.1",
    };
    if (sub == "frames")
    {
        var process = Option(options, "--process");
        var csv = Option(options, "--csv");
        if (process is null && csv is null)
        {
            Console.Error.WriteLine("Indica il gioco con --process <gioco.exe> oppure un file con --csv <file>.");
            return 2;
        }

        benchmarkOptions = benchmarkOptions with
        {
            Frames = new FrameCaptureOptions(process, IntOption(options, "--seconds") ?? 60, IntOption(options, "--delay") ?? 0, Option(options, "--presentmon"), csv),
        };
    }

    using var loggerFactory = CreateLoggerFactory();
    var drivers = KnownDriverCatalog.Load(DataDirectories(options));
    using var cancel = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancel.Cancel();
    };

    BenchmarkRun run;
    try
    {
        run = await new BenchmarkRunner(loggerFactory, drivers, paths)
            .RunAsync(benchmarkOptions, new Progress<string>(Console.WriteLine), cancel.Token);
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Misura annullata.");
        return 1;
    }

    BenchmarkReportPrinter.Print(run);
    try
    {
        var saved = store.Save(run);
        Console.WriteLine($"\nMisura salvata ({run.Id.ToString("N")[..8]}): {saved}");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Misura non salvata: {ex.Message}");
    }

    await SaveJsonAsync(options, run);
    return 0;
}

static int Compare(string[] options)
{
    var store = new BenchmarkStore(new AppPaths());
    var before = ResolveRuns(store, Option(options, "--before"));
    var after = ResolveRuns(store, Option(options, "--after"));
    if (before is null || after is null)
    {
        Console.Error.WriteLine("Uso: pcopt compare --before <id>[,<id>...] --after <id>[,<id>...]  (id da \"pcopt benchmark list\")");
        return 2;
    }

    var scenarios = before.Concat(after).Select(r => r.Scenario).Distinct().ToList();
    if (scenarios.Count > 1)
    {
        Console.WriteLine($"Attenzione: si confrontano scenari diversi ({string.Join(", ", scenarios)}): il risultato non è affidabile.");
    }

    BenchmarkReportPrinter.PrintComparison(BenchmarkComparer.Compare(before, after), before.Count, after.Count);
    return 0;
}

static List<BenchmarkRun>? ResolveRuns(BenchmarkStore store, string? ids)
{
    if (string.IsNullOrWhiteSpace(ids))
    {
        return null;
    }

    var entries = store.List();
    var runs = new List<BenchmarkRun>();
    foreach (var id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (File.Exists(id))
        {
            runs.Add(BenchmarkStore.Load(id));
            continue;
        }

        var matches = entries.Where(e => e.Id.ToString("N").StartsWith(id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1)
        {
            Console.Error.WriteLine(matches.Count == 0 ? $"Misura non trovata: {id}" : $"Id ambiguo: {id}");
            return null;
        }

        runs.Add(BenchmarkStore.Load(matches[0].Path));
    }

    return runs;
}

static bool Flag(string[] options, string name) => options.Any(o => o.Equals(name, StringComparison.OrdinalIgnoreCase));

static int? IntOption(string[] options, string name) =>
    int.TryParse(Option(options, name), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;

static string[] DataDirectories(string[] options) =>
    new[] { Option(options, "--data"), Path.Combine(AppContext.BaseDirectory, "data") }
        .Where(d => d is not null)
        .Select(d => d!)
        .ToArray();

static string? Option(string[] options, string name)
{
    var i = Array.FindIndex(options, o => o.Equals(name, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < options.Length ? options[i + 1] : null;
}

static async Task SaveJsonAsync(string[] options, object data)
{
    if (Option(options, "--json") is not { } path)
    {
        return;
    }

    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    });
    await File.WriteAllTextAsync(path, json);
    Console.WriteLine($"\nReport completo salvato in {Path.GetFullPath(path)}");
}

static ILoggerFactory CreateLoggerFactory()
{
    try
    {
        return AppLogging.CreateFactory(new AppPaths());
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Log su file non disponibile: {ex.Message}");
        return NullLoggerFactory.Instance;
    }
}
