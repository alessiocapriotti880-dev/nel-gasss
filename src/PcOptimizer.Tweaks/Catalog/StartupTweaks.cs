using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Catalog;

/// <summary>
/// Programmi in esecuzione automatica: si disattivano come fa Gestione attività (StartupApproved, o lo stato
/// dell'attività di avvio per le app dello Store). Il programma resta installato e si può avviare a mano.
/// </summary>
internal static class StartupTweaks
{
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    private const string StoreTasks = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\";

    /// <summary>Stato "disattivato" scritto da Gestione attività: primo byte dispari; la data (qui zero) è solo informativa.</summary>
    internal static readonly byte[] DisabledBytes = [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>StartupTaskState "disattivata dall'utente".</summary>
    internal const uint StoreDisabledByUser = 1;

    private static readonly SoftwareCategory[] Never = [SoftwareCategory.AntiCheat, SoftwareCategory.Security];

    private static readonly SoftwareCategory[] RecommendedCategories =
    [
        SoftwareCategory.Launcher, SoftwareCategory.Chat, SoftwareCategory.Updater, SoftwareCategory.Telemetry,
        SoftwareCategory.Media, SoftwareCategory.Optimizer, SoftwareCategory.Overlay, SoftwareCategory.Recording, SoftwareCategory.Browser,
    ];

    public static IEnumerable<CatalogTweak> Build(TweakContext context)
    {
        var catalog = context.Software();
        foreach (var entry in context.Snapshot().Startup.Where(e => e.Enabled && e.ApprovalName is not null))
        {
            var known = catalog?.MatchStartup(entry.Name, entry.Command);
            if (known is not null && (known.NeverTouch || Never.Contains(known.Category)))
            {
                continue;
            }

            // Voci di Microsoft non riconosciute (es. Sicurezza di Windows nella barra delle applicazioni): non si propongono.
            if (known is null && entry.Company?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true)
            {
                continue;
            }

            if (entry.Source == StartupSource.StoreApp && entry.StoreTaskState == 4)
            {
                continue; // attivata da criteri di gruppo: non modificabile dall'utente
            }

            var change = Change(entry);
            if (change is null)
            {
                continue;
            }

            yield return new CatalogTweak
            {
                Id = $"startup.{entry.Source.ToString().ToLowerInvariant()}.{ServiceTweaks.Slug(entry.ApprovalName!)}",
                Title = $"Avvio automatico: {(known?.Name ?? entry.Name)}",
                Category = TweakCategory.System,
                Group = TweakGroup.Startup,
                WhatChanges = $"Disattiva l'avvio automatico di \"{entry.Name}\" (come in Gestione attività > App di avvio). Il programma resta installato.",
                Why = known is null
                    ? "Programma avviato a ogni accesso: occupa memoria e CPU in background anche quando non serve."
                    : $"{known.Name} ({ServiceTweaks.CategoryName(known.Category)}) viene avviato a ogni accesso anche quando non serve" +
                      (known.Note is null ? "." : $": {known.Note}"),
                ExpectedBenefit = "Avvio più rapido e meno programmi in background durante il gioco.",
                Risk = known is null || known.Category is SoftwareCategory.Rgb or SoftwareCategory.Audio or SoftwareCategory.Network ? Risk.Medium : Risk.Low,
                Evidence = known is null ? TweakEvidence.Situational : TweakEvidence.Documented,
                MeasuredBy = Measures.Boot,
                RequiresReboot = true,
                Recommended = known is not null && RecommendedCategories.Contains(known.Category),
                Caution = known is null
                    ? "Programma non riconosciuto: verifica che non serva a una periferica (touchpad, tasti speciali, VPN, backup) prima di disattivarlo."
                    : known.Category switch
                    {
                        SoftwareCategory.Rgb => "Luci RGB e profili delle periferiche potrebbero non caricarsi finché non apri il programma.",
                        SoftwareCategory.Cloud => "La sincronizzazione dei file partirà solo quando apri il programma.",
                        SoftwareCategory.Chat => "Non riceverai messaggi finché non apri il programma.",
                        _ => null,
                    },
                Plan = _ => [change],
            };
        }
    }

    private static SettingChange? Change(StartupEntry entry)
    {
        var name = entry.ApprovalName!;
        return entry.Source switch
        {
            StartupSource.RegistryMachine => new(Reg.Machine(Approved + "Run", name, RegValueKind.Binary), SettingValue.OfBinary(DisabledBytes)),
            StartupSource.RegistryMachine32 => new(Reg.Machine(Approved + "Run32", name, RegValueKind.Binary), SettingValue.OfBinary(DisabledBytes)),
            StartupSource.RegistryUser => new(Reg.User(Approved + "Run", name, RegValueKind.Binary), SettingValue.OfBinary(DisabledBytes)),
            StartupSource.StartupFolderCommon => new(Reg.Machine(Approved + "StartupFolder", name, RegValueKind.Binary), SettingValue.OfBinary(DisabledBytes)),
            StartupSource.StartupFolderUser => new(Reg.User(Approved + "StartupFolder", name, RegValueKind.Binary), SettingValue.OfBinary(DisabledBytes)),
            StartupSource.StoreApp => new(Reg.User(StoreTasks + name, "State"), SettingValue.Of(StoreDisabledByUser)),
            _ => null,
        };
    }
}
