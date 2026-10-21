using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOptimizer.Benchmark;
using PcOptimizer.Benchmark.Comparison;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Statistics;
using PcOptimizer.Benchmark.Storage;
using PcOptimizer.Presentation.Charts;

namespace PcOptimizer.Presentation.ViewModels;

/// <summary>Voce dell'elenco delle misure salvate, selezionabile per il confronto.</summary>
public sealed partial class BenchmarkEntryViewModel(BenchmarkEntry entry) : ObservableObject
{
    public BenchmarkEntry Entry { get; } = entry;

    public string Title => $"{Entry.StartedAt:dd/MM/yyyy HH:mm}  {(Entry.Label.Length > 0 ? Entry.Label : "senza nome")}";

    public string Detail => $"{Entry.Scenario switch { BenchmarkScenario.Idle => "a riposo", BenchmarkScenario.CpuLoad => "sotto carico", _ => "in gioco" }}" +
                            (Entry.Score is { } s ? $" - punteggio {s}" : "");

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>Misure: latenza, rete, avvio e frame time; elenco delle misure e confronto statistico prima/dopo.</summary>
public sealed partial class BenchmarkViewModel : PageViewModel
{
    public BenchmarkViewModel(AppState state) : base(state, "Misure", "") => Refresh();

    [ObservableProperty]
    private int _durationSeconds = 60;

    [ObservableProperty]
    private bool _cpuLoad;

    [ObservableProperty]
    private bool _measureNetwork = true;

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _gameProcess = "";

    [ObservableProperty]
    private int _frameSeconds = 60;

    [ObservableProperty]
    private BenchmarkEntryViewModel? _selected;

    [ObservableProperty]
    private IReadOnlyList<ChartSeries> _chart = [];

    [ObservableProperty]
    private IReadOnlyList<ChartSeries> _frameChart = [];

    [ObservableProperty]
    private IReadOnlyList<ComparisonBar> _comparison = [];

    [ObservableProperty]
    private string _comparisonNote = "Seleziona due o più misure (le più vecchie fanno da \"prima\") e premi Confronta.";

    public ObservableCollection<BenchmarkEntryViewModel> Runs { get; } = [];

    public ObservableCollection<InfoRow> Details { get; } = [];

    public ObservableCollection<FindingItem> Findings { get; } = [];

    public ObservableCollection<InfoRow> ComparisonRows { get; } = [];

    [RelayCommand]
    private async Task RunLatencyAsync()
    {
        var options = new BenchmarkOptions
        {
            Label = Label,
            LatencyDuration = TimeSpan.FromSeconds(Math.Clamp(DurationSeconds, 10, 600)),
            CpuLoad = CpuLoad,
            Network = MeasureNetwork,
        };
        await RunAsync(options);
    }

    [RelayCommand]
    private async Task RunFramesAsync()
    {
        if (string.IsNullOrWhiteSpace(GameProcess))
        {
            State.Dialogs.Inform("Gioco mancante", "Scrivi il nome dell'eseguibile del gioco (es. cs2.exe) e avvia il gioco prima della misura.");
            return;
        }

        await RunAsync(new BenchmarkOptions
        {
            Label = Label.Length > 0 ? Label : GameProcess,
            Frames = new FrameCaptureOptions(GameProcess.Trim(), Math.Clamp(FrameSeconds, 10, 600)),
        });
    }

    [RelayCommand]
    private void Cancel() => State.Cancel();

    [RelayCommand]
    public void Refresh()
    {
        Runs.Clear();
        foreach (var e in State.Services.ListBenchmarks())
        {
            Runs.Add(new BenchmarkEntryViewModel(e));
        }
    }

    [RelayCommand]
    private void Compare()
    {
        var chosen = Runs.Where(r => r.IsChecked).OrderBy(r => r.Entry.StartedAt).ToList();
        if (chosen.Count < 2)
        {
            ComparisonNote = "Seleziona almeno due misure.";
            return;
        }

        if (chosen.Select(c => c.Entry.Scenario).Distinct().Count() > 1)
        {
            ComparisonNote = "Le misure selezionate hanno scenari diversi (riposo, carico, gioco): il confronto non è affidabile.";
            return;
        }

        var runs = chosen.Select(c => State.Services.LoadBenchmark(c.Entry.Path)).ToList();
        // Prima metà (le più vecchie) contro seconda metà: 2 → 1/1, 3 → 1/2, 4 → 2/2.
        var before = runs.Take(Math.Max(1, runs.Count / 2)).ToList();
        var after = runs.Skip(before.Count).ToList();
        var comparisons = BenchmarkComparer.Compare(before, after);
        Comparison = ChartData.Bars(comparisons);
        ComparisonRows.Clear();
        foreach (var c in comparisons.Where(c => c.Verdict != ComparisonVerdict.Insufficient))
        {
            ComparisonRows.Add(new InfoRow(c.Title, $"{VerdictName(c.Verdict)}: {Format.Num(c.BaselineMedian, "0.##")} → {Format.Num(c.AfterMedian, "0.##")} {c.Unit} " +
                                                    $"({(double.IsFinite(c.RelativeChange) ? Format.Num(c.RelativeChange * 100, "+0.#;-0.#") + "%" : "n/d")}, p = {Format.Num(c.PValue, "0.###")})"));
        }

        ComparisonNote = $"{before.Count} misure prima, {after.Count} dopo. \"Migliorato\" solo se la differenza è statisticamente significativa (Mann-Whitney, p < 0,05) " +
                         "e supera la soglia minima e la variabilità fra le misure \"prima\".";
        Chart = ChartData.BeforeAfter(before[^1], after[^1]);
    }

    partial void OnSelectedChanged(BenchmarkEntryViewModel? value)
    {
        if (value is not null)
        {
            Show(State.Services.LoadBenchmark(value.Entry.Path));
        }
    }

    private async Task RunAsync(BenchmarkOptions options)
    {
        var run = await State.RunAsync("Misura in corso (non usare il PC)", (p, ct) => State.Services.RunBenchmarkAsync(options, p, ct));
        if (run is null)
        {
            return;
        }

        State.LatestRun = run;
        Refresh();
        Show(run);
    }

    internal void Show(BenchmarkRun run)
    {
        Details.Clear();
        if (run.Score is { } score)
        {
            Details.Add(new InfoRow("Punteggio di latenza", $"{score.Score}/100{(score.Partial ? " (parziale)" : "")}"));
        }

        if (run.DpcIsr is { } d)
        {
            Details.Add(new InfoRow("DPC", $"massimo {Format.Num(d.MaxDpcUs, "0")} µs, P95 dei massimi al secondo {Format.Num(Descriptive.Percentile(d.WindowMaxDpcUs, 95), "0")} µs"));
            Details.Add(new InfoRow("ISR", $"massimo {Format.Num(d.MaxIsrUs, "0")} µs"));
            foreach (var driver in d.Drivers.Take(3))
            {
                Details.Add(new InfoRow("  Driver", $"{driver.Module}{(driver.Description is null ? "" : $" ({driver.Description})")}: {Format.Num(Math.Max(driver.MaxDpcUs, driver.MaxIsrUs), "0")} µs"));
            }
        }

        if (run.Timer is { } t)
        {
            Details.Add(new InfoRow("Timer", $"ritardo mediano {Format.Num(t.LatenessUs.Median, "0")} µs, P99 {Format.Num(t.LatenessUs.P99, "0")} µs"));
        }

        if (run.Cpu is { Busy.Count: > 0 } cpu)
        {
            Details.Add(new InfoRow("CPU", $"uso medio {Format.Num(cpu.Busy.Mean)}%; " + string.Join(", ", cpu.TopProcesses.Take(3).Select(p => $"{p.Name} {Format.Num(p.CpuPercent)}%"))));
        }

        if (run.Frames is { } f)
        {
            Details.Add(new InfoRow("FPS", $"medi {Format.Num(f.AverageFps)}, 1% low {Format.Num(f.OnePercentLowFps)}, 0,1% low {Format.Num(f.PointOnePercentLowFps)}"));
            Details.Add(new InfoRow("Frame", $"P99 {Format.Num(f.FrameTimeMs.P99, "0.##")} ms, scatti {f.StutterCount}, {f.PresentMode ?? "modalità n/d"}"));
        }

        foreach (var target in run.Network?.Targets ?? [])
        {
            Details.Add(new InfoRow(target.Kind == "gateway" ? "Router" : "Internet", target.Received == 0
                ? $"{target.Target}: nessuna risposta"
                : $"{target.Target}: {Format.Num(target.RttMs.Median)} ms, jitter {Format.Num(target.JitterMs ?? 0, "0.##")} ms, persi {Format.Num(target.LossPercent)}%"));
        }

        if (run.Sensors is { Samples.Count: > 0 } s)
        {
            Details.Add(new InfoRow("Temperature", $"CPU max {(s.CpuTempMaxC is { } c ? Format.Num(c, "0") + " °C" : "n/d")}, GPU max {(s.GpuTempMaxC is { } g ? Format.Num(g, "0") + " °C" : "n/d")}"));
        }

        foreach (var note in run.Notes)
        {
            Details.Add(new InfoRow("Nota", note));
        }

        Findings.Clear();
        foreach (var finding in run.Findings)
        {
            Findings.Add(new FindingItem(Format.Severity(finding.Severity), finding.Severity.ToString(), finding.Area, finding.Title, finding.Detail, finding.Recommendation));
        }

        Chart = ChartData.Latency(run);
        FrameChart = ChartData.FrameTimes(run);
    }

    private static string VerdictName(ComparisonVerdict v) => v switch
    {
        ComparisonVerdict.Improved => "Migliorato",
        ComparisonVerdict.Worse => "Peggiorato",
        _ => "Invariato",
    };
}
