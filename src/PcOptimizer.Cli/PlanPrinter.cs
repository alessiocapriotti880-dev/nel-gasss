using PcOptimizer.Core.Model;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Hardware.Assessment;
using PcOptimizer.Tweaks;
using PcOptimizer.Tweaks.Guidance;
using PcOptimizer.Tweaks.Model;
using static PcOptimizer.Cli.ConsoleOutput;

namespace PcOptimizer.Cli;

/// <summary>Stampa del piano di ottimizzazione: interventi per sezione con stato, rischio ed evidenza.</summary>
internal static class PlanPrinter
{
    public static void Print(Inspection inspection, Func<CatalogTweak, TweakStatus> status, bool all, IReadOnlyList<GuidanceItem> guidance)
    {
        var context = inspection.Context;
        var recommended = TweakCatalog.Recommended(inspection.Tweaks, context).Select(t => t.Id).ToHashSet();
        Heading($"Piano di ottimizzazione - fascia {TierClassifier.TierName(context.Tier)}");
        Console.WriteLine($"  {TierProfiles.Describe(context.Tier)}");
        Console.WriteLine("  [x] = consigliato per questo PC. Nessuna modifica viene applicata da questo comando.");

        foreach (var group in inspection.Tweaks.GroupBy(t => t.Group).OrderBy(g => g.Key))
        {
            var rows = group.Select(t => (Tweak: t, Status: status(t)))
                .Where(x => all || x.Status != TweakStatus.NotApplicable || x.Tweak.IsSatisfied(context, out _))
                .ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            Heading(GroupName(group.Key));
            foreach (var (t, s) in rows)
            {
                var satisfied = t.IsSatisfied(context, out var satisfiedMessage);
                var mark = recommended.Contains(t.Id) && s != TweakStatus.Applied ? "[x]" : "[ ]";
                var flags = string.Join(", ", new[]
                {
                    RiskName(t.Risk),
                    EvidenceName(t.Evidence),
                    t.RequiresReboot ? "riavvio" : null,
                    t.IsMeasurable ? "misurato" : "non misurabile",
                    t.AffectsSecurity ? "RIDUCE LA SICUREZZA" : null,
                }.Where(f => f is not null));
                Console.WriteLine($"  {mark} {(satisfied ? "già a posto" : StatusName(s)),-15} {t.Title}  ({flags})");
                if (satisfied)
                {
                    Console.WriteLine($"        {satisfiedMessage}");
                }
                else if (s == TweakStatus.NotApplicable && t.IsApplicable(context, out var reason) == false)
                {
                    Console.WriteLine($"        {reason}");
                }
            }
        }

        Heading($"Indicazioni guidate ({guidance.Count}): da seguire a mano, l'app non le modifica");
        foreach (var g in guidance)
        {
            Console.WriteLine($"  - {g.Title}: {g.Summary}");
        }

        Console.WriteLine($"\n  Interventi applicabili: {inspection.Tweaks.Count(t => status(t) != TweakStatus.NotApplicable)}, consigliati: {recommended.Count}." +
                          (all ? "" : " Usa --all per vedere anche quelli non applicabili e il motivo."));
    }

    internal static string GroupName(TweakGroup g) => g switch
    {
        TweakGroup.Power => "Alimentazione",
        TweakGroup.Services => "Servizi",
        TweakGroup.Startup => "Avvio automatico",
        TweakGroup.Tasks => "Attività pianificate",
        TweakGroup.Privacy => "Telemetria e privacy",
        TweakGroup.Visual => "Effetti visivi",
        TweakGroup.Gaming => "Impostazioni di gioco",
        TweakGroup.Gpu => "GPU e monitor",
        TweakGroup.Latency => "Latenza",
        TweakGroup.Network => "Rete",
        TweakGroup.Storage => "Archiviazione e memoria",
        TweakGroup.Security => "Sicurezza",
        TweakGroup.Defaults => "Ripristino dei valori predefiniti",
        _ => "Giochi",
    };

    internal static string StatusName(TweakStatus s) => s switch
    {
        TweakStatus.Applied => "già a posto",
        TweakStatus.PartiallyApplied => "parziale",
        TweakStatus.NotApplied => "da applicare",
        _ => "non applicabile",
    };

    internal static string RiskName(Risk r) => r switch
    {
        Risk.Low => "rischio basso",
        Risk.Medium => "rischio medio",
        _ => "rischio alto",
    };

    internal static string EvidenceName(TweakEvidence e) => e switch
    {
        TweakEvidence.Documented => "documentato",
        TweakEvidence.Situational => "dipende dal sistema",
        TweakEvidence.Debatable => "da verificare con la misura",
        TweakEvidence.RestoreDefault => "ripristino",
        _ => "privacy",
    };
}
