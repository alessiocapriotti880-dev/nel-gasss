using PcOptimizer.Analysis.Model;
using PcOptimizer.Benchmark;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Storage;
using PcOptimizer.Core.Journal;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Optimization.Planning;
using PcOptimizer.Optimization.Session;
using PcOptimizer.Tuning.Bios;
using PcOptimizer.Tuning.Guides;
using PcOptimizer.Tuning.Stability;
using PcOptimizer.Tweaks.Context;
using PcOptimizer.Tweaks.Guidance;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Presentation.Services;

/// <summary>Tutto ciò che l'analisi produce, per le pagine dell'interfaccia.</summary>
public sealed record SystemInspection
{
    public required HardwareProfile Profile { get; init; }

    public required HardwareAssessment Assessment { get; init; }

    public required SystemSnapshot Snapshot { get; init; }

    public required SystemReport Report { get; init; }

    public required TweakContext Context { get; init; }

    public required IReadOnlyList<CatalogTweak> Tweaks { get; init; }

    public required IReadOnlyDictionary<string, TweakStatus> Statuses { get; init; }

    public required IReadOnlyList<GuidanceItem> Guidance { get; init; }

    public required IReadOnlyList<BiosChecklistItem> BiosChecklist { get; init; }

    public required IReadOnlyList<TuningMethod> TuningMethods { get; init; }
}

/// <summary>Azione di manutenzione senza rollback (sempre con anteprima).</summary>
public sealed record MaintenanceInfo(string Id, string Title, string Description, Risk Risk);

/// <summary>
/// Servizi dell'applicazione usati dai view model. L'implementazione reale collega i moduli su Windows;
/// i test usano una versione simulata.
/// </summary>
public interface IOptimizerServices
{
    bool IsElevated { get; }

    string DataFolder { get; }

    Task<SystemInspection> InspectAsync(GameLibrary games, IProgress<string>? progress, CancellationToken cancellationToken);

    Task<BenchmarkRun> RunBenchmarkAsync(BenchmarkOptions options, IProgress<string>? progress, CancellationToken cancellationToken);

    IReadOnlyList<BenchmarkEntry> ListBenchmarks();

    BenchmarkRun LoadBenchmark(string path);

    Task<OptimizationSession> ApplyAsync(OptimizationPlan plan, TweakContext context, SessionOptions options, IProgress<string>? progress, CancellationToken cancellationToken);

    OptimizationSession? CurrentSession();

    Task<OptimizationSession> ResumeSessionAsync(IReadOnlyList<CatalogTweak> catalog, IProgress<string>? progress, CancellationToken cancellationToken);

    RevertReport RevertAll();

    bool RevertTweak(string tweakId);

    IReadOnlyList<JournalTransaction> Journal();

    Task<StabilityReport> RunStabilityTestAsync(StabilityTestKind kind, TimeSpan duration, IProgress<string>? progress, CancellationToken cancellationToken);

    IReadOnlyList<TuningProfile> TuningProfiles();

    TuningProfile OpenTuningProfile(string methodId);

    void SaveTuningProfile(TuningProfile profile);

    IReadOnlyList<MaintenanceInfo> Maintenance();

    Task<string> PreviewMaintenanceAsync(string id, CancellationToken cancellationToken);

    Task<string> RunMaintenanceAsync(string id, CancellationToken cancellationToken);

    GameLibrary Games();

    GameLibrary AddGame(string executablePath, string? name);

    GameLibrary RemoveGame(string executablePath);

    /// <summary>Ultime righe del log dell'app.</summary>
    IReadOnlyList<string> LogTail(int lines);
}
