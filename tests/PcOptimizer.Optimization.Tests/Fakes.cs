using Microsoft.Extensions.Logging.Abstractions;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Statistics;
using PcOptimizer.Benchmark.Storage;
using PcOptimizer.Core;
using PcOptimizer.Core.Backup;
using PcOptimizer.Core.Journal;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Safety;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Optimization.Session;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Optimization.Tests;

internal sealed class MemoryProvider : ISettingProvider
{
    public Dictionary<SettingTarget, SettingValue> Values { get; } = [];

    public bool CanHandle(SettingTarget target) => true;

    public SettingValue Read(SettingTarget target) => Values.TryGetValue(target, out var v) ? v : SettingValue.Absent;

    public void Write(SettingTarget target, SettingValue value)
    {
        if (value.Exists)
        {
            Values[target] = value;
        }
        else
        {
            Values.Remove(target);
        }
    }
}

/// <summary>Misure simulate: il DPC massimo dipende dalle impostazioni attualmente scritte nel sistema finto.</summary>
internal sealed class FakeMeasurer(Func<double> dpcCenter) : IMeasurer
{
    private int _seed;

    public int Calls { get; private set; }

    public Task<BenchmarkRun> MeasureAsync(string label, CancellationToken cancellationToken)
    {
        Calls++;
        var random = new Random(++_seed);
        var center = dpcCenter();
        var dpc = Enumerable.Range(0, 60).Select(_ => center * (1 + (random.NextDouble() - 0.5) * 0.1)).ToArray();
        var timer = Enumerable.Range(0, 60).Select(_ => 300 * (1 + (random.NextDouble() - 0.5) * 0.1)).ToArray();
        return Task.FromResult(new BenchmarkRun
        {
            StartedAt = DateTimeOffset.Now,
            Label = label,
            Environment = new BenchmarkEnvironment { LogicalProcessors = 8, UptimeMinutes = 60 },
            DpcIsr = new DpcIsrResult
            {
                WindowMaxDpcUs = dpc,
                WindowMaxIsrUs = dpc.Select(v => v / 10).ToArray(),
                WindowLoadPercent = dpc.Select(_ => 0.05).ToArray(),
            },
            Timer = new TimerLatencyResult { Samples = 60_000, WindowP99Us = timer, WindowMeanUs = timer.Select(v => v / 3).ToArray(), LatenessUs = Descriptive.Summarize(timer) },
        });
    }
}

internal sealed class FakeRestorePoints(RestorePointOutcome outcome) : IRestorePointService
{
    public int Calls { get; private set; }

    public RestorePointResult EnsureRestorePoint(string description)
    {
        Calls++;
        return new RestorePointResult(outcome, outcome == RestorePointOutcome.Disabled ? null : DateTime.Now, $"esito {outcome}");
    }
}

internal sealed class FakeResume : IResumeScheduler
{
    public bool Scheduled { get; private set; }

    public void Schedule() => Scheduled = true;

    public void Cancel() => Scheduled = false;
}

internal sealed class NoRefresh : IContextRefresher
{
    public void Refresh(TweakContext context)
    {
    }
}

internal sealed class FakeClock : ISystemClock
{
    public TimeSpan Uptime { get; set; } = TimeSpan.FromMinutes(30);

    public List<TimeSpan> Delays { get; } = [];

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Delays.Add(delay);
        return Task.CompletedTask;
    }
}

/// <summary>Ambiente completo di una sessione su un sistema finto, in una cartella temporanea.</summary>
internal sealed class Harness : IDisposable
{
    public Harness(RestorePointOutcome restorePoint = RestorePointOutcome.Created, Func<MemoryProvider, double>? dpc = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "pcopt-session-" + Guid.NewGuid().ToString("N"));
        Paths = new AppPaths(Root);
        Journal = new ChangeJournal(Paths.JournalFile);
        var engine = new TweakEngine(new SettingStore([Provider]), Journal, new BackupService(Paths.Backups), NullLogger<TweakEngine>.Instance);
        Applier = new EngineApplier(engine, Journal);
        RestorePoints = new FakeRestorePoints(restorePoint);
        Measurer = new FakeMeasurer(() => (dpc ?? (_ => 400))(Provider));
        Sessions = new SessionStore(Paths);
        Benchmarks = new BenchmarkStore(Paths);
        Runner = new SessionRunner(Applier, RestorePoints, Measurer, Resume, new NoRefresh(), Clock, Sessions, Benchmarks, NullLogger<SessionRunner>.Instance);
    }

    public string Root { get; }

    public AppPaths Paths { get; }

    public MemoryProvider Provider { get; } = new();

    public ChangeJournal Journal { get; }

    public EngineApplier Applier { get; }

    public FakeRestorePoints RestorePoints { get; }

    public FakeMeasurer Measurer { get; }

    public FakeResume Resume { get; } = new();

    public FakeClock Clock { get; } = new();

    public SessionStore Sessions { get; }

    public BenchmarkStore Benchmarks { get; }

    public SessionRunner Runner { get; }

    public TweakContext Context { get; } = new(HardwareTier.Mid, 22631);

    public void Dispose() => Directory.Delete(Root, recursive: true);

    public static RegistryValueTarget Target(string name) => new(RegHive.LocalMachine, @"SOFTWARE\Prova", name, RegValueKind.DWord);

    public static CatalogTweak Tweak(
        string id, TweakEvidence evidence = TweakEvidence.Debatable, bool reboot = false, IReadOnlyList<string>? measuredBy = null,
        bool security = false, uint value = 1, string? target = null, Func<TweakContext, string?>? notApplicable = null) => new()
    {
        Id = id,
        Title = $"Intervento {id}",
        Category = TweakCategory.Latency,
        Group = TweakGroup.Latency,
        WhatChanges = "w",
        Why = "y",
        ExpectedBenefit = "b",
        Evidence = evidence,
        RequiresReboot = reboot,
        MeasuredBy = measuredBy ?? Measures.Latency,
        AffectsSecurity = security,
        SecurityWarning = security ? "attenzione" : null,
        NotApplicableReason = notApplicable ?? (_ => null),
        Plan = _ => [new(Target(target ?? id), SettingValue.Of(value))],
    };
}
