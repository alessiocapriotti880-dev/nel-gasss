using PcOptimizer.Benchmark.Model;
using PcOptimizer.Tuning.Guides;

namespace PcOptimizer.Tuning.Stability;

/// <summary>Esito di un test di stabilità.</summary>
public sealed record StabilityReport
{
    public StabilityTestKind Kind { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>Unità di lavoro verificate (iterazioni, passaggi di memoria, dispatch della GPU).</summary>
    public long WorkUnits { get; init; }

    /// <summary>Risultati diversi da quelli attesi: con hardware stabile devono essere zero.</summary>
    public long Errors { get; init; }

    /// <summary>Errori hardware corretti/fatali registrati da Windows (WHEA) durante il test.</summary>
    public int WheaErrors { get; init; }

    /// <summary>Motivo dell'interruzione anticipata (temperatura, errore di sistema), o null.</summary>
    public string? AbortReason { get; init; }

    public bool Cancelled { get; init; }

    public IReadOnlyList<SensorSample> Sensors { get; init; } = [];

    public IReadOnlyList<string> Notes { get; init; } = [];

    public bool Passed => Outcome == StepOutcome.Stable;

    /// <summary>Senza unità di lavoro completate (test troppo breve o annullato) il valore resta da verificare.</summary>
    public StepOutcome Outcome => AbortReason is not null ? StepOutcome.Aborted
        : Errors > 0 || WheaErrors > 0 ? StepOutcome.Unstable
        : Cancelled || WorkUnits == 0 ? StepOutcome.Pending
        : StepOutcome.Stable;
}

/// <summary>Un carico di lavoro verificabile: ogni unità produce un risultato confrontato con quello atteso.</summary>
public interface IStressWorkload
{
    StabilityTestKind Kind { get; }

    /// <summary>Esegue il carico finché il token non viene annullato; restituisce unità eseguite ed errori.</summary>
    Task<(long WorkUnits, long Errors, IReadOnlyList<string> Notes)> RunAsync(CancellationToken cancellationToken);
}
