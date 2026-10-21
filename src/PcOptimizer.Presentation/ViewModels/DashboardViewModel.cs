using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOptimizer.Benchmark.Comparison;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Core.Journal;
using PcOptimizer.Core.Model;
using PcOptimizer.Optimization.Judging;
using PcOptimizer.Presentation.Charts;
using PcOptimizer.Tweaks;

namespace PcOptimizer.Presentation.ViewModels;

/// <summary>Riepilogo: fascia, punteggio di latenza, grafico prima/dopo, osservazioni principali e azioni rapide.</summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly Action<PageViewModel> _navigate;
    private readonly Func<PageViewModel> _benchmarkPage;
    private readonly Func<PageViewModel> _optimizationPage;

    public DashboardViewModel(AppState state, Action<PageViewModel> navigate, Func<PageViewModel> benchmarkPage, Func<PageViewModel> optimizationPage)
        : base(state, "Panoramica", "")
    {
        _navigate = navigate;
        _benchmarkPage = benchmarkPage;
        _optimizationPage = optimizationPage;
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppState.LatestRun))
            {
                Refresh();
            }
        };
    }

    [ObservableProperty]
    private string _tierName = "—";

    [ObservableProperty]
    private string _tierScore = "Analisi non ancora eseguita";

    [ObservableProperty]
    private string _tierDescription = "Avvia l'analisi per conoscere la fascia del PC e gli interventi consigliati.";

    [ObservableProperty]
    private string _latencyScore = "—";

    [ObservableProperty]
    private string _latencyNote = "Nessuna misura: esegui una misura di latenza.";

    [ObservableProperty]
    private string _sessionSummary = "Nessuna ottimizzazione applicata.";

    [ObservableProperty]
    private int _activeTweaks;

    [ObservableProperty]
    private IReadOnlyList<ChartSeries> _chart = [];

    [ObservableProperty]
    private IReadOnlyList<ComparisonBar> _bars = [];

    public ObservableCollection<InfoRow> Hardware { get; } = [];

    public ObservableCollection<FindingItem> TopFindings { get; } = [];

    [RelayCommand]
    private Task AnalyzeAsync() => State.InspectAsync();

    [RelayCommand]
    private void GoToBenchmark() => _navigate(_benchmarkPage());

    [RelayCommand]
    private void GoToOptimization() => _navigate(_optimizationPage());

    protected override void OnInspectionChanged() => Refresh();

    public void Refresh()
    {
        if (State.Inspection is { } inspection)
        {
            var tier = inspection.Assessment.Tier;
            TierName = Format.Tier(tier.Tier);
            TierScore = $"Punteggio hardware {Format.Num(tier.Score, "0")}/100";
            TierDescription = TierProfiles.Describe(tier.Tier);

            Hardware.Clear();
            var p = inspection.Profile;
            Hardware.Add(new InfoRow("CPU", p.Cpu.BrandString));
            Hardware.Add(new InfoRow("GPU", inspection.Assessment.PrimaryGpu?.Gpu.Name ?? "n/d"));
            Hardware.Add(new InfoRow("RAM", $"{Format.Num(inspection.Assessment.Memory.InstalledGB, "0")} GB {inspection.Assessment.Memory.Type}" +
                                            (inspection.Assessment.Memory.ConfiguredMTs is { } mts ? $" a {mts} MT/s" : "")));
            Hardware.Add(new InfoRow("Disco", inspection.Assessment.Storage.SystemDisk?.FriendlyName ?? "n/d"));
            Hardware.Add(new InfoRow("Windows", $"{p.Os.ProductName} {p.Os.DisplayVersion} (build {p.Os.Build})"));

            TopFindings.Clear();
            foreach (var f in inspection.Assessment.Findings.Concat(inspection.Report.Findings)
                         .Where(f => f.Severity >= FindingSeverity.Suggestion)
                         .OrderByDescending(f => f.Severity)
                         .Take(6))
            {
                TopFindings.Add(new FindingItem(Format.Severity(f.Severity), f.Severity.ToString(), f.Area, f.Title, f.Detail, f.Recommendation));
            }
        }

        var journal = State.Services.Journal();
        ActiveTweaks = journal.Count(t => t.State == TransactionState.Active);
        UpdateSession();
        UpdateLatency();
    }

    private void UpdateLatency()
    {
        var runs = State.Services.ListBenchmarks().Where(e => e.Scenario == BenchmarkScenario.Idle).Take(2).ToList();
        var latest = State.LatestRun ?? (runs.Count > 0 ? State.Services.LoadBenchmark(runs[0].Path) : null);
        if (latest?.Score is { } score)
        {
            LatencyScore = $"{score.Score}";
            LatencyNote = (score.Partial ? "Punteggio parziale (senza DPC/ISR: serve l'amministratore). " : "") +
                          $"Misura del {latest.StartedAt:dd/MM HH:mm}. Indicativo: confrontalo solo con misure di questo PC.";
        }

        // Grafico: misure di riferimento e finali dell'ultima sessione, altrimenti le ultime due misure a riposo.
        var session = State.Services.CurrentSession();
        BenchmarkRun? before = null, after = null;
        if (session is { BaselineRunIds.Count: > 0, FinalRunIds.Count: > 0 })
        {
            var entries = State.Services.ListBenchmarks();
            before = Load(entries, session.BaselineRunIds[0]);
            after = Load(entries, session.FinalRunIds[^1]);
        }
        else if (runs.Count == 2)
        {
            before = State.Services.LoadBenchmark(runs[1].Path);
            after = State.Services.LoadBenchmark(runs[0].Path);
        }

        Chart = before is not null && after is not null ? ChartData.BeforeAfter(before, after) : ChartData.Latency(latest);
        Bars = before is not null && after is not null
            ? ChartData.Bars(BenchmarkComparer.Compare([before], [after], [MetricCatalog.DpcMax, MetricCatalog.TimerP99, MetricCatalog.CpuBusy]))
            : [];
    }

    private BenchmarkRun? Load(IReadOnlyList<Benchmark.Storage.BenchmarkEntry> entries, Guid id) =>
        entries.FirstOrDefault(e => e.Id == id) is { } entry ? State.Services.LoadBenchmark(entry.Path) : null;

    private void UpdateSession()
    {
        if (State.Services.CurrentSession() is not { } s)
        {
            SessionSummary = "Nessuna ottimizzazione applicata.";
            return;
        }

        var kept = s.Results.Count(r => r.Applied && !r.Reverted && r.Decision == Decision.Keep);
        var reverted = s.Results.Count(r => r.Reverted);
        SessionSummary = s.Phase switch
        {
            Optimization.Session.SessionPhase.AwaitingReboot => "Sessione in attesa del riavvio: dopo l'accesso misura e decide sugli ultimi interventi.",
            Optimization.Session.SessionPhase.Aborted => $"Ultima sessione interrotta senza modifiche: {s.AbortReason}",
            _ => $"Ultima sessione del {s.StartedAt:dd/MM/yyyy}: {kept} interventi tenuti, {reverted} annullati perché senza miglioramento misurabile.",
        };
    }
}
