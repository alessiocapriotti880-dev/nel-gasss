using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Catalog;

/// <summary>Monitor alla frequenza massima disponibile.</summary>
internal static class DisplayTweaks
{
    public static IEnumerable<CatalogTweak> Build(TweakContext context)
    {
        foreach (var display in context.Env().Displays.Where(d => d.AvailableHz.Count > 0))
        {
            var max = display.AvailableHz.Max();
            yield return new CatalogTweak
            {
                Id = $"display.max-refresh.{ServiceTweaks.Slug(display.DeviceName)}",
                Title = $"Monitor {display.DeviceName.TrimStart('\\', '.')} a {max} Hz",
                Category = TweakCategory.Gpu,
                Group = TweakGroup.Gpu,
                WhatChanges = $"Porta la frequenza di aggiornamento da {display.CurrentHz} Hz a {max} Hz (alla risoluzione attuale).",
                Why = "Molti monitor ad alta frequenza restano a 60 Hz finché non si cambia l'impostazione: ogni frame resta sullo schermo più a lungo.",
                ExpectedBenefit = $"Immagine aggiornata fino a {max} volte al secondo: meno latenza visiva e movimento più fluido.",
                Evidence = TweakEvidence.Documented,
                Recommended = true,
                Caution = "Se lo schermo resta nero, attendi 15 secondi senza toccare nulla: Windows torna alla frequenza precedente. " +
                          "Per frequenze alte serve un cavo adeguato (DisplayPort o HDMI 2.0/2.1).",
                AlreadySatisfied = _ => display.CurrentHz >= max ? $"Il monitor è già alla frequenza massima ({max} Hz)." : null,
                Plan = _ => [new(new DisplayRefreshTarget(display.DeviceName), SettingValue.Of((uint)max))],
            };
        }
    }
}
