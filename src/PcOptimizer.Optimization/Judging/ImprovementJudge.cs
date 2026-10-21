using System.Globalization;
using PcOptimizer.Benchmark.Comparison;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Optimization.Judging;

public enum Decision
{
    Keep,
    Revert,
}

public enum JudgementReason
{
    /// <summary>Nessuna grandezza misurabile (privacy, frequenza del monitor...): si tiene per scelta dell'utente.</summary>
    NotMeasurable,
    /// <summary>Almeno una grandezza è migliorata e nessuna è peggiorata.</summary>
    Improved,
    /// <summary>Almeno una grandezza è peggiorata.</summary>
    Worse,
    /// <summary>Nessuna differenza distinguibile dal rumore.</summary>
    Unchanged,
    /// <summary>Le grandezze necessarie non sono state misurate (es. DPC senza amministratore, frame senza gioco).</summary>
    Insufficient,
    /// <summary>L'utente ha scelto di applicare senza misurare.</summary>
    NotMeasured,
}

public sealed record Judgement(Decision Decision, JudgementReason Reason, string Explanation, IReadOnlyList<MetricComparison> Comparisons);

/// <summary>
/// Decide se tenere un intervento confrontando le misure prima e dopo.
/// <list type="bullet">
/// <item>Peggioramento significativo di una grandezza: si annulla, sempre.</item>
/// <item>Miglioramento significativo senza peggioramenti: si tiene.</item>
/// <item>Nessuna differenza o dati insufficienti: si tiene solo se il comportamento è documentato (o è un ripristino
/// o una scelta di privacy); gli interventi "dipende dal sistema" o "da verificare" si annullano, perché il loro
/// beneficio va dimostrato.</item>
/// </list>
/// </summary>
public static class ImprovementJudge
{
    private static readonly TweakEvidence[] KeptWithoutProof = [TweakEvidence.Documented, TweakEvidence.RestoreDefault, TweakEvidence.Privacy];

    public static Judgement Judge(CatalogTweak tweak, IReadOnlyList<BenchmarkRun> before, IReadOnlyList<BenchmarkRun> after)
    {
        if (!tweak.IsMeasurable)
        {
            return new Judgement(Decision.Keep, JudgementReason.NotMeasurable,
                "Effetto non misurabile con le prove disponibili: mantenuto come scelto.", []);
        }

        var comparisons = BenchmarkComparer.Compare(before, after, tweak.MeasuredBy);
        var measured = comparisons.Where(c => c.Verdict != ComparisonVerdict.Insufficient).ToList();
        var worse = measured.Where(c => c.Verdict == ComparisonVerdict.Worse).ToList();
        var improved = measured.Where(c => c.Verdict == ComparisonVerdict.Improved).ToList();
        var provenEnough = KeptWithoutProof.Contains(tweak.Evidence);

        if (worse.Count > 0)
        {
            return new Judgement(Decision.Revert, JudgementReason.Worse, $"Peggiorato: {Describe(worse)}. Annullato.", comparisons);
        }

        if (improved.Count > 0)
        {
            return new Judgement(Decision.Keep, JudgementReason.Improved, $"Migliorato: {Describe(improved)}.", comparisons);
        }

        if (measured.Count == 0)
        {
            return provenEnough
                ? new Judgement(Decision.Keep, JudgementReason.Insufficient,
                    "Grandezze non misurabili in questa sessione: mantenuto perché il comportamento è documentato.", comparisons)
                : new Judgement(Decision.Revert, JudgementReason.Insufficient,
                    "Grandezze non misurabili in questa sessione: annullato perché il beneficio va dimostrato con una misura.", comparisons);
        }

        return provenEnough
            ? new Judgement(Decision.Keep, JudgementReason.Unchanged,
                "Nessuna differenza misurabile; mantenuto perché il comportamento è documentato e non peggiora nulla.", comparisons)
            : new Judgement(Decision.Revert, JudgementReason.Unchanged,
                "Nessuna differenza misurabile rispetto alla variabilità delle misure: annullato.", comparisons);
    }

    private static string Describe(IEnumerable<MetricComparison> comparisons) =>
        string.Join("; ", comparisons.Select(c =>
            $"{c.Title.ToLowerInvariant()} {Num(c.BaselineMedian)} → {Num(c.AfterMedian)} {c.Unit} ({(c.RelativeChange >= 0 ? "+" : "")}{Num(c.RelativeChange * 100)}%)"));

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');
}
