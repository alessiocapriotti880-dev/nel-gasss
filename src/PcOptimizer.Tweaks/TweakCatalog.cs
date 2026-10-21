using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Hardware;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Tweaks.Catalog;
using PcOptimizer.Tweaks.Context;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks;

/// <summary>
/// Catalogo completo degli interventi per questo PC: quelli fissi più quelli costruiti dai dati reali (servizi,
/// programmi in avvio, dispositivi, impostazioni modificate da altri, giochi indicati dall'utente).
/// </summary>
public static class TweakCatalog
{
    public static TweakContext CreateContext(
        HardwareProfile profile, HardwareAssessment assessment, SystemSnapshot snapshot, SystemReport report,
        TweakEnvironment environment, KnownSoftwareCatalog software, GameLibrary? games = null) =>
        HardwareContext.CreateTweakContext(profile, assessment)
            .With(snapshot)
            .With(report)
            .With(environment)
            .With(software)
            .With(games ?? GameLibrary.Empty);

    public static IReadOnlyList<CatalogTweak> Build(TweakContext context)
    {
        IEnumerable<CatalogTweak>[] sources =
        [
            PowerTweaks.Build(context),
            ServiceTweaks.Build(context),
            StartupTweaks.Build(context),
            TaskTweaks.Build(context),
            SystemTweaks.Build(context),
            GamingTweaks.Build(context),
            DisplayTweaks.Build(context),
            LatencyTweaks.Build(context),
            NetworkTweaks.Build(context),
            StorageSecurityTweaks.Build(context),
            DefaultsTweaks.Build(context),
            GameTweaks.Build(context),
        ];

        // Gli id devono essere unici (servono al giornale): due voci che producono lo stesso id ricevono un suffisso.
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<CatalogTweak>();
        foreach (var tweak in sources.SelectMany(s => s))
        {
            var count = seen.GetValueOrDefault(tweak.Id);
            seen[tweak.Id] = count + 1;
            result.Add(count == 0 ? tweak : Rename(tweak, $"{tweak.Id}-{count + 1}"));
        }

        return result.OrderBy(t => t.Order).ThenBy(t => t.Group).ToList();
    }

    /// <summary>
    /// Profilo consigliato per la fascia del PC: interventi preselezionati, applicabili e non ancora applicati.
    /// Gli interventi che riducono la sicurezza non sono mai preselezionati.
    /// </summary>
    public static IReadOnlyList<CatalogTweak> Recommended(IEnumerable<CatalogTweak> tweaks, TweakContext context) =>
        tweaks.Where(t => t.Recommended && !t.AffectsSecurity && t.AppliesTo.Includes(context.Tier) && t.IsApplicable(context, out _))
            .ToList();

    private static CatalogTweak Rename(CatalogTweak t, string id) => new()
    {
        Id = id,
        Title = t.Title,
        Category = t.Category,
        Group = t.Group,
        WhatChanges = t.WhatChanges,
        Why = t.Why,
        ExpectedBenefit = t.ExpectedBenefit,
        Risk = t.Risk,
        AppliesTo = t.AppliesTo,
        RequiresReboot = t.RequiresReboot,
        AffectsSecurity = t.AffectsSecurity,
        SecurityWarning = t.SecurityWarning,
        HowToRevert = t.HowToRevert,
        Evidence = t.Evidence,
        MeasuredBy = t.MeasuredBy,
        Recommended = t.Recommended,
        Order = t.Order,
        Caution = t.Caution,
        NotApplicableReason = t.NotApplicableReason,
        AlreadySatisfied = t.AlreadySatisfied,
        Plan = t.Plan,
    };
}

/// <summary>Descrizione dei profili per fascia mostrata nell'interfaccia.</summary>
public static class TierProfiles
{
    public static string Describe(HardwareTier tier) => tier switch
    {
        HardwareTier.Low =>
            "Fascia bassa: la priorità è liberare risorse. Meno programmi e servizi in background, niente indicizzazione su disco " +
            "meccanico, effetti visivi ridotti, piano energetico ad alte prestazioni. I guadagni maggiori vengono spesso da hardware " +
            "(SSD, RAM in doppio canale): l'app lo segnala nelle osservazioni.",
        HardwareTier.Mid =>
            "Fascia media: piano energetico e periferiche senza risparmio energetico, registrazione in background disattivata, " +
            "pulizia dell'avvio. Gli interventi di latenza \"da misurare\" sono disponibili ma non preselezionati.",
        _ =>
            "Fascia alta: il sistema ha margine, quindi si punta alla regolarità dei frame e alla latenza: frequenza del monitor, " +
            "interrupt MSI, rete e interventi di latenza da verificare con la misura. Molti tweak \"classici\" qui non cambiano nulla.",
    };
}
