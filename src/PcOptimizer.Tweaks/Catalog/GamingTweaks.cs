using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Analysis.Parsing;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Catalog;

/// <summary>Impostazioni di gioco di Windows: modalità gioco, registrazione in background, giochi in finestra, HAGS.</summary>
internal static class GamingTweaks
{
    /// <summary>Prima build con "Ottimizzazioni per i giochi in finestra" nelle Impostazioni (Windows 11 22H2).</summary>
    public const int WindowedOptimizationsBuild = 22621;

    /// <summary>Prima build con la pianificazione GPU con accelerazione hardware (Windows 10 2004).</summary>
    public const int HagsBuild = 19041;

    public static IEnumerable<CatalogTweak> Build(TweakContext context)
    {
        var gaming = context.Report()?.Gaming;

        yield return new CatalogTweak
        {
            Id = "gaming.game-mode-on",
            Title = "Modalità gioco attiva",
            Category = TweakCategory.Gpu,
            Group = TweakGroup.Gaming,
            WhatChanges = "Riattiva la modalità gioco di Windows (attiva di default).",
            Why = "Durante il gioco Windows rinvia l'installazione di driver e aggiornamenti e le notifiche di riavvio, e dà priorità al gioco.",
            ExpectedBenefit = "Meno interruzioni in background durante il gioco.",
            Evidence = TweakEvidence.Documented,
            MeasuredBy = Measures.Frames,
            Recommended = true,
            AlreadySatisfied = _ => gaming?.GameMode != false ? "La modalità gioco è già attiva." : null,
            Plan = _ => [Reg.Set(RegistryProbes.GameMode.Target, 1)],
        };

        yield return new CatalogTweak
        {
            Id = "gaming.background-recording-off",
            Title = "Registrazione in background disattivata",
            Category = TweakCategory.Gpu,
            Group = TweakGroup.Gaming,
            WhatChanges = "Disattiva \"Registra in background mentre gioco\" della Xbox Game Bar (le clip manuali restano disponibili).",
            Why = "La registrazione continua degli ultimi minuti tiene sempre impegnato il codificatore video e una parte della GPU.",
            ExpectedBenefit = "Qualche FPS in più e frame time più regolari, soprattutto su GPU di fascia media e bassa.",
            Evidence = TweakEvidence.Documented,
            MeasuredBy = Measures.Frames,
            Recommended = true,
            AlreadySatisfied = _ => gaming?.BackgroundRecording != true ? "La registrazione in background è già disattivata." : null,
            Plan = _ => [Reg.Set(RegistryProbes.HistoricalCapture.Target, 0)],
        };

        yield return new CatalogTweak
        {
            Id = "gaming.windowed-optimizations-on",
            Title = "Ottimizzazioni per i giochi in finestra",
            Category = TweakCategory.Gpu,
            Group = TweakGroup.Gaming,
            WhatChanges = "Attiva \"Ottimizzazioni per i giochi in finestra\" (Impostazioni > Schermo > Grafica).",
            Why = "I giochi DirectX 10/11 in finestra o finestra senza bordi usano il modello di presentazione \"flip\": latenza " +
                  "simile allo schermo intero e supporto a VRR e Auto HDR.",
            ExpectedBenefit = "Latenza più bassa nei giochi DX10/11 in finestra senza bordi.",
            Evidence = TweakEvidence.Documented,
            MeasuredBy = Measures.Frames,
            Recommended = true,
            NotApplicableReason = c => c.WindowsBuild < WindowedOptimizationsBuild ? "Disponibile da Windows 11 22H2." : null,
            AlreadySatisfied = _ => gaming?.WindowedOptimizations == true ? "Già attive." : null,
            Plan = c => [Reg.Set(RegistryProbes.DirectXGlobal.Target, WithFlag(c.Snapshot().Reg(RegistryProbes.DirectXGlobal.Id).Data, "SwapEffectUpgradeEnable", "1"))],
        };

        var hagsCandidate = context.Snapshot().GpuScheduling.FirstOrDefault(g => g.HwSchSupported && !g.HwSchEnabled);
        yield return new CatalogTweak
        {
            Id = "gaming.hags-on",
            Title = "Pianificazione GPU con accelerazione hardware (HAGS)",
            Category = TweakCategory.Gpu,
            Group = TweakGroup.Gpu,
            WhatChanges = "Attiva la pianificazione GPU con accelerazione hardware (richiede il riavvio).",
            Why = "La GPU gestisce da sé la coda dei comandi invece di Windows: meno lavoro per la CPU. È necessaria per la " +
                  "generazione di frame DLSS 3 e successivi.",
            ExpectedBenefit = "Risultati variabili secondo GPU, driver e gioco: si tiene solo se la misura migliora (o se serve per la generazione di frame).",
            Risk = Risk.Medium,
            Evidence = TweakEvidence.Debatable,
            MeasuredBy = Measures.LatencyAndFrames,
            RequiresReboot = true,
            AppliesTo = TierMask.Mid | TierMask.High,
            NotApplicableReason = c =>
                c.WindowsBuild < HagsBuild ? "Richiede Windows 10 versione 2004 o successiva."
                : hagsCandidate is null && gaming?.Hags != HagsState.On ? "La scheda video o il driver non supportano HAGS."
                : null,
            AlreadySatisfied = _ => hagsCandidate is null && gaming?.Hags == HagsState.On ? "HAGS è già attiva." : null,
            Plan = _ => [Reg.Set(RegistryProbes.HwSchMode.Target, 2)],
        };
    }

    /// <summary>Imposta una chiave nella stringa "Chiave=valore;..." di DirectX conservando le altre.</summary>
    internal static string WithFlag(string? current, string key, string value)
    {
        var pairs = DirectXSettingsParser.Parse(current).Where(p => !p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
        pairs.Add(new KeyValuePair<string, string>(key, value));
        return string.Concat(pairs.Select(p => $"{p.Key}={p.Value};"));
    }
}
