using PcOptimizer.Benchmark.Comparison;
using PcOptimizer.Optimization.Session;
using PcOptimizer.Presentation.Charts;
using PcOptimizer.Presentation.ViewModels;
using PcOptimizer.Tuning.Guides;
using PcOptimizer.Tuning.Stability;

namespace PcOptimizer.Presentation.Tests;

public class ViewModelTests
{
    private static async Task<(MainViewModel Main, FakeServices Services, FakeDialogs Dialogs)> Started()
    {
        var services = new FakeServices();
        var dialogs = new FakeDialogs();
        var main = new MainViewModel(services, dialogs);
        await main.InitializeAsync(resume: false);
        return (main, services, dialogs);
    }

    [Fact]
    public async Task StartupAnalyzesAndFillsThePages()
    {
        var (main, services, _) = await Started();
        using var _s = services;

        Assert.NotNull(main.State.Inspection);
        Assert.Equal(6, main.Pages.Count);
        Assert.Same(main.Dashboard, main.SelectedPage);
        Assert.NotEqual("—", main.Dashboard.TierName);
        Assert.NotEmpty(main.Dashboard.Hardware);
        Assert.NotEmpty(main.Analysis.Hardware);
        Assert.NotEmpty(main.Analysis.Findings);
        Assert.Contains(main.Analysis.ModifiedSettings, r => r.Label.Contains("LargeSystemCache"));
        Assert.NotEmpty(main.Optimization.Groups);
        Assert.NotEmpty(main.Tuning.Bios);
        Assert.NotEmpty(main.Tuning.Methods);
        Assert.Equal("Pronto".Length > 0, main.State.Activity.Count > 0);
        Assert.False(main.State.IsBusy);
    }

    [Fact]
    public async Task RecommendedSelectionAndApply()
    {
        var (main, services, dialogs) = await Started();
        using var _s = services;
        var opt = main.Optimization;

        opt.SelectRecommendedCommand.Execute(null);
        var selected = opt.AllItems.Where(i => i.IsSelected).ToList();
        Assert.NotEmpty(selected);
        Assert.Contains(selected, i => i.Id == "power.plan.high-performance");
        Assert.DoesNotContain(selected, i => i.Tweak.AffectsSecurity);
        Assert.Contains("selezionati", opt.SelectionSummary);

        dialogs.Answer = false;
        await opt.ApplyCommand.ExecuteAsync(null);
        Assert.Empty(services.AppliedPlans);

        dialogs.Answer = true;
        await opt.ApplyCommand.ExecuteAsync(null);
        var plan = Assert.Single(services.AppliedPlans);
        Assert.Equal(selected.Select(s => s.Id).Order(), plan.All.Select(t => t.Id).Order());
        Assert.NotEmpty(opt.SessionResults);
        Assert.Contains("punto di ripristino", dialogs.Messages.Last(m => m.StartsWith("Applicare")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SecurityTweaksNeedExplicitConfirmation()
    {
        var (main, services, dialogs) = await Started();
        using var _s = services;
        var opt = main.Optimization;
        var hvci = opt.AllItems.Single(i => i.Id == "security.memory-integrity-off");
        Assert.True(hvci.IsApplicable);

        hvci.IsSelected = true;
        await opt.ApplyCommand.ExecuteAsync(null);
        Assert.Empty(services.AppliedPlans);
        Assert.Contains(dialogs.Messages, m => m.Contains("conferma esplicita"));

        hvci.SecurityConfirmed = true;
        await opt.ApplyCommand.ExecuteAsync(null);
        Assert.Single(services.AppliedPlans);
        Assert.Contains(dialogs.Messages, m => m.Contains("ATTENZIONE"));
    }

    [Fact]
    public async Task RevertAllAsksForConfirmation()
    {
        var (main, services, dialogs) = await Started();
        using var _s = services;
        dialogs.Answer = false;
        await main.Optimization.RevertAllCommand.ExecuteAsync(null);
        Assert.Equal(0, services.RevertAllCalls);
        dialogs.Answer = true;
        await main.Optimization.RevertAllCommand.ExecuteAsync(null);
        Assert.Equal(1, services.RevertAllCalls);
    }

    [Fact]
    public async Task RebootSessionCanBeResumed()
    {
        var (main, services, dialogs) = await Started();
        using var _s = services;
        var opt = main.Optimization;
        foreach (var item in opt.AllItems)
        {
            item.IsSelected = item.Id == "services.windows.diagtrack";
        }

        await opt.ApplyCommand.ExecuteAsync(null);
        Assert.True(opt.CanResume);
        Assert.Contains("riavvio", opt.SessionSummary);

        await opt.ResumeCommand.ExecuteAsync(null);
        Assert.False(opt.CanResume);
        Assert.Equal(SessionPhase.Completed, services.Session!.Phase);
    }

    [Fact]
    public async Task BenchmarkRunsAndCompares()
    {
        var (main, services, _) = await Started();
        using var _s = services;
        var bench = main.Benchmark;
        bench.DurationSeconds = 5000;

        await bench.RunLatencyCommand.ExecuteAsync(null);
        await bench.RunLatencyCommand.ExecuteAsync(null);

        Assert.Equal(600, services.BenchmarkCalls[0].LatencyDuration.TotalSeconds);
        Assert.NotNull(main.State.LatestRun);
        Assert.Equal(2, bench.Runs.Count);
        Assert.NotEmpty(bench.Details);
        Assert.NotEmpty(bench.Chart);

        foreach (var r in bench.Runs)
        {
            r.IsChecked = true;
        }

        bench.CompareCommand.Execute(null);
        Assert.NotEmpty(bench.ComparisonRows);
        Assert.Contains(bench.Comparison, b => b.Verdict == ComparisonVerdict.Improved);
        Assert.Equal(2, bench.Chart.Count);
    }

    [Fact]
    public async Task FramesNeedAGame()
    {
        var (main, services, dialogs) = await Started();
        using var _s = services;
        await main.Benchmark.RunFramesCommand.ExecuteAsync(null);
        Assert.Empty(services.BenchmarkCalls);
        Assert.Contains(dialogs.Messages, m => m.StartsWith("Gioco mancante"));

        main.Benchmark.GameProcess = "cs2.exe";
        await main.Benchmark.RunFramesCommand.ExecuteAsync(null);
        Assert.Equal("cs2.exe", services.BenchmarkCalls.Single().Frames!.ProcessName);
    }

    [Fact]
    public async Task TuningStepRecordsOutcomeAndAdvances()
    {
        var (main, services, _) = await Started();
        using var _s = services;
        var gpu = main.Tuning.Methods.Single(m => m.Method.Id == "gpu.undervolt");
        Assert.True(gpu.IsAvailable);
        Assert.Equal(-50, gpu.NextValue);

        await gpu.TestCurrentValueCommand.ExecuteAsync(null);

        Assert.Equal([StabilityTestKind.Gpu, StabilityTestKind.Vram], services.StabilityCalls);
        Assert.Equal(-75, gpu.NextValue);
        Assert.Single(gpu.History);

        services.StabilityPasses = false;
        await gpu.TestCurrentValueCommand.ExecuteAsync(null);
        Assert.Null(gpu.NextValue);
        Assert.Contains("-25", gpu.NextText); // ultimo stabile -50 meno un passo di margine
        Assert.Equal(StepOutcome.Unstable, services.TuningProfiles().Single().Steps[^1].Outcome);
    }

    [Fact]
    public async Task ErrorsBecomeMessagesNotCrashes()
    {
        var services = new FakeServices();
        using var _s = services;
        var dialogs = new FakeDialogs();
        var state = new AppState(services, dialogs);
        var result = await state.RunAsync<string>("Prova", (_, _) => throw new InvalidOperationException("guasto simulato"));
        Assert.Null(result);
        Assert.StartsWith("Errore", state.Status);
        Assert.Contains(dialogs.Messages, m => m.Contains("guasto simulato"));
        Assert.False(state.IsBusy);
    }

    [Fact]
    public async Task GamesAndMaintenance()
    {
        var (main, services, dialogs) = await Started();
        using var _s = services;
        dialogs.Executable = @"C:\Giochi\cs2.exe";
        await main.Optimization.AddGameCommand.ExecuteAsync(null);
        Assert.Single(main.Optimization.Games);
        Assert.Contains(main.Optimization.AllItems, i => i.Id.StartsWith("games.cs2-exe", StringComparison.Ordinal));

        var cleanup = main.Optimization.Maintenance.Single();
        Assert.False(cleanup.RunCommand.CanExecute(null));
        await cleanup.PreviewCommand.ExecuteAsync(null);
        Assert.True(cleanup.RunCommand.CanExecute(null));
        await cleanup.RunCommand.ExecuteAsync(null);
        Assert.Equal("Eliminati 3 file", cleanup.Result);
    }

    [Fact]
    public void ChartScaling()
    {
        var points = ChartData.Scale([0, 50, 100], 200, 110);
        Assert.Equal(3, points.Count);
        Assert.Equal((0.0, 110.0), points[0]);
        Assert.Equal(200, points[2].X);
        Assert.Equal(10, points[2].Y, 6); // 100 su un massimo di 110 (margine del 10%)
        Assert.Empty(ChartData.Scale([], 100, 100));
        Assert.All(ChartData.Scale([double.NaN, 5], 10, 10), p => Assert.True(double.IsFinite(p.Y)));
    }
}
