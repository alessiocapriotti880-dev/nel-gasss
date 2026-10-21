using Microsoft.Extensions.Logging;
using PcOptimizer.Core.Backup;
using PcOptimizer.Core.Journal;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Tweaks;

/// <summary>Esito di un'applicazione.</summary>
public sealed record ApplyResult(
    string TweakId,
    TweakStatus StatusAfter,
    Guid? TransactionId,
    string? BackupFolder,
    IReadOnlyList<string> Warnings);

/// <summary>Esito di un rollback: le transazioni annullate e quelle che hanno dato errore.</summary>
public sealed record RevertReport(IReadOnlyList<Guid> Reverted, IReadOnlyList<(Guid TransactionId, string Error)> Failed)
{
    public bool Success => Failed.Count == 0;
}

public sealed class TweakApplyException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Applica e annulla gli interventi in modo transazionale.
/// <para>Sequenza di applicazione:</para>
/// <list type="number">
/// <item>cattura dei valori attuali;</item>
/// <item>scrittura nel giornale (con flush su disco) e backup .reg/JSON;</item>
/// <item>scrittura delle impostazioni; se una fallisce, quelle già scritte vengono riportate indietro;</item>
/// <item>verifica rileggendo i valori, poi conferma nel giornale.</item>
/// </list>
/// <para>
/// Più interventi possono toccare la stessa impostazione. Regole:
/// il valore "originale" salvato è sempre quello precedente a QUALSIASI intervento;
/// annullando un intervento, un'impostazione posseduta anche da un intervento più recente
/// non viene toccata, mentre se è posseduta da uno più vecchio torna al valore di quello.
/// Così l'ordine dei rollback non lascia mai il sistema in uno stato incoerente.
/// </para>
/// </summary>
public sealed class TweakEngine(SettingStore store, ChangeJournal journal, IBackupWriter backup, ILogger<TweakEngine> log)
{
    /// <summary>Rileva lo stato attuale dell'intervento senza modificare nulla.</summary>
    public TweakStatus Detect(ITweak tweak, TweakContext context)
    {
        if (!tweak.IsApplicable(context, out _))
        {
            return TweakStatus.NotApplicable;
        }

        var changes = Normalize(tweak.PlanChanges(context));
        if (changes.Count == 0)
        {
            return TweakStatus.NotApplicable;
        }

        var matching = changes.Count(c => SafeRead(c.Target) == c.Desired);
        return matching == changes.Count ? TweakStatus.Applied
            : matching == 0 ? TweakStatus.NotApplied
            : TweakStatus.PartiallyApplied;
    }

    public ApplyResult Apply(ITweak tweak, TweakContext context)
    {
        if (journal.Pending.Count > 0)
        {
            throw new InvalidOperationException(
                "Ci sono modifiche interrotte da una chiusura imprevista: ripristinale prima di applicarne altre.");
        }

        if (!tweak.IsApplicable(context, out var reason))
        {
            log.LogInformation("Intervento {Tweak} non applicabile: {Reason}", tweak.Id, reason);
            return new ApplyResult(tweak.Id, TweakStatus.NotApplicable, null, null, [reason ?? "Non applicabile."]);
        }

        // 1. Cattura: valore attuale (per l'annullamento immediato in caso di errore)
        //    e valore originale "vero" (per il giornale).
        var pending = new List<(CapturedSetting Captured, SettingValue Before)>();
        foreach (var change in Normalize(tweak.PlanChanges(context)))
        {
            var current = store.Read(change.Target);
            if (current == change.Desired)
            {
                continue; // già al valore desiderato: non serve toccarlo, né annullarlo poi.
            }

            var original = TrueOriginal(change.Target) ?? current;
            pending.Add((new CapturedSetting(change.Target, original, change.Desired), current));
        }

        if (pending.Count == 0)
        {
            log.LogInformation("Intervento {Tweak} già applicato, nessuna modifica.", tweak.Id);
            return new ApplyResult(tweak.Id, TweakStatus.Applied, null, null, []);
        }

        // 2. Giornale e backup PRIMA di modificare il sistema.
        var captured = pending.Select(p => p.Captured).ToList();
        var tx = journal.Begin(tweak.Id, tweak.Title, captured);
        string backupFolder;
        try
        {
            backupFolder = backup.Write(tx.Id, tweak.Id, captured);
        }
        catch (Exception ex)
        {
            journal.Abort(tx.Id, $"Backup non riuscito: {ex.Message}");
            throw new TweakApplyException($"Impossibile creare il backup per \"{tweak.Title}\": nessuna modifica applicata.", ex);
        }

        // 3. Scrittura, con annullamento di quanto già scritto in caso di errore.
        var written = new List<(CapturedSetting Captured, SettingValue Before)>();
        foreach (var item in pending)
        {
            try
            {
                log.LogInformation("[{Tweak}] {Target}: {Before} -> {Desired}",
                    tweak.Id, item.Captured.Target.Describe(), item.Before, item.Captured.Desired);
                store.Write(item.Captured.Target, item.Captured.Desired);
                written.Add(item);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "[{Tweak}] Scrittura fallita su {Target}, annullo le modifiche già fatte.",
                    tweak.Id, item.Captured.Target.Describe());
                var undoErrors = UndoWrites(written);
                journal.Abort(tx.Id, $"{item.Captured.Target.Describe()}: {ex.Message}" +
                    (undoErrors.Count > 0 ? $" | Annullamento incompleto: {string.Join("; ", undoErrors)}" : ""));
                throw new TweakApplyException(
                    $"\"{tweak.Title}\" non applicato: {ex.Message}" +
                    (undoErrors.Count > 0 ? " Alcune modifiche non sono state annullate: usa il backup .reg." : ""), ex);
            }
        }

        // 4. Verifica: alcuni valori possono essere riscritti da Windows o da criteri di gruppo.
        var warnings = new List<string>();
        foreach (var (c, _) in written)
        {
            var actual = SafeRead(c.Target);
            if (actual != c.Desired)
            {
                warnings.Add($"{c.Target.Describe()}: atteso {c.Desired}, letto {actual}.");
            }
        }

        journal.Commit(tx.Id);
        foreach (var w in warnings)
        {
            log.LogWarning("[{Tweak}] Verifica: {Warning}", tweak.Id, w);
        }

        return new ApplyResult(tweak.Id, Detect(tweak, context), tx.Id, backupFolder, warnings);
    }

    /// <summary>Annulla una singola transazione (rollback per intervento).</summary>
    public void Revert(Guid transactionId)
    {
        var tx = journal.Get(transactionId);
        if (tx.State != TransactionState.Active)
        {
            throw new InvalidOperationException($"La transazione \"{tx.Title}\" non è attiva (stato: {tx.State}).");
        }

        RestoreSettings(tx);
        journal.MarkReverted(tx.Id);
        log.LogInformation("Intervento {Tweak} annullato (transazione {Tx}).", tx.TweakId, tx.Id);
    }

    /// <summary>
    /// "Ripristina tutto": annulla le transazioni attive dalla più recente alla più vecchia.
    /// Continua anche se una fallisce e riporta l'elenco degli errori.
    /// </summary>
    public RevertReport RevertAll()
    {
        var reverted = new List<Guid>();
        var failed = new List<(Guid, string)>();
        foreach (var tx in journal.Active.OrderByDescending(t => t.Sequence))
        {
            try
            {
                Revert(tx.Id);
                reverted.Add(tx.Id);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Rollback di {Tweak} non riuscito.", tx.TweakId);
                failed.Add((tx.Id, ex.Message));
            }
        }

        return new RevertReport(reverted, failed);
    }

    /// <summary>
    /// Ripristina le transazioni rimaste "Pending" dopo una chiusura imprevista.
    /// Le impostazioni non ancora scritte hanno già il valore originale: riscriverlo è innocuo.
    /// </summary>
    public RevertReport RecoverPending()
    {
        var reverted = new List<Guid>();
        var failed = new List<(Guid, string)>();
        foreach (var tx in journal.Pending.OrderByDescending(t => t.Sequence))
        {
            try
            {
                RestoreSettings(tx);
                journal.MarkReverted(tx.Id, "Ripristinata dopo chiusura imprevista.");
                reverted.Add(tx.Id);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Recupero di {Tweak} non riuscito.", tx.TweakId);
                failed.Add((tx.Id, ex.Message));
            }
        }

        return new RevertReport(reverted, failed);
    }

    private void RestoreSettings(JournalTransaction tx)
    {
        var others = journal.Active.Where(t => t.Id != tx.Id).ToList();
        var errors = new List<string>();

        foreach (var setting in tx.Settings.Reverse())
        {
            var owners = others.Where(o => o.Settings.Any(s => s.Target == setting.Target)).ToList();

            // Un intervento più recente possiede ancora l'impostazione: non la tocchiamo.
            if (owners.Any(o => o.Sequence > tx.Sequence))
            {
                log.LogInformation("{Target} lasciato invariato: è usato da un intervento più recente.", setting.Target.Describe());
                continue;
            }

            // Altrimenti torna al valore dell'intervento precedente ancora attivo, o all'originale.
            var previousOwner = owners.Where(o => o.Sequence < tx.Sequence).MaxBy(o => o.Sequence);
            var restoreTo = previousOwner?.Settings.First(s => s.Target == setting.Target).Desired ?? setting.Original;

            try
            {
                log.LogInformation("[rollback {Tweak}] {Target} -> {Value}", tx.TweakId, setting.Target.Describe(), restoreTo);
                store.Write(setting.Target, restoreTo);
            }
            catch (Exception ex)
            {
                errors.Add($"{setting.Target.Describe()}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            // La transazione resta attiva: le scritture sono idempotenti, l'utente può riprovare.
            throw new InvalidOperationException($"Rollback incompleto di \"{tx.Title}\": {string.Join("; ", errors)}");
        }
    }

    private List<string> UndoWrites(List<(CapturedSetting Captured, SettingValue Before)> written)
    {
        var errors = new List<string>();
        foreach (var (c, before) in Enumerable.Reverse(written))
        {
            try
            {
                store.Write(c.Target, before);
            }
            catch (Exception ex)
            {
                errors.Add($"{c.Target.Describe()}: {ex.Message}");
            }
        }

        return errors;
    }

    /// <summary>Valore precedente a qualsiasi intervento, se l'impostazione è già posseduta da uno attivo.</summary>
    private SettingValue? TrueOriginal(SettingTarget target) =>
        journal.Active
            .OrderBy(t => t.Sequence)
            .SelectMany(t => t.Settings)
            .FirstOrDefault(s => s.Target == target)
            ?.Original;

    // Se un intervento elenca due volte la stessa impostazione vale l'ultima.
    private static List<SettingChange> Normalize(IReadOnlyList<SettingChange> changes) =>
        changes.GroupBy(c => c.Target).Select(g => g.Last()).ToList();

    private SettingValue? SafeRead(SettingTarget target)
    {
        try
        {
            return store.Read(target);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Lettura non riuscita: {Target}", target.Describe());
            return null;
        }
    }
}
