using System.Text.Json;
using System.Text.Json.Serialization;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Journal;

/// <summary>Stato di una transazione nel giornale.</summary>
public enum TransactionState
{
    /// <summary>Iniziata ma non confermata: l'app si è chiusa/bloccata durante l'applicazione.</summary>
    Pending,
    /// <summary>Applicata con successo e attualmente in vigore.</summary>
    Active,
    /// <summary>Fallita durante l'applicazione e già annullata.</summary>
    Aborted,
    /// <summary>Annullata dall'utente o dal rollback automatico.</summary>
    Reverted,
}

/// <summary>Una transazione = l'applicazione di un intervento con tutte le sue impostazioni.</summary>
public sealed class JournalTransaction
{
    public required Guid Id { get; init; }
    public required string TweakId { get; init; }
    public required string Title { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    /// <summary>Numero progressivo: ordina le transazioni anche con orologio di sistema che cambia.</summary>
    public required long Sequence { get; init; }
    public required IReadOnlyList<CapturedSetting> Settings { get; init; }
    public TransactionState State { get; internal set; }
    public DateTimeOffset? EndedAt { get; internal set; }
    public string? Note { get; internal set; }
}

/// <summary>
/// Giornale delle modifiche: file JSON Lines in sola aggiunta.
/// <para>
/// Ogni riga è un evento (begin/commit/abort/revert). Lo stato si ricostruisce rileggendo
/// gli eventi, quindi un crash a metà scrittura lascia al massimo una transazione "Pending",
/// che all'avvio successivo viene proposta per il ripristino. Ogni riga viene scritta
/// su disco con flush esplicito prima di toccare il sistema.
/// </para>
/// </summary>
public sealed class ChangeJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly Dictionary<Guid, JournalTransaction> _transactions = [];
    private long _nextSequence;

    public ChangeJournal(string filePath)
    {
        _filePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        Load();
    }

    /// <summary>Tutte le transazioni, dalla più vecchia alla più recente.</summary>
    public IReadOnlyList<JournalTransaction> Transactions
    {
        get
        {
            lock (_lock)
            {
                return _transactions.Values.OrderBy(t => t.Sequence).ToList();
            }
        }
    }

    public IReadOnlyList<JournalTransaction> Active => Transactions.Where(t => t.State == TransactionState.Active).ToList();

    public IReadOnlyList<JournalTransaction> Pending => Transactions.Where(t => t.State == TransactionState.Pending).ToList();

    public JournalTransaction Get(Guid id)
    {
        lock (_lock)
        {
            return _transactions.TryGetValue(id, out var tx)
                ? tx
                : throw new KeyNotFoundException($"Transazione {id} non trovata nel giornale.");
        }
    }

    /// <summary>Registra l'inizio di un'applicazione. Va chiamato PRIMA di scrivere sul sistema.</summary>
    public JournalTransaction Begin(string tweakId, string title, IReadOnlyList<CapturedSetting> settings)
    {
        lock (_lock)
        {
            var tx = new JournalTransaction
            {
                Id = Guid.NewGuid(),
                TweakId = tweakId,
                Title = title,
                StartedAt = DateTimeOffset.Now,
                Sequence = _nextSequence++,
                Settings = settings,
                State = TransactionState.Pending,
            };
            Append(new JournalRecord(RecordType.Begin, tx.Id, tx.StartedAt)
            {
                Sequence = tx.Sequence,
                TweakId = tweakId,
                Title = title,
                Settings = settings.ToList(),
            });
            _transactions[tx.Id] = tx;
            return tx;
        }
    }

    public void Commit(Guid id) => Transition(id, RecordType.Commit, TransactionState.Active, null);

    public void Abort(Guid id, string reason) => Transition(id, RecordType.Abort, TransactionState.Aborted, reason);

    public void MarkReverted(Guid id, string? note = null) => Transition(id, RecordType.Revert, TransactionState.Reverted, note);

    private void Transition(Guid id, RecordType type, TransactionState newState, string? note)
    {
        lock (_lock)
        {
            var tx = Get(id);
            var now = DateTimeOffset.Now;
            Append(new JournalRecord(type, id, now) { Note = note });
            tx.State = newState;
            tx.EndedAt = now;
            tx.Note = note;
        }
    }

    private void Append(JournalRecord record)
    {
        var line = JsonSerializer.Serialize(record, JsonOptions) + "\n";
        using var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        var bytes = System.Text.Encoding.UTF8.GetBytes(line);
        stream.Write(bytes);
        // Flush fino al disco: il record deve esistere prima che il sistema venga modificato.
        stream.Flush(flushToDisk: true);
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        foreach (var line in File.ReadLines(_filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JournalRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<JournalRecord>(line, JsonOptions);
            }
            catch (JsonException)
            {
                // Riga troncata da un crash durante la scrittura: l'evento non è mai stato
                // confermato, quindi lo si ignora (la transazione resta Pending o non esiste).
                continue;
            }

            if (record is not null)
            {
                Replay(record);
            }
        }
    }

    private void Replay(JournalRecord record)
    {
        if (record.Type == RecordType.Begin)
        {
            _transactions[record.TxId] = new JournalTransaction
            {
                Id = record.TxId,
                TweakId = record.TweakId ?? "",
                Title = record.Title ?? "",
                StartedAt = record.At,
                Sequence = record.Sequence,
                Settings = record.Settings ?? [],
                State = TransactionState.Pending,
            };
            _nextSequence = Math.Max(_nextSequence, record.Sequence + 1);
            return;
        }

        if (!_transactions.TryGetValue(record.TxId, out var tx))
        {
            return;
        }

        tx.State = record.Type switch
        {
            RecordType.Commit => TransactionState.Active,
            RecordType.Abort => TransactionState.Aborted,
            RecordType.Revert => TransactionState.Reverted,
            _ => tx.State,
        };
        tx.EndedAt = record.At;
        tx.Note = record.Note;
    }

    private enum RecordType
    {
        Begin,
        Commit,
        Abort,
        Revert,
    }

    private sealed record JournalRecord(RecordType Type, Guid TxId, DateTimeOffset At)
    {
        public long Sequence { get; init; }
        public string? TweakId { get; init; }
        public string? Title { get; init; }
        public List<CapturedSetting>? Settings { get; init; }
        public string? Note { get; init; }
    }
}
