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
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Core.Windows;
using PcOptimizer.Optimization.Planning;
using PcOptimizer.Optimization.Session;
using PcOptimizer.Optimization.Windows;
using PcOptimizer.Tweaks;
using PcOptimizer.Tweaks.Context;
using PcOptimizer.Tweaks.Guidance;
using PcOptimizer.Core;
using PcOptimizer.Core.Logging;
using PcOptimizer.Core.Model;
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
    case "plan":
        return await PlanAsync(options);
    case "games":
        return Games(options);
    case "apply":
        return await ApplyAsync(options);
    case "session":
        return await SessionAsync(options);
    case "rollback":
        return Rollback(options);
    case "tune":
    {
        using var tuneLoggers = CreateLoggerFactory();
        return await TuneCommands.RunAsync(options, tuneLoggers, DataDirectories(options));
    }

    case "history":
        SessionPrinter.PrintHistory(new PcOptimizer.Core.Journal.ChangeJournal(new AppPaths().JournalFile).Transactions);
        return 0;
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
        PC Optimizer Pro - strumento a riga di comando
        Solo "apply", "session resume" e "rollback" modificano il sistema; tutti gli altri comandi sono in sola lettura.

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

        pcopt plan [--all] [--json <file>]
            Mostra il piano di ottimizzazione per la fascia del PC: interventi per sezione con stato attuale, rischio,
            evidenza e consigliati [x], più le indicazioni guidate. Sola lettura.

        pcopt apply (--recommended | --ids <id>,<id>...) [--confirm-security <id>,...] [--duration <s>] [--runs <n>]
                    [--no-measure] [--yes]
            Applica gli interventi scelti (id da "pcopt plan --json"). Prima crea SEMPRE un punto di ripristino (senza,
            non modifica nulla) e il backup delle impostazioni; misura il sistema, applica un intervento alla volta,
            rimisura e annulla quelli che non portano un miglioramento misurabile. Quelli con riavvio vengono giudicati
            dopo il riavvio (ripresa automatica all'accesso). --no-measure applica senza misurare né annullare.

        pcopt session status | resume | history
            Stato della sessione, ripresa dopo il riavvio, sessioni passate.

        pcopt rollback --all | <id intervento>
            Annulla tutti gli interventi attivi ("Ripristina tutto") o uno solo.

        pcopt history
            Giornale delle modifiche: ogni impostazione toccata con valore originale e nuovo.

        pcopt tune bios | methods | step <procedura> | record <procedura> <valore> stabile|instabile
        pcopt tune test cpu|ram|gpu|vram [--minutes <n>]
            Overclock e undervolt GUIDATI: l'app non scrive nel BIOS e non cambia frequenze o tensioni. "bios" mostra la
            checklist con i menu della tua scheda madre; "step" indica il valore da impostare, esegue il test di stabilità
            (interrotto oltre i limiti di temperatura) e registra l'esito; "test" esegue solo un test di stabilità.

        pcopt games list | add <percorso\gioco.exe> [--name <nome>] | remove <nome o exe>
            Giochi per gli interventi specifici (GPU preferita, priorità, ottimizzazioni a schermo intero).

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

static async Task<int> PlanAsync(string[] options)
{
    using var loggerFactory = CreateLoggerFactory();
    var paths = new AppPaths();
    var inspection = await Inspection.RunAsync(loggerFactory, DataDirectories(options), new GameLibraryStore(paths).Load());
    var engine = new TweakEngine(WindowsSettingProviders.CreateStore(), new PcOptimizer.Core.Journal.ChangeJournal(paths.JournalFile),
        new PcOptimizer.Core.Backup.BackupService(paths.Backups), loggerFactory.CreateLogger<TweakEngine>());
    var statuses = inspection.Tweaks.ToDictionary(t => t.Id, t => SafeDetect(engine, t, inspection.Context));
    var guidance = GuidanceCatalog.Build(inspection.Context);
    PlanPrinter.Print(inspection, t => statuses[t.Id], Flag(options, "--all"), guidance);
    await SaveJsonAsync(options, new
    {
        tier = inspection.Context.Tier,
        tweaks = inspection.Tweaks.Select(t => new
        {
            t.Id, t.Title, t.Group, t.Category, t.Risk, t.Evidence, t.RequiresReboot, t.AffectsSecurity, t.Recommended, t.MeasuredBy,
            Status = statuses[t.Id], t.WhatChanges, t.Why, t.ExpectedBenefit, t.Caution, t.HowToRevert,
            Changes = t.IsApplicable(inspection.Context, out var reason) ? t.PlanChanges(inspection.Context).Select(c => new { Target = c.Target.Describe(), Desired = c.Desired.ToString() }) : null,
            NotApplicable = reason,
        }),
        guidance,
    });
    return 0;
}

static async Task<int> ApplyAsync(string[] options)
{
    var paths = new AppPaths();
    paths.EnsureCreated();
    using var loggerFactory = CreateLoggerFactory();
    var inspection = await Inspection.RunAsync(loggerFactory, DataDirectories(options), new GameLibraryStore(paths).Load());
    var ids = Flag(options, "--recommended")
        ? TweakCatalog.Recommended(inspection.Tweaks, inspection.Context).Select(t => t.Id).ToList()
        : Option(options, "--ids")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    if (ids is null)
    {
        Console.Error.WriteLine("Indica gli interventi con --recommended oppure --ids <id>,<id> (id da \"pcopt plan --json\").");
        return 2;
    }

    var plan = PlanBuilder.Build(inspection.Tweaks, ids, inspection.Context,
        Option(options, "--confirm-security")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    SessionPrinter.PrintPlan(plan);
    if (!plan.IsValid)
    {
        Console.Error.WriteLine(plan.Problems.Count == 0 ? "Nessun intervento da applicare." : "Correggi la selezione e riprova.");
        return 2;
    }

    var sessionOptions = new SessionOptions { Measure = !Flag(options, "--no-measure"), BaselineRuns = IntOption(options, "--runs") ?? 2 };
    var duration = IntOption(options, "--duration") ?? 60;
    Console.WriteLine(sessionOptions.Measure
        ? $"\nOgni misura dura circa {duration} s: non usare il PC durante la sessione."
        : "\nApplicazione senza misure: nessun annullamento automatico.");
    if (!Flag(options, "--yes") && !Confirm("Procedere?"))
    {
        return 1;
    }

    var runner = CreateSessionRunner(paths, loggerFactory, duration, plan.All.Any(t => t.Group == PcOptimizer.Tweaks.Model.TweakGroup.Network));
    using var cancel = CancelOnCtrlC();
    var session = await runner.StartAsync(plan, inspection.Context, sessionOptions, new Progress<string>(m => Console.WriteLine($"  {m}")), cancel.Token);
    SessionPrinter.PrintSession(session);
    return session.Phase == SessionPhase.Aborted ? 1 : 0;
}

static async Task<int> SessionAsync(string[] options)
{
    var paths = new AppPaths();
    var store = new SessionStore(paths);
    switch (options.FirstOrDefault()?.ToLowerInvariant())
    {
        case "resume":
        {
            using var loggerFactory = CreateLoggerFactory();
            if (store.Current() is not { Phase: SessionPhase.AwaitingReboot })
            {
                Console.WriteLine("Nessuna sessione in attesa di riavvio.");
                return 0;
            }

            var inspection = await Inspection.RunAsync(loggerFactory, DataDirectories(options), new GameLibraryStore(paths).Load());
            var runner = CreateSessionRunner(paths, loggerFactory, IntOption(options, "--duration") ?? 60, network: true);
            using var cancel = CancelOnCtrlC();
            var session = await runner.ResumeAsync(inspection.Tweaks, new SessionOptions(), new Progress<string>(m => Console.WriteLine($"  {m}")), cancel.Token);
            SessionPrinter.PrintSession(session);
            return 0;
        }

        case "history":
            foreach (var s in store.History())
            {
                SessionPrinter.PrintSession(s);
            }

            return 0;
        default:
            if (store.Current() is { } current)
            {
                SessionPrinter.PrintSession(current);
            }
            else
            {
                Console.WriteLine("Nessuna sessione.");
            }

            return 0;
    }
}

static int Rollback(string[] options)
{
    var paths = new AppPaths();
    using var loggerFactory = CreateLoggerFactory();
    var runner = CreateSessionRunner(paths, loggerFactory, 60, network: false);
    if (Flag(options, "--all"))
    {
        var report = runner.RevertAll();
        Console.WriteLine($"Annullati {report.Reverted.Count} interventi.");
        foreach (var (tx, error) in report.Failed)
        {
            Console.Error.WriteLine($"Non annullato {tx}: {error}");
        }

        Console.WriteLine("Riavvia il PC se tra gli interventi annullati ce n'erano che richiedevano il riavvio.");
        return report.Success ? 0 : 1;
    }

    if (options.FirstOrDefault(o => !o.StartsWith("--", StringComparison.Ordinal)) is not { } id)
    {
        Console.Error.WriteLine("Uso: pcopt rollback --all | <id intervento>  (id da \"pcopt history\")");
        return 2;
    }

    if (!runner.RevertTweak(id))
    {
        Console.Error.WriteLine($"Nessun intervento attivo con id {id}.");
        return 1;
    }

    Console.WriteLine($"Annullato: {id}.");
    return 0;
}

static SessionRunner CreateSessionRunner(AppPaths paths, ILoggerFactory loggers, int durationSeconds, bool network)
{
    var journal = new PcOptimizer.Core.Journal.ChangeJournal(paths.JournalFile);
    var engine = new TweakEngine(WindowsSettingProviders.CreateStore(), journal, new PcOptimizer.Core.Backup.BackupService(paths.Backups), loggers.CreateLogger<TweakEngine>());
    var benchmarkOptions = new BenchmarkOptions
    {
        LatencyDuration = TimeSpan.FromSeconds(durationSeconds),
        Network = network,
        PingCount = 100,
        Boot = false,
    };
    var measurer = new BenchmarkMeasurer(new BenchmarkRunner(loggers, KnownDriverCatalog.Load(), paths), benchmarkOptions);

    // Con "dotnet run" il processo è dotnet.exe: la ripresa deve rilanciare la dll dell'app.
    var process = Environment.ProcessPath ?? "pcopt.exe";
    var isHost = Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
    var resume = new ScheduledTaskResumeScheduler(process,
        isHost ? $"\"{typeof(Inspection).Assembly.Location}\" session resume" : "session resume",
        loggers.CreateLogger<ScheduledTaskResumeScheduler>());

    return new SessionRunner(new EngineApplier(engine, journal), new WindowsRestorePointService(loggers.CreateLogger<WindowsRestorePointService>()),
        measurer, resume, new PowerContextRefresher(), new SystemClock(), new SessionStore(paths), new BenchmarkStore(paths),
        loggers.CreateLogger<SessionRunner>());
}

static bool Confirm(string question)
{
    Console.Write($"{question} (s/N) ");
    var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
    return answer is "s" or "si" or "sì" or "y" or "yes";
}

static CancellationTokenSource CancelOnCtrlC()
{
    var cancel = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancel.Cancel();
    };
    return cancel;
}

static TweakStatus SafeDetect(TweakEngine engine, ITweak tweak, TweakContext context)
{
    try
    {
        return engine.Detect(tweak, context);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException)
    {
        // Lettura non riuscita (es. impostazione protetta): l'intervento si mostra come da applicare.
        return TweakStatus.NotApplied;
    }
}

static int Games(string[] options)
{
    var store = new GameLibraryStore(new AppPaths());
    var sub = options.FirstOrDefault()?.ToLowerInvariant();
    try
    {
        var library = sub switch
        {
            "add" when options.Length > 1 => store.Add(options[1], Option(options, "--name")),
            "remove" when options.Length > 1 => store.Remove(options[1]),
            "list" or null => store.Load(),
            _ => null,
        };
        if (library is null)
        {
            Console.Error.WriteLine("Uso: pcopt games list | add <gioco.exe> [--name <nome>] | remove <nome o exe>");
            return 2;
        }

        ConsoleOutput.Heading(library.Games.Count == 0 ? "Nessun gioco indicato" : $"Giochi ({library.Games.Count})");
        foreach (var g in library.Games)
        {
            Console.WriteLine($"  {g.Name}: {g.ExecutablePath}");
        }

        return 0;
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
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
