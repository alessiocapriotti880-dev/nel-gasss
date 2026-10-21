using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Catalog;

/// <summary>Archiviazione e memoria (TRIM, file di paging) e sicurezza (integrità della memoria).</summary>
internal static class StorageSecurityTweaks
{
    /// <summary>Valore di PagingFiles per "Gestisci automaticamente le dimensioni del file di paging per tutte le unità".</summary>
    internal const string SystemManagedPageFile = @"?:\pagefile.sys";

    public static IEnumerable<CatalogTweak> Build(TweakContext context)
    {
        var env = context.Env();

        yield return new CatalogTweak
        {
            Id = "storage.trim-on",
            Title = "TRIM per gli SSD attivo",
            Category = TweakCategory.StorageMemory,
            Group = TweakGroup.Storage,
            WhatChanges = "Riattiva l'invio dei comandi TRIM agli SSD (DisableDeleteNotification = 0, come \"fsutil behavior set DisableDeleteNotify 0\").",
            Why = "Senza TRIM l'SSD non sa quali blocchi sono liberi: le scritture rallentano con il tempo e l'usura aumenta.",
            ExpectedBenefit = "Prestazioni di scrittura dell'SSD stabili nel tempo.",
            Evidence = TweakEvidence.Documented,
            Recommended = true,
            NotApplicableReason = c =>
                !c.HasSsd() ? "Nessun SSD rilevato."
                : env.Reg(TweakProbes.DisableDeleteNotify).Data != "1" ? "TRIM è già attivo."
                : null,
            Plan = _ => [Reg.Set(TweakProbes.All[TweakProbes.DisableDeleteNotify], 0)],
        };

        var pagingFiles = env.Reg(TweakProbes.PagingFiles);
        yield return new CatalogTweak
        {
            Id = "storage.pagefile-system-managed",
            Title = "File di paging gestito da Windows",
            Category = TweakCategory.StorageMemory,
            Group = TweakGroup.Storage,
            WhatChanges = "Ripristina \"Gestisci automaticamente le dimensioni del file di paging\" (richiede il riavvio).",
            Why = "Senza file di paging (o con uno troppo piccolo) i giochi che superano la memoria impegnabile si chiudono con errori " +
                  "\"memoria insufficiente\" anche se la RAM sembra bastare: Windows riserva comunque memoria virtuale.",
            ExpectedBenefit = "Niente chiusure improvvise dei giochi per memoria esaurita.",
            Evidence = TweakEvidence.RestoreDefault,
            RequiresReboot = true,
            Recommended = true,
            NotApplicableReason = c =>
                c.Report()?.Software.PageFileEnabled != false && (!pagingFiles.Exists || pagingFiles.AsMulti().Length > 0)
                    ? "Il file di paging è attivo."
                    : null,
            Plan = _ => [new(TweakProbes.All[TweakProbes.PagingFiles], SettingValue.OfMulti([SystemManagedPageFile]))],
        };

        yield return new CatalogTweak
        {
            Id = "security.memory-integrity-off",
            Title = "Integrità della memoria (HVCI) disattivata",
            Category = TweakCategory.System,
            Group = TweakGroup.Security,
            WhatChanges = "Disattiva l'integrità della memoria (Sicurezza di Windows > Sicurezza dispositivi > Isolamento core). Richiede il riavvio.",
            Why = "L'integrità della memoria fa verificare all'hypervisor il codice eseguito nel kernel: in alcuni giochi costa qualche " +
                  "punto percentuale di FPS. Microsoft la indica tra le opzioni che i giocatori possono disattivare.",
            ExpectedBenefit = "Da nessuno a qualche FPS secondo il gioco e la CPU: si tiene solo se la misura mostra un miglioramento.",
            Risk = Risk.High,
            Evidence = TweakEvidence.Debatable,
            MeasuredBy = Measures.LatencyAndFrames,
            RequiresReboot = true,
            AffectsSecurity = true,
            SecurityWarning =
                "Stai riducendo una protezione di sicurezza: senza integrità della memoria un driver malevolo o vulnerabile può " +
                "manomettere il kernel più facilmente. Alcuni anti-cheat e giochi la richiedono, altri no: verifica i requisiti dei tuoi giochi. " +
                "Se la misura non mostra un miglioramento, l'app la riattiva automaticamente.",
            Caution = "Scelta esplicita richiesta: non è mai preselezionata.",
            NotApplicableReason = c =>
                env.Reg(TweakProbes.HvciPolicy).Exists ? "L'integrità della memoria è imposta da criteri di gruppo: non va modificata da qui."
                : env.Reg(TweakProbes.HvciLocked).Data == "1" ? "L'integrità della memoria è bloccata dal firmware (UEFI lock): si disattiva solo dal BIOS/UEFI."
                : c.Report()?.Security.HvciRunning != true && c.Snapshot().Reg("security.hvci").Data != "1" ? "L'integrità della memoria non è attiva."
                : null,
            Plan = _ => [Reg.Set(Reg.Machine(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled"), 0)],
        };
    }
}
