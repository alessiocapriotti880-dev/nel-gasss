using Microsoft.Extensions.Logging.Abstractions;
using PcOptimizer.Core.Backup;
using PcOptimizer.Core.Journal;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tests.Fakes;
using PcOptimizer.Core.Tweaks;

namespace PcOptimizer.Core.Tests;

public sealed class TweakEngineTests : IDisposable
{
    private static readonly RegistryValueTarget A = new(RegHive.LocalMachine, @"SOFTWARE\Test", "A", RegValueKind.DWord);
    private static readonly RegistryValueTarget B = new(RegHive.LocalMachine, @"SOFTWARE\Test", "B", RegValueKind.DWord);
    private static readonly ServiceStartTarget Svc = new("DiagTrack");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pco-tests-" + Guid.NewGuid().ToString("N"));
    private readonly InMemorySettingProvider _system = new();
    private readonly TweakContext _context = new(HardwareTier.Mid, 22631);
    private ChangeJournal _journal;
    private TweakEngine _engine;

    public TweakEngineTests()
    {
        _journal = new ChangeJournal(Path.Combine(_dir, "journal.jsonl"));
        _engine = CreateEngine();
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private TweakEngine CreateEngine() => new(
        new SettingStore([_system]), _journal, new BackupService(Path.Combine(_dir, "backups")),
        NullLogger<TweakEngine>.Instance);

    [Fact]
    public void Apply_then_revert_restores_original_values_including_absent_ones()
    {
        _system.Values[A] = SettingValue.Of(5u);
        _system.Values[Svc] = SettingValue.Of(ServiceStartValues.Automatic);
        var tweak = new FakeTweak("t1",
            new SettingChange(A, SettingValue.Of(1u)),
            new SettingChange(B, SettingValue.Of(0u)), // B non esiste prima
            new SettingChange(Svc, SettingValue.Of(ServiceStartValues.Disabled)));

        var result = _engine.Apply(tweak, _context);

        Assert.Equal(TweakStatus.Applied, result.StatusAfter);
        Assert.Equal(SettingValue.Of(1u), _system.Values[A]);
        Assert.Equal(TransactionState.Active, _journal.Get(result.TransactionId!.Value).State);

        _engine.Revert(result.TransactionId!.Value);

        Assert.Equal(SettingValue.Of(5u), _system.Values[A]);
        Assert.False(_system.Values.ContainsKey(B)); // tornato assente, non a 0
        Assert.Equal(SettingValue.Of(ServiceStartValues.Automatic), _system.Values[Svc]);
        Assert.Equal(TweakStatus.NotApplied, _engine.Detect(tweak, _context));
    }

    [Fact]
    public void Apply_creates_reg_backup_with_original_values()
    {
        _system.Values[A] = SettingValue.Of(5u);
        var result = _engine.Apply(new FakeTweak("t1", new SettingChange(A, SettingValue.Of(1u))), _context);

        var reg = File.ReadAllText(Path.Combine(result.BackupFolder!, "original.reg"));
        Assert.Contains("\"A\"=dword:00000005", reg);
        Assert.True(File.Exists(Path.Combine(result.BackupFolder!, "snapshot.json")));
    }

    [Fact]
    public void Failed_write_undoes_previous_writes_and_aborts_transaction()
    {
        _system.Values[A] = SettingValue.Of(5u);
        _system.FailOnWrite.Add(B);
        var tweak = new FakeTweak("t1",
            new SettingChange(A, SettingValue.Of(1u)),
            new SettingChange(B, SettingValue.Of(1u)));

        Assert.Throws<TweakApplyException>(() => _engine.Apply(tweak, _context));

        Assert.Equal(SettingValue.Of(5u), _system.Values[A]);
        Assert.Equal(TransactionState.Aborted, Assert.Single(_journal.Transactions).State);
        Assert.Empty(_journal.Active);
    }

    [Fact]
    public void Settings_already_at_desired_value_are_not_recorded()
    {
        _system.Values[A] = SettingValue.Of(1u);
        var result = _engine.Apply(new FakeTweak("t1", new SettingChange(A, SettingValue.Of(1u))), _context);

        Assert.Equal(TweakStatus.Applied, result.StatusAfter);
        Assert.Null(result.TransactionId);
        Assert.Empty(_journal.Transactions);
    }

    [Fact]
    public void Not_applicable_tweak_changes_nothing()
    {
        var tweak = new FakeTweak("t1", new SettingChange(A, SettingValue.Of(1u))) { Applicable = false };

        var result = _engine.Apply(tweak, _context);

        Assert.Equal(TweakStatus.NotApplicable, result.StatusAfter);
        Assert.Empty(_system.Writes);
    }

    [Fact]
    public void Reverting_older_tweak_keeps_value_owned_by_newer_one()
    {
        _system.Values[A] = SettingValue.Of(0u);
        var first = _engine.Apply(new FakeTweak("t1", new SettingChange(A, SettingValue.Of(1u))), _context);
        var second = _engine.Apply(new FakeTweak("t2", new SettingChange(A, SettingValue.Of(2u))), _context);

        _engine.Revert(first.TransactionId!.Value);
        Assert.Equal(SettingValue.Of(2u), _system.Values[A]); // t2 è ancora attivo

        _engine.Revert(second.TransactionId!.Value);
        Assert.Equal(SettingValue.Of(0u), _system.Values[A]); // originale vero, non il valore di t1
    }

    [Fact]
    public void Reverting_newer_tweak_restores_value_of_older_active_one()
    {
        _system.Values[A] = SettingValue.Of(0u);
        _engine.Apply(new FakeTweak("t1", new SettingChange(A, SettingValue.Of(1u))), _context);
        var second = _engine.Apply(new FakeTweak("t2", new SettingChange(A, SettingValue.Of(2u))), _context);

        _engine.Revert(second.TransactionId!.Value);

        Assert.Equal(SettingValue.Of(1u), _system.Values[A]);
    }

    [Fact]
    public void RevertAll_restores_every_original_value()
    {
        _system.Values[A] = SettingValue.Of(0u);
        _system.Values[Svc] = SettingValue.Of(ServiceStartValues.Manual);
        _engine.Apply(new FakeTweak("t1", new SettingChange(A, SettingValue.Of(1u))), _context);
        _engine.Apply(new FakeTweak("t2",
            new SettingChange(A, SettingValue.Of(2u)),
            new SettingChange(Svc, SettingValue.Of(ServiceStartValues.Disabled))), _context);

        var report = _engine.RevertAll();

        Assert.True(report.Success);
        Assert.Equal(2, report.Reverted.Count);
        Assert.Equal(SettingValue.Of(0u), _system.Values[A]);
        Assert.Equal(SettingValue.Of(ServiceStartValues.Manual), _system.Values[Svc]);
        Assert.Empty(_journal.Active);
    }

    [Fact]
    public void Failed_revert_keeps_transaction_active_so_it_can_be_retried()
    {
        _system.Values[A] = SettingValue.Of(0u);
        var applied = _engine.Apply(new FakeTweak("t1", new SettingChange(A, SettingValue.Of(1u))), _context);
        _system.FailOnWrite.Add(A);

        var report = _engine.RevertAll();

        Assert.False(report.Success);
        Assert.Equal(TransactionState.Active, _journal.Get(applied.TransactionId!.Value).State);

        _system.FailOnWrite.Clear();
        _engine.Revert(applied.TransactionId!.Value);
        Assert.Equal(SettingValue.Of(0u), _system.Values[A]);
    }

    [Fact]
    public void Pending_transaction_after_crash_is_recovered_on_restart()
    {
        // Simula un crash: giornale con "begin" scritto, valore già modificato, nessun "commit".
        _system.Values[A] = SettingValue.Of(5u);
        _journal.Begin("t1", "Test", [new CapturedSetting(A, SettingValue.Of(5u), SettingValue.Of(1u))]);
        _system.Values[A] = SettingValue.Of(1u);

        // "Riavvio" dell'app: il giornale viene riletto da disco.
        _journal = new ChangeJournal(Path.Combine(_dir, "journal.jsonl"));
        _engine = CreateEngine();
        Assert.Single(_journal.Pending);
        Assert.Throws<InvalidOperationException>(() =>
            _engine.Apply(new FakeTweak("t2", new SettingChange(B, SettingValue.Of(1u))), _context));

        var report = _engine.RecoverPending();

        Assert.True(report.Success);
        Assert.Equal(SettingValue.Of(5u), _system.Values[A]);
        Assert.Empty(_journal.Pending);
    }

    [Fact]
    public void Detect_reports_partial_application()
    {
        _system.Values[A] = SettingValue.Of(1u);
        var tweak = new FakeTweak("t1",
            new SettingChange(A, SettingValue.Of(1u)),
            new SettingChange(B, SettingValue.Of(1u)));

        Assert.Equal(TweakStatus.PartiallyApplied, _engine.Detect(tweak, _context));
    }
}
