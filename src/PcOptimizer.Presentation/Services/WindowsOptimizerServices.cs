using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using PcOptimizer.Analysis;
using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Evaluation;
using PcOptimizer.Analysis.Windows;
using PcOptimizer.Benchmark;
using PcOptimizer.Benchmark.Catalog;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Storage;
using PcOptimizer.Benchmark.Windows;
using PcOptimizer.Core;
using PcOptimizer.Core.Backup;
using PcOptimizer.Core.Journal;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Core.Windows;
using PcOptimizer.Hardware.Assessment;
using PcOptimizer.Hardware.Database;
using PcOptimizer.Hardware.Windows;
using PcOptimizer.Optimization.Planning;
using PcOptimizer.Optimization.Session;
using PcOptimizer.Optimization.Windows;
using PcOptimizer.Tuning.Bios;
using PcOptimizer.Tuning.Guides;
using PcOptimizer.Tuning.Stability;
using PcOptimizer.Tuning.Windows;
using PcOptimizer.Tweaks;
using PcOptimizer.Tweaks.Context;
using PcOptimizer.Tweaks.Guidance;
using PcOptimizer.Tweaks.Maintenance;
using PcOptimizer.Tweaks.Model;
using PcOptimizer.Tweaks.Windows;

namespace PcOptimizer.Presentation.Services;

/// <summary>Collegamento di tutti i moduli su Windows: è l'unico punto in cui l'interfaccia tocca il sistema.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsOptimizerServices : IOptimizerServices
{
    private readonly ILoggerFactory _loggers;
    private readonly AppPaths _paths;
    private readonly string[] _dataDirs;
    private readonly string _resumeExecutable;
    private readonly string _resumeArguments;
    private readonly IReadOnlyDictionary<string, IMaintenanceAction> _maintenance;

    /// <param name="resumeExecutable">Programma da rilanciare dopo il riavvio per riprendere la sessione.</param>
    public WindowsOptimizerServices(ILoggerFactory loggers, AppPaths paths, string[] dataDirs, string resumeExecutable, string resumeArguments)
    {
        _loggers = loggers;
        _paths = paths;
        _dataDirs = dataDirs;
        _resumeExecutable = resumeExecutable;
        _resumeArguments = resumeArguments;
        paths.EnsureCreated();
        _maintenance = new IMaintenanceAction[] { new TempCleanup(), new StandbyListPurge() }.ToDictionary(m => m.Id);
    }

    public bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public string DataFolder => _paths.Root;

    public async Task<SystemInspection> InspectAsync(GameLibrary games, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Rilevamento dell'hardware...");
        var profile = await new WindowsHardwareScanner(_loggers.CreateLogger<WindowsHardwareScanner>()).ScanAsync(cancellationToken);
        var assessment = new HardwareAnalyzer(HardwareDatabase.Load(_dataDirs)).Assess(profile);
        progress?.Report("Analisi della configurazione di Windows...");
        var software = KnownSoftwareCatalog.Load(_dataDirs);
        var snapshot = await new WindowsSystemCollector(_loggers.CreateLogger<WindowsSystemCollector>()).CollectAsync(cancellationToken);
        var report = new SystemAnalyzer(software).Analyze(snapshot, AnalysisContext.From(profile, assessment));
        progress?.Report("Preparazione degli interventi...");
        var environment = new TweakEnvironmentCollector(_loggers.CreateLogger<TweakEnvironmentCollector>()).Collect(profile, games);
        var context = TweakCatalog.CreateContext(profile, assessment, snapshot, report, environment, software, games);
        var tweaks = TweakCatalog.Build(context);
        var engine = Engine(out _);
        var statuses = tweaks.ToDictionary(t => t.Id, t =>
        {
            try
            {
                return engine.Detect(t, context);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return TweakStatus.NotApplied;
            }
        });
        var bios = BiosGuide.Load(_dataDirs);
        return new SystemInspection
        {
            Profile = profile,
            Assessment = assessment,
            Snapshot = snapshot,
            Report = report,
            Context = context,
            Tweaks = tweaks,
            Statuses = statuses,
            Guidance = GuidanceCatalog.Build(context),
            BiosChecklist = bios.Build(profile, assessment),
            TuningMethods = TuningMethods.For(profile, assessment, bios),
        };
    }

    public async Task<BenchmarkRun> RunBenchmarkAsync(BenchmarkOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var run = await BenchmarkRunner().RunAsync(options, progress, cancellationToken);
        new BenchmarkStore(_paths).Save(run);
        return run;
    }

    public IReadOnlyList<BenchmarkEntry> ListBenchmarks() => new BenchmarkStore(_paths).List();

    public BenchmarkRun LoadBenchmark(string path) => BenchmarkStore.Load(path);

    public Task<OptimizationSession> ApplyAsync(OptimizationPlan plan, TweakContext context, SessionOptions options, IProgress<string>? progress, CancellationToken cancellationToken) =>
        SessionRunner(network: plan.All.Any(t => t.Group == TweakGroup.Network)).StartAsync(plan, context, options, progress, cancellationToken);

    public OptimizationSession? CurrentSession() => new SessionStore(_paths).Current();

    public Task<OptimizationSession> ResumeSessionAsync(IReadOnlyList<CatalogTweak> catalog, IProgress<string>? progress, CancellationToken cancellationToken) =>
        SessionRunner(network: true).ResumeAsync(catalog, new SessionOptions(), progress, cancellationToken);

    public RevertReport RevertAll() => SessionRunner(network: false).RevertAll();

    public bool RevertTweak(string tweakId) => SessionRunner(network: false).RevertTweak(tweakId);

    public IReadOnlyList<JournalTransaction> Journal() => new ChangeJournal(_paths.JournalFile).Transactions;

    public async Task<StabilityReport> RunStabilityTestAsync(StabilityTestKind kind, TimeSpan duration, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var workload = StabilityRunner.CreateWorkload(kind);
        try
        {
            return await new StabilityRunner(_loggers).RunAsync(workload, duration, progress, cancellationToken);
        }
        finally
        {
            (workload as IDisposable)?.Dispose();
        }
    }

    public IReadOnlyList<TuningProfile> TuningProfiles() => new TuningProfileStore(_paths).Load();

    public TuningProfile OpenTuningProfile(string methodId) => new TuningProfileStore(_paths).Open(methodId);

    public void SaveTuningProfile(TuningProfile profile) => new TuningProfileStore(_paths).Save(profile);

    public IReadOnlyList<MaintenanceInfo> Maintenance() =>
        _maintenance.Values.Select(m => new MaintenanceInfo(m.Id, m.Title, m.Description, m.Risk)).ToList();

    public Task<string> PreviewMaintenanceAsync(string id, CancellationToken cancellationToken) => _maintenance[id].PreviewAsync(cancellationToken);

    public Task<string> RunMaintenanceAsync(string id, CancellationToken cancellationToken) => _maintenance[id].RunAsync(cancellationToken);

    public GameLibrary Games() => new GameLibraryStore(_paths).Load();

    public GameLibrary AddGame(string executablePath, string? name) => new GameLibraryStore(_paths).Add(executablePath, name);

    public GameLibrary RemoveGame(string executablePath) => new GameLibraryStore(_paths).Remove(executablePath);

    public IReadOnlyList<string> LogTail(int lines)
    {
        var file = Directory.Exists(_paths.Logs)
            ? new DirectoryInfo(_paths.Logs).EnumerateFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
            : null;
        if (file is null)
        {
            return [];
        }

        // Il file è aperto in scrittura dal logger: lettura condivisa.
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var all = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            all.Add(line);
        }

        return all.Skip(Math.Max(0, all.Count - lines)).ToList();
    }

    private TweakEngine Engine(out ChangeJournal journal)
    {
        journal = new ChangeJournal(_paths.JournalFile);
        return new TweakEngine(WindowsSettingProviders.CreateStore(), journal, new BackupService(_paths.Backups), _loggers.CreateLogger<TweakEngine>());
    }

    private BenchmarkRunner BenchmarkRunner() => new(_loggers, KnownDriverCatalog.Load(_dataDirs), _paths);

    private SessionRunner SessionRunner(bool network)
    {
        var engine = Engine(out var journal);
        var measurer = new BenchmarkMeasurer(BenchmarkRunner(), new BenchmarkOptions { Network = network, PingCount = 100, Boot = false });
        return new SessionRunner(
            new EngineApplier(engine, journal),
            new WindowsRestorePointService(_loggers.CreateLogger<WindowsRestorePointService>()),
            measurer,
            new ScheduledTaskResumeScheduler(_resumeExecutable, _resumeArguments, _loggers.CreateLogger<ScheduledTaskResumeScheduler>()),
            new PowerContextRefresher(),
            new SystemClock(),
            new SessionStore(_paths),
            new BenchmarkStore(_paths),
            _loggers.CreateLogger<SessionRunner>());
    }
}
