using PcOptimizer.Benchmark.Comparison;
using PcOptimizer.Core.Safety;
using PcOptimizer.Optimization.Judging;

namespace PcOptimizer.Optimization.Session;

public enum SessionPhase
{
    /// <summary>Creata, nessuna modifica ancora.</summary>
    Started,
    /// <summary>Interventi senza riavvio applicati e giudicati; quelli con riavvio applicati: si attende il riavvio.</summary>
    AwaitingReboot,
    Completed,
    /// <summary>Interrotta prima di modificare il sistema (es. punto di ripristino non disponibile).</summary>
    Aborted,
}

/// <summary>Esito di un intervento nella sessione.</summary>
public sealed record SessionTweakResult
{
    public required string TweakId { get; init; }

    public required string Title { get; init; }

    public bool AfterReboot { get; init; }

    public bool Applied { get; init; }

    public Guid? TransactionId { get; init; }

    public Decision? Decision { get; init; }

    public JudgementReason? Reason { get; init; }

    public string? Explanation { get; init; }

    public IReadOnlyList<MetricComparison> Comparisons { get; init; } = [];

    public bool Reverted { get; init; }

    public string? Error { get; init; }
}

/// <summary>Stato persistente di una sessione: permette di riprendere dopo il riavvio o una chiusura imprevista.</summary>
public sealed record OptimizationSession
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; init; }

    public SessionPhase Phase { get; init; }

    public bool Measured { get; init; } = true;

    public IReadOnlyList<string> SelectedIds { get; init; } = [];

    public IReadOnlyList<string> ConfirmedSecurityIds { get; init; } = [];

    public RestorePointResult? RestorePoint { get; init; }

    /// <summary>Misure del sistema prima di qualsiasi modifica.</summary>
    public IReadOnlyList<Guid> BaselineRunIds { get; init; } = [];

    /// <summary>Misure dello stato attuale (dopo gli interventi tenuti finora): riferimento per i passi successivi.</summary>
    public IReadOnlyList<Guid> ReferenceRunIds { get; init; } = [];

    /// <summary>Misure finali, dopo il riavvio o dopo l'ultimo intervento.</summary>
    public IReadOnlyList<Guid> FinalRunIds { get; init; } = [];

    public IReadOnlyList<SessionTweakResult> Results { get; init; } = [];

    /// <summary>Alcuni annullamenti valgono solo dopo un altro riavvio.</summary>
    public bool RevertNeedsReboot { get; init; }

    /// <summary>Cronologia leggibile degli eventi della sessione.</summary>
    public IReadOnlyList<string> Events { get; init; } = [];

    public string? AbortReason { get; init; }
}
