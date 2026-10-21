using Microsoft.Extensions.Logging;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Storage;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Safety;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Optimization.Judging;
using PcOptimizer.Optimization.Planning;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Optimization.Session;

/// <summary>
/// Esegue una sessione di ottimizzazione:
/// <list type="number">
/// <item>recupera eventuali modifiche interrotte da una chiusura imprevista;</item>
/// <item>crea (o riusa, se Windows lo impone) un punto di ripristino: senza, non si modifica nulla;</item>
/// <item>misura il sistema prima delle modifiche;</item>
/// <item>applica gli interventi senza riavvio uno alla volta, misura e decide se tenerli;</item>
/// <item>applica insieme quelli con riavvio e programma la ripresa: dopo il riavvio misura e decide.</item>
/// </list>
/// Ogni passo salva lo stato: una chiusura imprevista non lascia modifiche "orfane" (restano nel giornale).
/// </summary>
public sealed class SessionRunner(
    ITweakApplier applier,
    IRestorePointService restorePoints,
    IMeasurer measurer,
    IResumeScheduler resume,
    IContextRefresher refresher,
    ISystemClock clock,
    SessionStore sessions,
    BenchmarkStore benchmarks,
    ILogger<SessionRunner> log)
{
    public const string RestorePointDescription = "PC Optimizer Pro - prima delle ottimizzazioni";

    public async Task<OptimizationSession> StartAsync(
        OptimizationPlan plan, TweakContext context, SessionOptions options, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!plan.IsValid)
        {
            throw new InvalidOperationException("Piano non valido: " + string.Join(" ", plan.Problems));
        }

        if (sessions.Current() is { Phase: SessionPhase.AwaitingReboot })
        {
            throw new InvalidOperationException("Una sessione attende il riavvio: riavvia il PC (o riprendila) prima di avviarne un'altra.");
        }

        var state = new State(new OptimizationSession
        {
            StartedAt = DateTimeOffset.Now,
            Phase = SessionPhase.Started,
            Measured = options.Measure,
            SelectedIds = plan.All.Select(t => t.Id).ToList(),
            ConfirmedSecurityIds = plan.All.Where(t => t.AffectsSecurity).Select(t => t.Id).ToList(),
        }, sessions, progress, log);

        var recovered = applier.RecoverPending();
        if (recovered.Reverted.Count > 0)
        {
            state.Event($"Ripristinate {recovered.Reverted.Count} modifiche rimaste a metà da una chiusura imprevista.");
        }

        state.Event("Creazione del punto di ripristino...");
        var point = restorePoints.EnsureRestorePoint(RestorePointDescription);
        state.Update(s => s with { RestorePoint = point });
        if (!point.IsUsable)
        {
            state.Event(point.Message);
            return state.Finish(SessionPhase.Aborted, "Nessun punto di ripristino disponibile: nessuna modifica applicata. " + point.Message);
        }

        state.Event(point.Message);

        IReadOnlyList<BenchmarkRun> reference = [];
        if (options.Measure)
        {
            reference = await MeasureAsync(state, "prima", options.BaselineRuns, cancellationToken);
            state.Update(s => s with { BaselineRunIds = Ids(reference), ReferenceRunIds = Ids(reference) });
        }

        foreach (var tweak in plan.Immediate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = ApplyOne(state, tweak, context, afterReboot: false);
            if (result is null)
            {
                continue;
            }

            if (!options.Measure)
            {
                state.Record(result with { Decision = Decision.Keep, Reason = JudgementReason.NotMeasured, Explanation = "Applicato senza misura, come richiesto." });
                continue;
            }

            if (!tweak.IsMeasurable)
            {
                var judgement = ImprovementJudge.Judge(tweak, reference, reference);
                state.Record(result with { Decision = judgement.Decision, Reason = judgement.Reason, Explanation = judgement.Explanation });
                continue;
            }

            var after = await MeasureAsync(state, $"dopo {tweak.Title}", options.RunsPerStep, cancellationToken);
            var verdict = ImprovementJudge.Judge(tweak, reference, after);
            state.Event($"{tweak.Title}: {verdict.Explanation}");
            if (verdict.Decision == Decision.Revert)
            {
                result = Revert(state, result);
                refresher.Refresh(context);
            }
            else
            {
                reference = after;
                state.Update(s => s with { ReferenceRunIds = Ids(after) });
            }

            state.Record(result with { Decision = verdict.Decision, Reason = verdict.Reason, Explanation = verdict.Explanation, Comparisons = verdict.Comparisons });
        }

        var pendingReboot = false;
        foreach (var tweak in plan.AfterReboot)
        {
            if (ApplyOne(state, tweak, context, afterReboot: true) is { } result)
            {
                state.Record(options.Measure
                    ? result
                    : result with { Decision = Decision.Keep, Reason = JudgementReason.NotMeasured, Explanation = "Applicato senza misura, come richiesto." });
                pendingReboot = true;
            }
        }

        if (pendingReboot)
        {
            if (options.Measure)
            {
                resume.Schedule();
                state.Event("Riavvia il PC: dopo l'accesso la sessione riprende da sola, misura e decide quali interventi tenere.");
                state.Update(s => s with { Phase = SessionPhase.AwaitingReboot });
                return state.Current;
            }

            state.Event("Riavvia il PC perché gli interventi applicati abbiano effetto.");
        }

        return state.Finish(SessionPhase.Completed, null, options.Measure ? Ids(reference) : []);
    }

    /// <summary>Dopo il riavvio: attende che il sistema si assesti, misura e decide sugli interventi con riavvio.</summary>
    public async Task<OptimizationSession> ResumeAsync(
        IReadOnlyList<CatalogTweak> catalog, SessionOptions options, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var current = sessions.Current();
        if (current is not { Phase: SessionPhase.AwaitingReboot })
        {
            throw new InvalidOperationException("Nessuna sessione in attesa di riavvio.");
        }

        resume.Cancel();
        var state = new State(current, sessions, progress, log);
        var wait = options.SettleAfterBoot - clock.Uptime;
        if (wait > TimeSpan.Zero)
        {
            state.Event($"Attendo {Math.Ceiling(wait.TotalMinutes)} min perché Windows completi le attività di avvio...");
            await clock.DelayAsync(wait, cancellationToken);
        }

        var reference = current.ReferenceRunIds.Select(benchmarks.Find).Where(r => r is not null).Select(r => r!).ToList();
        var after = await MeasureAsync(state, "dopo il riavvio", options.RunsAfterReboot, cancellationToken);

        var revertNeedsReboot = false;
        foreach (var result in current.Results.Where(r => r.AfterReboot && r.Applied && !r.Reverted && r.Decision is null))
        {
            var tweak = catalog.FirstOrDefault(t => t.Id == result.TweakId);
            var verdict = tweak is null
                ? new Judgement(Decision.Keep, JudgementReason.Insufficient, "Intervento non più presente nel catalogo: mantenuto.", [])
                : ImprovementJudge.Judge(tweak, reference, after);
            var updated = result with { Decision = verdict.Decision, Reason = verdict.Reason, Explanation = verdict.Explanation, Comparisons = verdict.Comparisons };
            state.Event($"{result.Title}: {verdict.Explanation}");
            if (verdict.Decision == Decision.Revert)
            {
                updated = Revert(state, updated);
                revertNeedsReboot |= updated.Reverted;
            }

            state.Record(updated);
        }

        state.Update(s => s with { RevertNeedsReboot = revertNeedsReboot });
        if (revertNeedsReboot)
        {
            state.Event("Alcuni interventi sono stati annullati: riavvia ancora una volta perché il ripristino abbia effetto.");
        }

        return state.Finish(SessionPhase.Completed, null, Ids(after));
    }

    /// <summary>Annulla un singolo intervento attivo (anche di sessioni precedenti).</summary>
    public bool RevertTweak(string tweakId)
    {
        var tx = applier.Active.Where(t => t.TweakId.Equals(tweakId, StringComparison.OrdinalIgnoreCase)).MaxBy(t => t.Sequence);
        if (tx is null)
        {
            return false;
        }

        applier.Revert(tx.Id);
        return true;
    }

    /// <summary>"Ripristina tutto": annulla ogni intervento attivo, dal più recente.</summary>
    public RevertReport RevertAll()
    {
        resume.Cancel();
        if (sessions.Current() is { Phase: SessionPhase.AwaitingReboot } pending)
        {
            sessions.Save(pending with { Phase = SessionPhase.Completed, EndedAt = DateTimeOffset.Now, Events = [.. pending.Events, "Sessione chiusa da \"Ripristina tutto\"."] });
        }

        return applier.RevertAll();
    }

    private SessionTweakResult? ApplyOne(State state, CatalogTweak tweak, TweakContext context, bool afterReboot)
    {
        state.Event($"Applico: {tweak.Title}");
        try
        {
            var applied = applier.Apply(tweak, context);
            refresher.Refresh(context);
            foreach (var warning in applied.Warnings)
            {
                state.Event($"  {warning}");
            }

            return new SessionTweakResult
            {
                TweakId = tweak.Id,
                Title = tweak.Title,
                AfterReboot = afterReboot,
                Applied = applied.TransactionId is not null || applied.StatusAfter == TweakStatus.Applied,
                TransactionId = applied.TransactionId,
            };
        }
        catch (Exception ex) when (ex is TweakApplyException or InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            log.LogWarning(ex, "Intervento {Tweak} non applicato", tweak.Id);
            state.Event($"  Non applicato: {ex.Message}");
            state.Record(new SessionTweakResult { TweakId = tweak.Id, Title = tweak.Title, AfterReboot = afterReboot, Error = ex.Message });
            return null;
        }
    }

    private SessionTweakResult Revert(State state, SessionTweakResult result)
    {
        if (result.TransactionId is not { } tx)
        {
            return result; // era già al valore desiderato: nulla da annullare
        }

        try
        {
            applier.Revert(tx);
            return result with { Reverted = true };
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            log.LogError(ex, "Annullamento di {Tweak} non riuscito", result.TweakId);
            state.Event($"  Annullamento non riuscito: {ex.Message}. Usa \"Ripristina tutto\" o il punto di ripristino.");
            return result with { Error = ex.Message };
        }
    }

    private async Task<IReadOnlyList<BenchmarkRun>> MeasureAsync(State state, string label, int runs, CancellationToken cancellationToken)
    {
        var result = new List<BenchmarkRun>();
        for (var i = 1; i <= Math.Max(1, runs); i++)
        {
            state.Event(runs > 1 ? $"Misura \"{label}\" ({i}/{runs})..." : $"Misura \"{label}\"...");
            var run = await measurer.MeasureAsync($"Sessione: {label}", cancellationToken);
            try
            {
                benchmarks.Save(run);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.LogWarning(ex, "Misura non salvata");
            }

            result.Add(run);
        }

        return result;
    }

    private static IReadOnlyList<Guid> Ids(IEnumerable<BenchmarkRun> runs) => runs.Select(r => r.Id).ToList();

    /// <summary>Stato della sessione con salvataggio a ogni cambiamento.</summary>
    private sealed class State(OptimizationSession initial, SessionStore store, IProgress<string>? progress, ILogger log)
    {
        public OptimizationSession Current { get; private set; } = initial;

        public void Update(Func<OptimizationSession, OptimizationSession> change)
        {
            Current = change(Current);
            store.Save(Current);
        }

        public void Event(string text)
        {
            log.LogInformation("[sessione] {Event}", text);
            progress?.Report(text);
            Update(s => s with { Events = [.. s.Events, $"{DateTimeOffset.Now:HH:mm:ss} {text}"] });
        }

        public void Record(SessionTweakResult result) =>
            Update(s => s with { Results = [.. s.Results.Where(r => r.TweakId != result.TweakId), result] });

        public OptimizationSession Finish(SessionPhase phase, string? abortReason, IReadOnlyList<Guid>? finalRuns = null)
        {
            Update(s => s with { Phase = phase, EndedAt = DateTimeOffset.Now, AbortReason = abortReason, FinalRunIds = finalRuns ?? s.FinalRunIds });
            return Current;
        }
    }
}
