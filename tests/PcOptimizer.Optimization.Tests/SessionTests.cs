using PcOptimizer.Core.Safety;
using PcOptimizer.Optimization.Judging;
using PcOptimizer.Optimization.Planning;
using PcOptimizer.Optimization.Session;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Optimization.Tests;

public class PlanTests
{
    [Fact]
    public void ValidatesSelection()
    {
        var context = new Harness().Context;
        CatalogTweak[] catalog =
        [
            Harness.Tweak("a"),
            Harness.Tweak("b", reboot: true),
            Harness.Tweak("na", notApplicable: _ => "non serve"),
            Harness.Tweak("sec", security: true),
            Harness.Tweak("c1", target: "comune", value: 1),
            Harness.Tweak("c2", target: "comune", value: 2),
        ];

        var ok = PlanBuilder.Build(catalog, ["b", "a"], context);
        Assert.True(ok.IsValid);
        Assert.Equal(["a"], ok.Immediate.Select(t => t.Id));
        Assert.Equal(["b"], ok.AfterReboot.Select(t => t.Id));
        Assert.True(ok.RequiresReboot);

        var bad = PlanBuilder.Build(catalog, ["x", "na", "sec", "c1", "c2"], context);
        Assert.False(bad.IsValid);
        Assert.Contains(bad.Problems, p => p.Contains("sconosciuto"));
        Assert.Contains(bad.Problems, p => p.Contains("non serve"));
        Assert.Contains(bad.Problems, p => p.Contains("conferma esplicita"));
        Assert.Contains(bad.Problems, p => p.Contains("scegline uno"));

        var confirmed = PlanBuilder.Build(catalog, ["sec"], context, confirmedSecurity: ["sec"]);
        Assert.True(confirmed.IsValid);
        Assert.False(PlanBuilder.Build(catalog, [], context).IsValid);
    }
}

public class JudgeTests
{
    private static async Task<IReadOnlyList<PcOptimizer.Benchmark.Model.BenchmarkRun>> Runs(double dpc, int count = 2)
    {
        var measurer = new FakeMeasurer(() => dpc);
        var list = new List<PcOptimizer.Benchmark.Model.BenchmarkRun>();
        for (var i = 0; i < count; i++)
        {
            list.Add(await measurer.MeasureAsync("x", CancellationToken.None));
        }

        return list;
    }

    [Fact]
    public async Task Decisions()
    {
        var before = await Runs(400);
        var better = await Runs(200, 1);
        var worse = await Runs(800, 1);
        var same = await Runs(400, 1);

        Assert.Equal((Decision.Keep, JudgementReason.Improved), Verdict(Harness.Tweak("a"), before, better));
        Assert.Equal((Decision.Revert, JudgementReason.Worse), Verdict(Harness.Tweak("a", TweakEvidence.Documented), before, worse));
        Assert.Equal((Decision.Revert, JudgementReason.Unchanged), Verdict(Harness.Tweak("a", TweakEvidence.Debatable), before, same));
        Assert.Equal((Decision.Revert, JudgementReason.Unchanged), Verdict(Harness.Tweak("a", TweakEvidence.Situational), before, same));
        Assert.Equal((Decision.Keep, JudgementReason.Unchanged), Verdict(Harness.Tweak("a", TweakEvidence.Documented), before, same));
        Assert.Equal((Decision.Keep, JudgementReason.NotMeasurable), Verdict(Harness.Tweak("a", measuredBy: Measures.None), before, same));

        var frames = Harness.Tweak("f", TweakEvidence.Debatable, measuredBy: Measures.Frames);
        Assert.Equal((Decision.Revert, JudgementReason.Insufficient), Verdict(frames, before, same));
        Assert.Equal((Decision.Keep, JudgementReason.Insufficient), Verdict(Harness.Tweak("g", TweakEvidence.RestoreDefault, measuredBy: Measures.Frames), before, same));

        var improved = ImprovementJudge.Judge(Harness.Tweak("a"), before, better);
        Assert.Contains("dpc più lungo al secondo", improved.Explanation);
        Assert.Contains("%", improved.Explanation);
    }

    private static (Decision, JudgementReason) Verdict(CatalogTweak t, IReadOnlyList<PcOptimizer.Benchmark.Model.BenchmarkRun> a, IReadOnlyList<PcOptimizer.Benchmark.Model.BenchmarkRun> b)
    {
        var j = ImprovementJudge.Judge(t, a, b);
        return (j.Decision, j.Reason);
    }
}

public class SessionTests
{
    [Fact]
    public async Task WithoutRestorePointNothingChanges()
    {
        using var h = new Harness(RestorePointOutcome.Disabled);
        var plan = PlanBuilder.Build([Harness.Tweak("a")], ["a"], h.Context);

        var session = await h.Runner.StartAsync(plan, h.Context, new SessionOptions());

        Assert.Equal(SessionPhase.Aborted, session.Phase);
        Assert.Contains("punto di ripristino", session.AbortReason);
        Assert.Empty(h.Provider.Values);
        Assert.Equal(0, h.Measurer.Calls);
        Assert.Equal(SessionPhase.Aborted, h.Sessions.Current()!.Phase);
    }

    [Fact]
    public async Task KeepsWhatImprovesAndRevertsTheRest()
    {
        // Il DPC scende solo con l'intervento "utile" attivo.
        using var h = new Harness(dpc: p => p.Values.ContainsKey(Harness.Target("utile")) ? 200 : 400);
        CatalogTweak[] catalog =
        [
            Harness.Tweak("utile"),
            Harness.Tweak("inutile"),
            Harness.Tweak("privacy", TweakEvidence.Privacy, measuredBy: Measures.None),
        ];
        var plan = PlanBuilder.Build(catalog, ["utile", "inutile", "privacy"], h.Context);

        var session = await h.Runner.StartAsync(plan, h.Context, new SessionOptions());

        Assert.Equal(SessionPhase.Completed, session.Phase);
        Assert.Equal(RestorePointOutcome.Created, session.RestorePoint!.Outcome);
        Assert.Equal(2, session.BaselineRunIds.Count);
        Assert.Equal(4, h.Measurer.Calls); // 2 di riferimento + 1 per ciascun intervento misurabile

        var utile = session.Results.Single(r => r.TweakId == "utile");
        Assert.Equal(Decision.Keep, utile.Decision);
        Assert.Equal(JudgementReason.Improved, utile.Reason);
        Assert.NotEmpty(utile.Comparisons);

        var inutile = session.Results.Single(r => r.TweakId == "inutile");
        Assert.Equal(Decision.Revert, inutile.Decision);
        Assert.True(inutile.Reverted);

        Assert.Equal(JudgementReason.NotMeasurable, session.Results.Single(r => r.TweakId == "privacy").Reason);
        Assert.True(h.Provider.Values.ContainsKey(Harness.Target("utile")));
        Assert.False(h.Provider.Values.ContainsKey(Harness.Target("inutile")));
        Assert.True(h.Provider.Values.ContainsKey(Harness.Target("privacy")));
        Assert.Equal(2, h.Journal.Active.Count);
        Assert.Single(h.Sessions.History());
        Assert.Equal(4, h.Benchmarks.List().Count);
    }

    [Fact]
    public async Task RebootTweaksAreJudgedAfterTheReboot()
    {
        using var h = new Harness(dpc: p => p.Values.ContainsKey(Harness.Target("dopo-riavvio")) ? 900 : 400);
        CatalogTweak[] catalog = [Harness.Tweak("dopo-riavvio", reboot: true), Harness.Tweak("visivo", TweakEvidence.Situational, reboot: true, measuredBy: Measures.None)];
        var plan = PlanBuilder.Build(catalog, ["dopo-riavvio", "visivo"], h.Context);

        var started = await h.Runner.StartAsync(plan, h.Context, new SessionOptions());

        Assert.Equal(SessionPhase.AwaitingReboot, started.Phase);
        Assert.True(h.Resume.Scheduled);
        Assert.All(started.Results, r => Assert.Null(r.Decision));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Runner.StartAsync(plan, h.Context, new SessionOptions()));

        h.Clock.Uptime = TimeSpan.FromMinutes(1);
        var resumed = await h.Runner.ResumeAsync(catalog, new SessionOptions());

        Assert.Equal(SessionPhase.Completed, resumed.Phase);
        Assert.False(h.Resume.Scheduled);
        Assert.Equal(TimeSpan.FromMinutes(2), Assert.Single(h.Clock.Delays));
        var worse = resumed.Results.Single(r => r.TweakId == "dopo-riavvio");
        Assert.Equal(Decision.Revert, worse.Decision);
        Assert.Equal(JudgementReason.Worse, worse.Reason);
        Assert.True(worse.Reverted);
        Assert.True(resumed.RevertNeedsReboot);
        Assert.Equal(Decision.Keep, resumed.Results.Single(r => r.TweakId == "visivo").Decision);
        Assert.False(h.Provider.Values.ContainsKey(Harness.Target("dopo-riavvio")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Runner.ResumeAsync(catalog, new SessionOptions()));
    }

    [Fact]
    public async Task ApplyWithoutMeasuringKeepsEverything()
    {
        using var h = new Harness();
        CatalogTweak[] catalog = [Harness.Tweak("a"), Harness.Tweak("b", reboot: true)];
        var session = await h.Runner.StartAsync(PlanBuilder.Build(catalog, ["a", "b"], h.Context), h.Context, new SessionOptions { Measure = false });

        Assert.Equal(SessionPhase.Completed, session.Phase);
        Assert.Equal(0, h.Measurer.Calls);
        Assert.False(h.Resume.Scheduled);
        Assert.All(session.Results, r => Assert.Equal(JudgementReason.NotMeasured, r.Reason));
        Assert.Equal(2, h.Provider.Values.Count);
        Assert.Contains(session.Events, e => e.Contains("Riavvia"));
    }

    [Fact]
    public async Task RollbackSingleAndAll()
    {
        using var h = new Harness();
        CatalogTweak[] catalog = [Harness.Tweak("a", TweakEvidence.Documented), Harness.Tweak("b", TweakEvidence.Documented)];
        await h.Runner.StartAsync(PlanBuilder.Build(catalog, ["a", "b"], h.Context), h.Context, new SessionOptions { Measure = false });

        Assert.True(h.Runner.RevertTweak("a"));
        Assert.False(h.Runner.RevertTweak("a"));
        Assert.Single(h.Provider.Values);

        var report = h.Runner.RevertAll();
        Assert.True(report.Success);
        Assert.Empty(h.Provider.Values);
        Assert.Empty(h.Journal.Active);
    }

    [Fact]
    public async Task ApplyErrorsAreRecordedAndTheSessionContinues()
    {
        using var h = new Harness();
        var failing = new CatalogTweak
        {
            Id = "rotto",
            Title = "Rotto",
            Category = PcOptimizer.Core.Model.TweakCategory.System,
            Group = TweakGroup.Services,
            WhatChanges = "w",
            Why = "y",
            ExpectedBenefit = "b",
            Plan = _ => throw new InvalidOperationException("scrittura negata"),
        };
        var plan = new OptimizationPlan([failing, Harness.Tweak("ok", TweakEvidence.Documented)], [], []);

        var session = await h.Runner.StartAsync(plan, h.Context, new SessionOptions { Measure = false });

        Assert.Equal("scrittura negata", session.Results.Single(r => r.TweakId == "rotto").Error);
        Assert.Equal(Decision.Keep, session.Results.Single(r => r.TweakId == "ok").Decision);
    }
}
