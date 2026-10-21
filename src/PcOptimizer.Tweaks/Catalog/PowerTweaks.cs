using System.Text.RegularExpressions;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Analysis.Parsing;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Catalog;

/// <summary>Piano energetico e impostazioni del processore e dei dispositivi (solo con alimentatore collegato).</summary>
internal static partial class PowerTweaks
{
    public const string PlanId = "power.plan.high-performance";

    public static IEnumerable<CatalogTweak> Build(TweakContext context)
    {
        var laptop = context.IsLaptop();
        var schemes = context.Snapshot().Power.Schemes;
        var target = schemes.FirstOrDefault(s => PowerPlanParser.Kind(s.Id, s.Name) == PowerPlanKind.HighPerformance)
                     ?? schemes.FirstOrDefault(s => PowerPlanParser.Kind(s.Id, s.Name) == PowerPlanKind.UltimatePerformance);

        yield return new CatalogTweak
        {
            Id = PlanId,
            Title = $"Piano energetico \"{target?.Name ?? "Prestazioni elevate"}\"",
            Category = TweakCategory.System,
            Group = TweakGroup.Power,
            WhatChanges = "Attiva il piano energetico ad alte prestazioni già presente in Windows.",
            Why = "Il piano Bilanciato abbassa frequenze e parcheggia i core per risparmiare energia: risalire richiede tempo " +
                  "e nei giochi produce frame time più irregolari. Il piano ad alte prestazioni tiene la CPU pronta.",
            ExpectedBenefit = "Risposta più rapida della CPU ai carichi improvvisi; effetto più visibile su CPU di fascia media e bassa.",
            Risk = laptop ? Risk.Medium : Risk.Low,
            Evidence = TweakEvidence.Documented,
            MeasuredBy = Measures.LatencyAndFrames,
            Recommended = !laptop,
            Order = 0,
            Caution = laptop ? "Su un portatile aumenta consumi e temperature anche a batteria: usalo con l'alimentatore." : null,
            NotApplicableReason = _ => target is null
                ? "Il piano ad alte prestazioni non è disponibile su questo PC (tipico dei sistemi con Modern Standby): usa la modalità di alimentazione."
                : null,
            Plan = _ => target is null ? [] : [new(new ActivePowerSchemeTarget(), SettingValue.Of(target.Id.ToString("D")))],
        };

        yield return new CatalogTweak
        {
            Id = "power.overlay.best-performance",
            Title = "Modalità di alimentazione \"Prestazioni ottimali\"",
            Category = TweakCategory.System,
            Group = TweakGroup.Power,
            WhatChanges = "Porta su \"Prestazioni ottimali\" il cursore della modalità di alimentazione (con alimentatore collegato).",
            Why = "Sui PC dove esiste solo il piano Bilanciato (Modern Standby) è questo il modo previsto da Windows per privilegiare le prestazioni.",
            ExpectedBenefit = "Frequenze della CPU più reattive con il piano Bilanciato.",
            Risk = laptop ? Risk.Medium : Risk.Low,
            Evidence = TweakEvidence.Documented,
            MeasuredBy = Measures.LatencyAndFrames,
            Recommended = !laptop && target is null,
            Order = 1,
            NotApplicableReason = c =>
                !c.Env().PowerOverlaySupported ? "Modalità di alimentazione non disponibile in questa versione di Windows."
                : PowerPlanParser.Kind(c.ActiveScheme(), null) != PowerPlanKind.Balanced ? "La modalità di alimentazione esiste solo con il piano Bilanciato."
                : null,
            Plan = _ => [new(new PowerOverlayTarget(), SettingValue.Of(PowerGuids.OverlayBestPerformance.ToString("D")))],
        };

        yield return Processor(
            "latency.power.min-processor-100", "Stato minimo del processore al 100%", PowerGuids.SubProcessor, PowerGuids.ProcessorMinState, 100,
            "Imposta lo stato minimo del processore al 100% nel piano attivo (con alimentatore).",
            "La CPU non scende alle frequenze minime a riposo: niente tempo di risalita quando arriva il carico.",
            "Frame time più regolari in alcuni giochi; su CPU moderne spesso nessuna differenza misurabile.",
            Risk.Medium, TweakEvidence.Debatable, TierMask.Mid | TierMask.High, recommended: false,
            caution: "Aumenta consumi e temperature a riposo.",
            notApplicable: c => c.IsLaptop() ? "Sconsigliato sui portatili: consumi e temperature." : null);

        yield return Processor(
            "latency.power.core-parking-off", "Core parking disattivato", PowerGuids.SubProcessor, PowerGuids.CoreParkingMinCores, 100,
            "Impone che il 100% dei core resti attivo (\"parcheggio\" dei core disattivato) nel piano attivo.",
            "Riattivare un core parcheggiato richiede tempo; con tutti i core attivi i thread del gioco trovano subito un core libero.",
            "Possibile riduzione degli scatti nei giochi molto multithread; spesso nessuna differenza.",
            Risk.Low, TweakEvidence.Debatable, TierMask.All, recommended: false,
            notApplicable: c => DualCcdX3d(c.Profile().Cpu.BrandString)
                ? "Su questa CPU (Ryzen X3D con due CCD) il parcheggio dei core serve al driver AMD per tenere il gioco sui core con la cache 3D: non va disattivato."
                : null);

        yield return Processor(
            "latency.power.cstates-off", "Stati di inattività della CPU disattivati", PowerGuids.SubProcessor, PowerGuids.IdleDisable, 1,
            "Disattiva gli stati di inattività del processore (C-state) nel piano attivo: i core non entrano mai in risparmio.",
            "L'uscita dagli stati di risparmio profondi aggiunge decine di microsecondi di latenza al risveglio.",
            "Latenza di risveglio più bassa e più costante; può però ridurre il boost (meno margine termico) e quindi gli FPS.",
            Risk.High, TweakEvidence.Debatable, TierMask.High, recommended: false,
            caution: "La CPU resta sempre al 100% di attività apparente: consumi e temperature aumentano molto. Si tiene solo se la misura migliora.",
            notApplicable: c => c.IsLaptop() ? "Non applicabile ai portatili: consumi e temperature." : null);

        yield return Processor(
            "latency.power.usb-suspend-off", "Sospensione selettiva USB disattivata", PowerGuids.SubUsb, PowerGuids.UsbSelectiveSuspend, 0,
            "Disattiva la sospensione selettiva USB nel piano attivo (con alimentatore).",
            "Le periferiche USB inattive vengono messe in risparmio e il risveglio richiede tempo: con mouse, tastiere e cuffie " +
            "può causare microinterruzioni o un primo input in ritardo.",
            "Nessun ritardo di risveglio delle periferiche USB.",
            Risk.Low, TweakEvidence.Situational, TierMask.All, recommended: !laptop,
            measuredBy: Measures.Latency);

        yield return Processor(
            "latency.power.pcie-aspm-off", "Risparmio energetico del collegamento PCIe disattivato", PowerGuids.SubPciExpress, PowerGuids.PcieAspm, 0,
            "Imposta \"Risparmio energia collegamento\" PCI Express su Disattivato nel piano attivo.",
            "Con ASPM attivo il collegamento di scheda video e SSD NVMe entra in stati di risparmio da cui esce in alcuni microsecondi.",
            "Latenze di accesso a GPU e SSD leggermente più basse; effetto piccolo.",
            Risk.Low, TweakEvidence.Situational, TierMask.All, recommended: false,
            notApplicable: c => c.IsLaptop() ? "Sconsigliato sui portatili: aumenta i consumi a riposo." : null);

        yield return Processor(
            "storage.power.disk-idle-never", "Dischi meccanici sempre accesi", PowerGuids.SubDisk, PowerGuids.DiskIdle, 0,
            "Imposta \"Disattiva disco rigido dopo\" su Mai nel piano attivo (con alimentatore).",
            "Un disco meccanico fermo impiega alcuni secondi a ripartire: in gioco si traduce in blocchi durante i caricamenti.",
            "Niente attese di riavvio del disco meccanico.",
            Risk.Low, TweakEvidence.Situational, TierMask.All, recommended: context.HasHdd() && !laptop,
            notApplicable: c => c.HasHdd() ? null : "Nessun disco meccanico rilevato.",
            measuredBy: Measures.None);
    }

    private static CatalogTweak Processor(
        string id, string title, Guid subGroup, Guid setting, uint value, string what, string why, string benefit,
        Risk risk, TweakEvidence evidence, TierMask tiers, bool recommended,
        string? caution = null, Func<TweakContext, string?>? notApplicable = null, IReadOnlyList<string>? measuredBy = null) => new()
    {
        Id = id,
        Title = title,
        Category = id.StartsWith("storage", StringComparison.Ordinal) ? TweakCategory.StorageMemory : TweakCategory.Latency,
        Group = TweakGroup.Power,
        WhatChanges = what,
        Why = why,
        ExpectedBenefit = benefit,
        Risk = risk,
        Evidence = evidence,
        AppliesTo = tiers,
        Recommended = recommended,
        Caution = caution,
        Order = 10,
        MeasuredBy = measuredBy ?? Measures.LatencyAndFrames,
        NotApplicableReason = c => c.ActiveScheme() is null ? "Piano energetico attivo non rilevato." : notApplicable?.Invoke(c),
        Plan = c => c.ActiveScheme() is { } scheme
            ? [new(new PowerSettingTarget(scheme, subGroup, setting, PowerSource.Ac, title), SettingValue.Of(value))]
            : [],
    };

    /// <summary>Ryzen 9 X3D con due CCD (7900X3D, 7950X3D, 9900X3D, 9950X3D): usano il parcheggio per lo scheduling dei giochi.</summary>
    internal static bool DualCcdX3d(string brand) => DualCcdX3dPattern().IsMatch(brand);

    [GeneratedRegex(@"Ryzen 9 \d{4}X3D", RegexOptions.IgnoreCase)]
    private static partial Regex DualCcdX3dPattern();
}
