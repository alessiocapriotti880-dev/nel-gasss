using PcOptimizer.Core.Journal;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Tests;

public sealed class ChangeJournalTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pco-journal-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "journal.jsonl");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void State_survives_reload_with_all_setting_types()
    {
        var settings = new List<CapturedSetting>
        {
            new(new RegistryValueTarget(RegHive.CurrentUser, @"Software\X", "", RegValueKind.MultiString),
                SettingValue.OfMulti(["a", "b"]), SettingValue.Absent),
            new(new ServiceStartTarget("SysMain"), SettingValue.Of(ServiceStartValues.Automatic), SettingValue.Of(ServiceStartValues.Disabled)),
            new(new PowerSettingTarget(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PowerSource.Ac, "Core parking"),
                SettingValue.Of(10u), SettingValue.Of(100u)),
        };

        var journal = new ChangeJournal(FilePath);
        var committed = journal.Begin("t1", "Uno", settings);
        journal.Commit(committed.Id);
        var reverted = journal.Begin("t2", "Due", settings);
        journal.Commit(reverted.Id);
        journal.MarkReverted(reverted.Id);
        var pending = journal.Begin("t3", "Tre", settings);

        var reloaded = new ChangeJournal(FilePath);

        Assert.Equal(TransactionState.Active, reloaded.Get(committed.Id).State);
        Assert.Equal(TransactionState.Reverted, reloaded.Get(reverted.Id).State);
        Assert.Equal(TransactionState.Pending, reloaded.Get(pending.Id).State);
        Assert.Equal(settings, reloaded.Get(committed.Id).Settings);

        // I numeri di sequenza proseguono dopo il ricaricamento.
        var next = reloaded.Begin("t4", "Quattro", settings);
        Assert.True(next.Sequence > pending.Sequence);
    }

    [Fact]
    public void Truncated_last_line_is_ignored()
    {
        var journal = new ChangeJournal(FilePath);
        var tx = journal.Begin("t1", "Uno", []);
        File.AppendAllText(FilePath, "{\"Type\":\"Commit\",\"TxId\":\"" + tx.Id); // riga troncata da un crash

        var reloaded = new ChangeJournal(FilePath);

        Assert.Equal(TransactionState.Pending, reloaded.Get(tx.Id).State);
    }
}
