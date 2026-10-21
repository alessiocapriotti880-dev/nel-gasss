using PcOptimizer.Benchmark.Model;
using PcOptimizer.Core.Journal;
using PcOptimizer.Core.Safety;
using PcOptimizer.Core.Tweaks;

namespace PcOptimizer.Optimization.Session;

/// <summary>Esegue una misura completa nello scenario scelto per la sessione.</summary>
public interface IMeasurer
{
    Task<BenchmarkRun> MeasureAsync(string label, CancellationToken cancellationToken);
}

/// <summary>Applicazione e annullamento transazionale (il <see cref="TweakEngine"/> nell'uso reale).</summary>
public interface ITweakApplier
{
    ApplyResult Apply(ITweak tweak, TweakContext context);

    void Revert(Guid transactionId);

    RevertReport RevertAll();

    RevertReport RecoverPending();

    IReadOnlyList<JournalTransaction> Active { get; }
}

/// <summary>Ripresa automatica della sessione al prossimo accesso dopo il riavvio.</summary>
public interface IResumeScheduler
{
    void Schedule();

    void Cancel();
}

/// <summary>Aggiorna i dati del contesto che cambiano applicando gli interventi (es. il piano energetico attivo).</summary>
public interface IContextRefresher
{
    void Refresh(TweakContext context);
}

/// <summary>Tempo dall'avvio del sistema, per attendere che Windows finisca le attività di avvio prima di misurare.</summary>
public interface ISystemClock
{
    TimeSpan Uptime { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>Implementazione con il motore reale.</summary>
public sealed class EngineApplier(TweakEngine engine, ChangeJournal journal) : ITweakApplier
{
    public ApplyResult Apply(ITweak tweak, TweakContext context) => engine.Apply(tweak, context);

    public void Revert(Guid transactionId) => engine.Revert(transactionId);

    public RevertReport RevertAll() => engine.RevertAll();

    public RevertReport RecoverPending() => engine.RecoverPending();

    public IReadOnlyList<JournalTransaction> Active => journal.Active;
}

public sealed class SystemClock : ISystemClock
{
    public TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

public sealed record SessionOptions
{
    /// <summary>Misure di riferimento prima delle modifiche (almeno 2 per stimare la variabilità).</summary>
    public int BaselineRuns { get; init; } = 2;

    /// <summary>Misure dopo ogni intervento senza riavvio.</summary>
    public int RunsPerStep { get; init; } = 1;

    /// <summary>Misure dopo il riavvio.</summary>
    public int RunsAfterReboot { get; init; } = 2;

    /// <summary>Dopo il riavvio si misura solo quando il sistema è acceso da almeno questo tempo.</summary>
    public TimeSpan SettleAfterBoot { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>false = applica senza misurare (nessun annullamento automatico): scelta esplicita dell'utente.</summary>
    public bool Measure { get; init; } = true;
}
