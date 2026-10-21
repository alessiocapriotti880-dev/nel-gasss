using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Catalog;

/// <summary>
/// Attività pianificate: alcune attività di telemetria di Windows note per picchi di CPU e disco (CompatTelRunner) e
/// le attività di aggiornamento/telemetria dei programmi riconosciuti. Mai quelle di sicurezza o di aggiornamento di
/// Windows e dei browser, né la manutenzione dei dischi (TRIM).
/// </summary>
internal static class TaskTweaks
{
    internal static readonly (string Path, string Title, string Why, bool Recommended)[] WindowsTasks =
    [
        (@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser", "Valutazione compatibilità (CompatTelRunner)",
            "Analizza i programmi installati per la telemetria di compatibilità: è noto per picchi lunghi di CPU e disco (CompatTelRunner.exe).", true),
        (@"\Microsoft\Windows\Application Experience\ProgramDataUpdater", "Aggiornamento dati programmi (telemetria)",
            "Raccoglie dati sui programmi per il programma di miglioramento dell'esperienza utente.", false),
        (@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", "Programma Analisi utilizzo software (Consolidator)",
            "Raccoglie e invia dati di utilizzo.", false),
        (@"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip", "Programma Analisi utilizzo software (USB)",
            "Raccoglie dati di utilizzo delle periferiche USB.", false),
        (@"\Microsoft\Windows\Feedback\Siuf\DmClient", "Feedback di Windows (DmClient)",
            "Raccoglie dati per le richieste di feedback.", false),
        (@"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload", "Feedback di Windows (scenari)",
            "Raccoglie dati per le richieste di feedback.", false),
    ];

    /// <summary>Attività che non vanno mai disattivate, anche se riconosciute come "aggiornamento".</summary>
    private static readonly string[] ProtectedFragments =
    [
        @"\Microsoft\Windows\Windows Defender", @"\Microsoft\Windows\WindowsUpdate", @"\Microsoft\Windows\UpdateOrchestrator",
        @"\Microsoft\Windows\WaaSMedic", @"\Microsoft\Windows\Defrag", @"\Microsoft\Windows\Time Synchronization",
        "GoogleUpdate", "GoogleUpdater", "MicrosoftEdgeUpdate", "Mozilla", "BraveSoftwareUpdate", "Opera scheduled",
    ];

    public static bool IsProtected(string path) =>
        ProtectedFragments.Any(f => path.Contains(f, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<CatalogTweak> Build(TweakContext context)
    {
        var tasks = context.Snapshot().ScheduledTasks;
        foreach (var (path, title, why, recommended) in WindowsTasks)
        {
            var task = tasks.FirstOrDefault(t => t.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            yield return Make($"tasks.windows.{ServiceTweaks.Slug(path[(path.LastIndexOf('\\') + 1)..])}", title, path, why,
                TweakEvidence.Privacy, recommended, task is null ? "Attività non presente in questa versione di Windows." : null);
        }

        if (context.Software() is not { } catalog)
        {
            yield break;
        }

        foreach (var task in tasks.Where(t => t.Enabled && !t.IsMicrosoft && !IsProtected(t.Path)))
        {
            var entry = catalog.MatchTask(task.Path, task.Executable);
            if (entry is null || entry.NeverTouch || entry.Category is not (SoftwareCategory.Updater or SoftwareCategory.Telemetry))
            {
                continue;
            }

            yield return Make($"tasks.thirdparty.{ServiceTweaks.Slug(task.Path)}", $"{entry.Name}: attività {task.Path.TrimStart('\\')}", task.Path,
                $"Attività di {ServiceTweaks.CategoryName(entry.Category)} che si avvia in background ({string.Join(", ", task.Triggers)}).",
                TweakEvidence.Documented, recommended: true, notApplicable: null);
        }
    }

    private static CatalogTweak Make(string id, string title, string path, string why, TweakEvidence evidence, bool recommended, string? notApplicable) => new()
    {
        Id = id,
        Title = title,
        Category = TweakCategory.System,
        Group = TweakGroup.Tasks,
        WhatChanges = $"Disattiva l'attività pianificata {path} (resta presente e si può riattivare).",
        Why = why,
        ExpectedBenefit = "Niente picchi di attività in background causati da questa attività.",
        Risk = Risk.Low,
        Evidence = evidence,
        MeasuredBy = Measures.None,
        Recommended = recommended,
        NotApplicableReason = _ => notApplicable,
        Plan = _ => [new(new ScheduledTaskTarget(path), SettingValue.Of(ScheduledTaskValues.Disabled))],
    };
}
