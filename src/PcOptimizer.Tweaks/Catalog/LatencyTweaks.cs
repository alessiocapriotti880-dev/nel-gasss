using PcOptimizer.Analysis.Model;
using PcOptimizer.Analysis.Parsing;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Catalog;

/// <summary>Modalità degli interrupt (MSI) e risoluzione del timer.</summary>
internal static class LatencyTweaks
{
    public static IEnumerable<CatalogTweak> Build(TweakContext context)
    {
        // MSI solo per GPU, rete e audio: per i controller di archiviazione e USB un driver che non gestisce bene MSI
        // può impedire l'avvio di Windows, un rischio sproporzionato rispetto al beneficio.
        var devices = context.Snapshot().PciDevices
            .Select(d => (Device: d, Category: DeviceClassParser.Category(d.ClassGuid)))
            .Where(x => x.Category is DeviceCategory.Gpu or DeviceCategory.Network or DeviceCategory.Audio)
            .Where(x => x.Device.Irqs.Count > 0 && x.Device.Irqs.All(i => i < IrqParser.MsiThreshold) && x.Device.MsiSupported != true);
        foreach (var (device, category) in devices)
        {
            var key = $@"SYSTEM\CurrentControlSet\Enum\{device.PnpDeviceId}\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";
            yield return new CatalogTweak
            {
                Id = $"latency.msi.{ServiceTweaks.Slug(device.PnpDeviceId)}",
                Title = $"Interrupt MSI per {device.Name}",
                Category = TweakCategory.Latency,
                Group = TweakGroup.Latency,
                WhatChanges = $"Abilita gli interrupt MSI (Message Signaled Interrupts) per \"{device.Name}\", che ora usa un interrupt condiviso a linea (richiede il riavvio).",
                Why = "Con gli interrupt a linea più dispositivi condividono lo stesso IRQ e il sistema deve interrogarli tutti; con MSI " +
                      "ogni dispositivo ha i propri interrupt, gestiti più rapidamente e distribuibili su più core. Se il dispositivo non " +
                      "supporta MSI, Windows continua a usare l'interrupt a linea.",
                ExpectedBenefit = "ISR/DPC più brevi e meno variabili per questo dispositivo.",
                Risk = Risk.Medium,
                Evidence = TweakEvidence.Situational,
                MeasuredBy = Measures.Latency,
                RequiresReboot = true,
                Recommended = category == DeviceCategory.Gpu,
                Caution = "Se dopo il riavvio il dispositivo non funziona, usa il ripristino di questo intervento (o \"Ripristina tutto\"); " +
                          "nel caso peggiore avvia in modalità provvisoria e usa il punto di ripristino creato prima delle modifiche.",
                Plan = _ => [Reg.Set(Reg.Machine(key, "MSISupported"), 1)],
            };
        }

        yield return new CatalogTweak
        {
            Id = "latency.timer.global-requests",
            Title = "Risoluzione del timer globale (Windows 11)",
            Category = TweakCategory.Latency,
            Group = TweakGroup.Latency,
            WhatChanges = "Imposta GlobalTimerResolutionRequests = 1: la risoluzione del timer chiesta da un programma torna a valere per tutto il sistema, come in Windows 10 (richiede il riavvio).",
            Why = "Da Windows 11 la risoluzione fine del timer vale solo per il programma che la chiede e non per le finestre ridotte a icona. " +
                  "I giochi che regolano il ritmo dei frame con brevi pause senza chiederla da sé possono beneficiarne.",
            ExpectedBenefit = "Effetto solo su alcuni giochi; aumenta i consumi a riposo. Si tiene solo se la misura migliora.",
            Risk = Risk.Low,
            Evidence = TweakEvidence.Debatable,
            MeasuredBy = Measures.Frames,
            RequiresReboot = true,
            AppliesTo = TierMask.Mid | TierMask.High,
            NotApplicableReason = c =>
                !c.IsWindows11 ? "Su Windows 10 la risoluzione del timer è già globale."
                : c.Report()?.Timer.GlobalResolutionRequests == true ? "Già impostato (anche da un altro programma): il valore predefinito si ripristina dalla sezione Ripristino."
                : null,
            Plan = _ => [Reg.Set(Reg.Machine(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests"), 1)],
        };
    }
}
