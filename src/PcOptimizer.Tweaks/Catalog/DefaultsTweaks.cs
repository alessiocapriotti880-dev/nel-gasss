using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Catalog;

/// <summary>
/// Ripristino dei valori predefiniti di Windows per le impostazioni modificate da altri tweak o ottimizzatori
/// (rilevate dall'analisi). Le modifiche dannose sono preselezionate; quelle "da misurare" si tengono ripristinate
/// solo se la misura migliora; quelle senza effetto sono solo pulizia.
/// </summary>
internal static class DefaultsTweaks
{
    public static IEnumerable<CatalogTweak> Build(TweakContext context)
    {
        var report = context.Report();
        if (report is null)
        {
            yield break;
        }

        foreach (var modified in report.ModifiedSettings)
        {
            if (Plan(modified, context) is not { Count: > 0 } changes)
            {
                continue;
            }

            var bcd = modified.Id.StartsWith("bcd.", StringComparison.Ordinal);
            yield return new CatalogTweak
            {
                Id = $"defaults.{modified.Id}",
                Title = $"Ripristina: {modified.Title}",
                Category = bcd ? TweakCategory.Latency : TweakCategory.System,
                Group = TweakGroup.Defaults,
                WhatChanges = $"Riporta \"{modified.Title}\" dal valore attuale ({modified.Current}) al predefinito di Windows ({modified.Default}).",
                Why = modified.Note,
                ExpectedBenefit = modified.Effect switch
                {
                    TweakEffect.Harmful => "Elimina una modifica che di norma peggiora prestazioni o stabilità.",
                    TweakEffect.Placebo => "Nessun effetto prestazionale: rimuove una modifica inutile.",
                    _ => "Effetto da misurare: si tiene il valore predefinito solo se la misura migliora.",
                },
                Risk = bcd ? Risk.Medium : Risk.Low,
                Evidence = TweakEvidence.RestoreDefault,
                MeasuredBy = modified.Effect == TweakEffect.Debatable ? Measures.LatencyAndFrames : Measures.None,
                RequiresReboot = bcd || modified.Id.StartsWith("timer.", StringComparison.Ordinal) || modified.Id.StartsWith("memory.", StringComparison.Ordinal),
                Recommended = modified.Effect == TweakEffect.Harmful,
                NotApplicableReason = c => bcd && c.Env().BitLockerOnSystemDrive == true
                    ? "BitLocker è attivo sull'unità di sistema: una modifica della configurazione di avvio può far chiedere la chiave di ripristino. Sospendi BitLocker prima, oppure lascia il valore."
                    : null,
                Plan = _ => changes,
            };
        }
    }

    private static IReadOnlyList<SettingChange>? Plan(ModifiedSetting modified, TweakContext context)
    {
        if (modified.Id.StartsWith("bcd.", StringComparison.Ordinal))
        {
            return [new(new BcdElementTarget(modified.Id["bcd.".Length..]), SettingValue.Absent)];
        }

        if (modified.Id.StartsWith("network.nagle.", StringComparison.Ordinal))
        {
            var key = NetworkTweaks.InterfaceKey(modified.Id["network.nagle.".Length..]);
            return [Reg.Delete(Reg.Machine(key, "TcpAckFrequency")), Reg.Delete(Reg.Machine(key, "TCPNoDelay")), Reg.Delete(Reg.Machine(key, "TcpDelAckTicks"))];
        }

        if (modified.Id == "gaming.fso.global")
        {
            // Senza "onora la scelta dell'utente" Windows torna al comportamento predefinito delle ottimizzazioni a schermo intero.
            return [Reg.Set(RegistryProbes.FseHonorUser.Target, 0)];
        }

        // Il criterio di telemetria è gestito dall'intervento di privacy: non si propone di "ripristinarlo".
        if (modified.Effect == TweakEffect.Other && modified.Id.StartsWith("privacy.", StringComparison.Ordinal))
        {
            return null;
        }

        var known = RegistryProbes.KnownTweaks.FirstOrDefault(k => k.Probe.Id == modified.Id);
        if (known is null)
        {
            return null;
        }

        var value = known.Defaults[0] is { } d ? SettingValue.Of(d) : SettingValue.Absent;
        return [new(known.Probe.Target, value)];
    }
}
