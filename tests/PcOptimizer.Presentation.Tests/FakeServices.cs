using PcOptimizer.Analysis;
using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Evaluation;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Benchmark;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Statistics;
using PcOptimizer.Benchmark.Storage;
using PcOptimizer.Core;
using PcOptimizer.Core.Journal;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Safety;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Hardware;
using PcOptimizer.Hardware.Assessment;
using PcOptimizer.Hardware.Database;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Optimization.Judging;
using PcOptimizer.Optimization.Planning;
using PcOptimizer.Optimization.Session;
using PcOptimizer.Presentation.Services;
using PcOptimizer.Tuning.Bios;
using PcOptimizer.Tuning.Guides;
using PcOptimizer.Tuning.Stability;
using PcOptimizer.Tweaks;
using PcOptimizer.Tweaks.Context;
using PcOptimizer.Tweaks.Guidance;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Presentation.Tests;

/// <summary>Servizi simulati: l'analisi usa i moduli reali su un PC di prova, le operazioni sul sistema sono registrate.</summary>
internal sealed class FakeServices : IOptimizerServices, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pcopt-ui-" + Guid.NewGuid().ToString("N"));
    private readonly List<JournalTransaction> _journal = [];
    private readonly List<TuningProfile> _profiles = [];
    private GameLibrary _games = GameLibrary.Empty;

    public FakeServices() => Store = new BenchmarkStore(new AppPaths(_root));

    public BenchmarkStore Store { get; }

    public List<BenchmarkOptions> BenchmarkCalls { get; } = [];

    public List<OptimizationPlan> AppliedPlans { get; } = [];

    public List<StabilityTestKind> StabilityCalls { get; } = [];

    public int RevertAllCalls { get; private set; }

    public bool StabilityPasses { get; set; } = true;

    public OptimizationSession? Session { get; set; }

    public bool IsElevated => true;

    public string DataFolder => _root;

    public Task<SystemInspection> InspectAsync(GameLibrary games, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("analisi simulata");
        var profile = new HardwareProfile
        {
            Cpu = new CpuInfo { Vendor = CpuVendor.Intel, VendorId = "GenuineIntel", Family = 6, Model = 0xB7, BrandString = "13th Gen Intel(R) Core(TM) i7-13700K", PhysicalCores = 16, LogicalProcessors = 24, PerformanceCores = 8, EfficiencyCores = 8, HasSmt = true, MicrocodeRevision = 0x12F },
            Gpus = [new GpuInfo { Name = "NVIDIA GeForce RTX 4070", Vendor = GpuVendor.Nvidia, PciVendorId = "10DE", PciDeviceId = "2786", IsPhysicalPciDevice = true, DedicatedMemoryBytes = 12L << 30 }],
            Memory = new MemoryInfo { VisibleBytes = 32L << 30 },
            Disks = [new DiskInfo { FriendlyName = "NVMe SSD", MediaType = DiskMediaType.Ssd, IsSystemDisk = true, SizeBytes = 1L << 40 }],
            Board = new BoardInfo { BoardManufacturer = "ASUSTeK COMPUTER INC.", BoardProduct = "ROG STRIX Z790-F" },
            Os = new OsInfo { Build = 22631, ProductName = "Windows 11 Pro" },
        };
        var assessment = new HardwareAnalyzer(HardwareDatabase.Load()).Assess(profile);
        var snapshot = new SystemSnapshot
        {
            IsElevated = true,
            Power = new PowerSnapshot { ActiveScheme = PowerGuids.Balanced, ActiveSchemeName = "Bilanciato", Schemes = [new(PowerGuids.Balanced, "Bilanciato"), new(PowerGuids.HighPerformance, "Prestazioni elevate")] },
            Services = [new ServiceEntry { Name = "DiagTrack", StartMode = ServiceStartValues.Automatic, State = "Running" }],
            Registry = new Dictionary<string, SettingValue> { [RegistryProbes.HistoricalCapture.Id] = SettingValue.Of(1u), ["memory.largesystemcache"] = SettingValue.Of(1u) },
            Security = new SecuritySnapshot { VbsStatus = 2, SecurityServicesRunning = [2] },
        };
        var software = KnownSoftwareCatalog.Load();
        var report = new SystemAnalyzer(software).Analyze(snapshot, AnalysisContext.From(profile, assessment));
        var context = TweakCatalog.CreateContext(profile, assessment, snapshot, report, new TweakEnvironment(), software, games);
        var tweaks = TweakCatalog.Build(context);
        var bios = BiosGuide.Load();
        return Task.FromResult(new SystemInspection
        {
            Profile = profile,
            Assessment = assessment,
            Snapshot = snapshot,
            Report = report,
            Context = context,
            Tweaks = tweaks,
            Statuses = tweaks.ToDictionary(t => t.Id, t => t.IsApplicable(context, out _) ? TweakStatus.NotApplied : TweakStatus.NotApplicable),
            Guidance = GuidanceCatalog.Build(context),
            BiosChecklist = bios.Build(profile, assessment),
            TuningMethods = TuningMethods.For(profile, assessment, bios),
        });
    }

    public Task<BenchmarkRun> RunBenchmarkAsync(BenchmarkOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        BenchmarkCalls.Add(options);
        var run = Run(options.Label, 300 - BenchmarkCalls.Count * 100);
        Store.Save(run);
        return Task.FromResult(run);
    }

    public static BenchmarkRun Run(string label, double dpc, int seed = 1)
    {
        var random = new Random(seed);
        var windows = Enumerable.Range(0, 30).Select(_ => dpc * (0.95 + random.NextDouble() * 0.1)).ToArray();
        var timer = Enumerable.Range(0, 30).Select(_ => 200 * (0.95 + random.NextDouble() * 0.1)).ToArray();
        return new BenchmarkRun
        {
            StartedAt = DateTimeOffset.Now.AddSeconds(seed),
            Label = label,
            Environment = new BenchmarkEnvironment { LogicalProcessors = 8, UptimeMinutes = 30 },
            DpcIsr = new DpcIsrResult { WindowMaxDpcUs = windows, WindowMaxIsrUs = windows.Select(v => v / 5).ToArray(), WindowLoadPercent = windows.Select(_ => 0.05).ToArray(), MaxDpcUs = windows.Max() },
            Timer = new TimerLatencyResult { Samples = 30_000, WindowP99Us = timer, WindowMeanUs = timer, LatenessUs = Descriptive.Summarize(timer) },
            Score = new LatencyScore(80, false, []),
        };
    }

    public IReadOnlyList<BenchmarkEntry> ListBenchmarks() => Store.List();

    public BenchmarkRun LoadBenchmark(string path) => BenchmarkStore.Load(path);

    public Task<OptimizationSession> ApplyAsync(OptimizationPlan plan, TweakContext context, SessionOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        AppliedPlans.Add(plan);
        progress?.Report("Applico gli interventi");
        Session = new OptimizationSession
        {
            StartedAt = DateTimeOffset.Now,
            EndedAt = DateTimeOffset.Now,
            Phase = plan.RequiresReboot ? SessionPhase.AwaitingReboot : SessionPhase.Completed,
            RestorePoint = new RestorePointResult(RestorePointOutcome.Created, DateTime.Now, "Punto di ripristino creato."),
            Results = plan.All.Select(t => new SessionTweakResult
            {
                TweakId = t.Id,
                Title = t.Title,
                Applied = true,
                AfterReboot = t.RequiresReboot,
                Decision = t.RequiresReboot ? null : Decision.Keep,
                Reason = JudgementReason.Improved,
                Explanation = "Migliorato",
            }).ToList(),
        };
        return Task.FromResult(Session);
    }

    public OptimizationSession? CurrentSession() => Session;

    public Task<OptimizationSession> ResumeSessionAsync(IReadOnlyList<CatalogTweak> catalog, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Session = Session! with { Phase = SessionPhase.Completed };
        return Task.FromResult(Session);
    }

    public RevertReport RevertAll()
    {
        RevertAllCalls++;
        return new RevertReport([Guid.NewGuid()], []);
    }

    public bool RevertTweak(string tweakId) => false;

    public IReadOnlyList<JournalTransaction> Journal() => _journal;

    public Task<StabilityReport> RunStabilityTestAsync(StabilityTestKind kind, TimeSpan duration, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        StabilityCalls.Add(kind);
        return Task.FromResult(new StabilityReport { Kind = kind, Duration = duration, WorkUnits = 100, Errors = StabilityPasses ? 0 : 3 });
    }

    public IReadOnlyList<TuningProfile> TuningProfiles() => _profiles;

    public TuningProfile OpenTuningProfile(string methodId)
    {
        var profile = _profiles.LastOrDefault(p => p.MethodId == methodId && !p.Completed) ?? new TuningProfile { MethodId = methodId, StartedAt = DateTimeOffset.Now };
        SaveTuningProfile(profile);
        return profile;
    }

    public void SaveTuningProfile(TuningProfile profile)
    {
        _profiles.RemoveAll(p => p.Id == profile.Id);
        _profiles.Add(profile);
    }

    public IReadOnlyList<MaintenanceInfo> Maintenance() => [new("maintenance.temp-cleanup", "Pulizia", "Elimina file temporanei", Risk.Low)];

    public Task<string> PreviewMaintenanceAsync(string id, CancellationToken cancellationToken) => Task.FromResult("3 file (1 MB)");

    public Task<string> RunMaintenanceAsync(string id, CancellationToken cancellationToken) => Task.FromResult("Eliminati 3 file");

    public GameLibrary Games() => _games;

    public GameLibrary AddGame(string executablePath, string? name) =>
        _games = new GameLibrary([.. _games.Games, new GameEntry(name ?? Path.GetFileNameWithoutExtension(executablePath), executablePath)]);

    public GameLibrary RemoveGame(string executablePath) =>
        _games = new GameLibrary(_games.Games.Where(g => g.ExecutablePath != executablePath).ToList());

    public IReadOnlyList<string> LogTail(int lines) => ["riga di log"];

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

internal sealed class FakeDialogs : IDialogService
{
    public bool Answer { get; set; } = true;

    public string? Executable { get; set; }

    public List<string> Messages { get; } = [];

    public bool Confirm(string title, string message)
    {
        Messages.Add(title + ": " + message);
        return Answer;
    }

    public void Inform(string title, string message) => Messages.Add(title + ": " + message);

    public string? PickExecutable() => Executable;

    public void OpenFolder(string path) => Messages.Add("apri " + path);
}
