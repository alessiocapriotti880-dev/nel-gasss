using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Catalog;

/// <summary>Privacy, app in background, effetti visivi e mouse.</summary>
internal static class SystemTweaks
{
    private const string AdvertisingInfo = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo";
    private const string Privacy = @"Software\Microsoft\Windows\CurrentVersion\Privacy";
    private const string DataCollection = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";

    public static IEnumerable<CatalogTweak> Build(TweakContext context)
    {
        var low = context.Tier == HardwareTier.Low;

        yield return new CatalogTweak
        {
            Id = "privacy.telemetry.required",
            Title = "Dati di diagnostica al minimo",
            Category = TweakCategory.System,
            Group = TweakGroup.Privacy,
            WhatChanges = "Imposta il criterio di telemetria su \"solo dati di diagnostica obbligatori\" (AllowTelemetry = 1).",
            Why = "Riduce i dati inviati a Microsoft. Su Windows Home e Pro il livello 0 non esiste: 1 è il minimo effettivo.",
            ExpectedBenefit = "Privacy; nessun effetto prestazionale misurabile.",
            Evidence = TweakEvidence.Privacy,
            Caution = "Le Impostazioni mostreranno \"Alcune impostazioni sono gestite dall'organizzazione\": è normale con i criteri.",
            Plan = _ => [Reg.Set(Reg.Machine(DataCollection, "AllowTelemetry"), 1)],
        };

        yield return new CatalogTweak
        {
            Id = "privacy.advertising-id-off",
            Title = "ID pubblicità disattivato",
            Category = TweakCategory.System,
            Group = TweakGroup.Privacy,
            WhatChanges = "Disattiva l'ID pubblicità usato dalle app per gli annunci personalizzati.",
            Why = "Le app non possono collegare le tue attività a un identificativo pubblicitario.",
            ExpectedBenefit = "Privacy; nessun effetto prestazionale.",
            Evidence = TweakEvidence.Privacy,
            Plan = _ => [Reg.Set(Reg.User(AdvertisingInfo, "Enabled"), 0)],
        };

        yield return new CatalogTweak
        {
            Id = "privacy.tailored-experiences-off",
            Title = "Esperienze personalizzate disattivate",
            Category = TweakCategory.System,
            Group = TweakGroup.Privacy,
            WhatChanges = "Impedisce a Microsoft di usare i dati di diagnostica per suggerimenti e pubblicità personalizzati.",
            Why = "Meno suggerimenti e notifiche promozionali basati sull'uso del PC.",
            ExpectedBenefit = "Privacy; nessun effetto prestazionale.",
            Evidence = TweakEvidence.Privacy,
            Plan = _ => [Reg.Set(Reg.User(Privacy, "TailoredExperiencesWithDiagnosticDataEnabled"), 0)],
        };

        yield return new CatalogTweak
        {
            Id = "system.background-apps-off",
            Title = "App dello Store in background disattivate",
            Category = TweakCategory.System,
            Group = TweakGroup.Privacy,
            WhatChanges = "Disattiva l'interruttore globale \"Consenti l'esecuzione delle app in background\" (Windows 10).",
            Why = "Le app dello Store (Meteo, Notizie, Posta...) non si risvegliano in background per aggiornarsi.",
            ExpectedBenefit = "Meno attività in background, utile soprattutto con poca RAM.",
            Evidence = TweakEvidence.Situational,
            MeasuredBy = Measures.Background,
            Recommended = low,
            Caution = "Posta, Calendario e altre app dello Store non mostreranno notifiche finché non le apri.",
            NotApplicableReason = c => c.IsWindows11 ? "In Windows 11 l'interruttore globale non esiste più: si gestisce app per app." : null,
            Plan = _ =>
            [
                Reg.Set(Reg.User(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled"), 1),
                Reg.Set(Reg.User(@"Software\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle"), 0),
            ],
        };

        yield return new CatalogTweak
        {
            Id = "visual.transparency-off",
            Title = "Effetti di trasparenza disattivati",
            Category = TweakCategory.System,
            Group = TweakGroup.Visual,
            WhatChanges = "Disattiva la trasparenza di barra delle applicazioni, menu Start e finestre.",
            Why = "La trasparenza richiede alla GPU sfocature continue del desktop: pesa sulle schede video integrate o di fascia bassa.",
            ExpectedBenefit = "Meno lavoro per la GPU sul desktop e nei giochi in finestra; su GPU dedicate moderne nessuna differenza.",
            Evidence = TweakEvidence.Situational,
            Recommended = low,
            Plan = _ => [Reg.Set(Reg.User(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency"), 0)],
        };

        yield return new CatalogTweak
        {
            Id = "visual.animations-off",
            Title = "Animazioni delle finestre ridotte",
            Category = TweakCategory.System,
            Group = TweakGroup.Visual,
            WhatChanges = "Disattiva l'animazione di apertura/riduzione delle finestre e le animazioni della barra delle applicazioni " +
                          "(effetti visivi personalizzati). Vale dal prossimo accesso.",
            Why = "Le animazioni rendono più lente le interazioni sui PC con GPU integrata o poca memoria.",
            ExpectedBenefit = "Desktop più reattivo sui PC di fascia bassa; nessun effetto in gioco.",
            Evidence = TweakEvidence.Situational,
            RequiresReboot = true,
            Recommended = low,
            Plan = _ =>
            [
                Reg.Set(Reg.User(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting"), 3),
                Reg.Set(Reg.User(@"Control Panel\Desktop\WindowMetrics", "MinAnimate", RegValueKind.String), "0"),
                Reg.Set(Reg.User(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations"), 0),
            ],
        };

        yield return new CatalogTweak
        {
            Id = "input.mouse-acceleration-off",
            Title = "\"Precisione puntatore avanzata\" disattivata",
            Category = TweakCategory.System,
            Group = TweakGroup.Gaming,
            WhatChanges = "Disattiva l'accelerazione del mouse di Windows (MouseSpeed 0, soglie 0). Vale dal prossimo accesso.",
            Why = "Con l'accelerazione la stessa distanza fisica sposta il puntatore in modo diverso secondo la velocità: la mira " +
                  "nei giochi che usano il puntatore di Windows diventa meno costante.",
            ExpectedBenefit = "Movimento del mouse costante. Non cambia la latenza; i giochi con \"raw input\" la ignorano già.",
            Evidence = TweakEvidence.Documented,
            RequiresReboot = true,
            NotApplicableReason = c => c.Report()?.Gaming.MouseAcceleration == false ? "Accelerazione del mouse già disattivata." : null,
            Plan = _ =>
            [
                Reg.Set(Reg.User(@"Control Panel\Mouse", "MouseSpeed", RegValueKind.String), "0"),
                Reg.Set(Reg.User(@"Control Panel\Mouse", "MouseThreshold1", RegValueKind.String), "0"),
                Reg.Set(Reg.User(@"Control Panel\Mouse", "MouseThreshold2", RegValueKind.String), "0"),
            ],
        };
    }
}
