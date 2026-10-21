using System.Globalization;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Model;

namespace PcOptimizer.Analysis.Evaluation;

/// <summary>
/// Osservazioni sulla configurazione. Regole prudenti: un effetto non dimostrato viene
/// presentato come "da misurare", mai come guadagno garantito; le protezioni di sicurezza
/// vengono solo descritte, mai proposte come scorciatoia per gli FPS.
/// </summary>
internal static class AnalysisFindings
{
    private const double OneGiB = 1024.0 * 1024 * 1024;

    internal sealed record Input(
        SystemSnapshot Snapshot,
        AnalysisContext Context,
        PowerReport Power,
        TimerReport Timer,
        IReadOnlyList<InterruptReport> Interrupts,
        GamingReport Gaming,
        SecurityReport Security,
        SoftwareReport Software,
        IReadOnlyList<ModifiedSetting> Modified);

    public static IReadOnlyList<Finding> Build(Input input)
    {
        var findings = new List<Finding>();
        AddPower(input, findings);
        AddTimer(input, findings);
        AddInterrupts(input, findings);
        AddGaming(input, findings);
        AddSecurity(input, findings);
        AddSoftware(input, findings);
        AddModified(input, findings);
        AddCollection(input, findings);
        return findings.OrderByDescending(f => f.Severity).ToList();
    }

    private static void AddPower(Input input, List<Finding> findings)
    {
        var p = input.Power;
        if (p.Kind == PowerPlanKind.PowerSaver)
        {
            findings.Add(new Finding("power-plan-saver",
                input.Context.IsLaptop ? FindingSeverity.Suggestion : FindingSeverity.Warning, "Alimentazione",
                "Piano energetico \"Risparmio di energia\" attivo",
                "Il piano riduce frequenze e prestazioni della CPU per consumare meno.",
                "Per giocare usa \"Bilanciato\" con la modalità \"Prestazioni massime\" oppure \"Prestazioni elevate\": " +
                "il piano ottimizzato per la latenza è tra gli interventi proposti."));
        }

        if (p.MaxProcessorPercent is < 100 and var max)
        {
            findings.Add(new Finding("power-cpu-capped", FindingSeverity.Warning, "Alimentazione",
                $"CPU limitata al {max}% dal piano energetico",
                "Lo \"stato massimo del processore\" sotto il 100% impedisce alla CPU di raggiungere le frequenze di turbo.",
                "Riporta lo stato massimo al 100% (Opzioni risparmio energia > Gestione risparmio energia del processore)."));
        }

        if (p.BoostMode == 0)
        {
            findings.Add(new Finding("power-boost-off", FindingSeverity.Warning, "Alimentazione",
                "Turbo della CPU disattivato",
                "Con la modalità turbo disattivata la CPU non supera la frequenza base: prestazioni single-thread molto più basse.",
                "Riattivalo, a meno che non l'abbia disattivato tu per contenere le temperature di un portatile."));
        }

        if (p.OverlayMode == "Massima efficienza energetica")
        {
            findings.Add(new Finding("power-overlay-efficiency", FindingSeverity.Suggestion, "Alimentazione",
                "Modalità di alimentazione \"Massima efficienza energetica\"",
                "Windows privilegia i consumi rispetto alle prestazioni anche con il piano Bilanciato.",
                "Per giocare scegli \"Prestazioni massime\" in Impostazioni > Sistema > Alimentazione."));
        }

        if (p.IdleDisabled == true)
        {
            findings.Add(new Finding("power-idle-disabled", FindingSeverity.Info, "Alimentazione",
                "Stati di inattività della CPU (C-state) disattivati",
                "La CPU non entra mai in risparmio energetico: consumi e temperature più alti e, su molte CPU, " +
                "meno margine per il turbo. Il vantaggio sulla latenza non è garantito e va misurato.",
                "Le misure prima/dopo diranno se mantenerlo; in caso contrario il ripristino è automatico."));
        }
    }

    private static void AddTimer(Input input, List<Finding> findings)
    {
        var t = input.Timer;
        if (t.UsePlatformClock == true)
        {
            findings.Add(new Finding("timer-useplatformclock", FindingSeverity.Warning, "Timer",
                "HPET forzato come sorgente del tempo (useplatformclock)",
                "È un tweak diffuso su internet, non un'impostazione di Windows: obbliga il sistema a usare il timer HPET, " +
                "molto più lento da leggere del TSC della CPU, e può peggiorare latenza e frame time.",
                "La rimozione (bcdedit /deletevalue useplatformclock, con riavvio) è tra gli interventi proposti, con misura e rollback."));
        }

        if (t.DisableDynamicTick == true)
        {
            findings.Add(new Finding("timer-dynamictick", FindingSeverity.Info, "Timer",
                "Dynamic tick disattivato",
                "Il tick di sistema non si ferma mai quando la CPU è inattiva: aumentano i consumi, l'effetto sulla latenza va misurato."));
        }

        if (t.CurrentMs is { } current && current < 1.5)
        {
            findings.Add(new Finding("timer-high-resolution-idle", FindingSeverity.Info, "Timer",
                $"Risoluzione del timer a {ScoreFmt(current, "0.###")} ms durante l'analisi",
                "Un programma in esecuzione sta chiedendo un timer ad alta risoluzione (succede con browser che riproducono " +
                "video, app di chat o launcher). Costa un po' di energia; durante il gioco la richiede comunque il gioco stesso."));
        }

        if (t.HpetDeviceEnabled == false)
        {
            findings.Add(new Finding("timer-hpet-device-disabled", FindingSeverity.Info, "Timer",
                "Dispositivo HPET disattivato in Gestione dispositivi",
                "È un tweak diffuso: di norma Windows usa già il TSC della CPU come sorgente principale del tempo. " +
                "L'effetto reale verrà misurato."));
        }
    }

    private static void AddInterrupts(Input input, List<Finding> findings)
    {
        foreach (var gpu in input.Interrupts.Where(i => i.Category == DeviceCategory.Gpu && i.Mode == InterruptMode.LineBased))
        {
            findings.Add(new Finding("irq-gpu-line-based", FindingSeverity.Suggestion, "Interrupt",
                $"{gpu.Name}: interrupt line-based (non MSI)",
                "Con gli interrupt MSI ogni dispositivo ha interrupt propri e non condivisi: su molte schede video riduce " +
                "i tempi di gestione. Alcuni driver lo attivano da soli, altri no.",
                "L'attivazione dell'MSI per la scheda video è tra gli interventi proposti (richiede riavvio, con misura e rollback)."));
        }

        var reported = new HashSet<string>();
        foreach (var device in input.Interrupts.Where(i => i.SharesIrqWith.Count > 0))
        {
            var pair = string.Join(" + ", new[] { device.Name }.Concat(device.SharesIrqWith).Order());
            if (!reported.Add(pair))
            {
                continue;
            }

            findings.Add(new Finding("irq-shared", FindingSeverity.Warning, "Interrupt",
                $"IRQ condiviso: {pair}",
                "Dispositivi che condividono lo stesso interrupt line-based si rallentano a vicenda: ogni interrupt " +
                "costringe a interrogare tutti i driver della linea.",
                "Attivare l'MSI su uno dei dispositivi elimina la condivisione (intervento proposto con misura e rollback)."));
        }

        var custom = input.Interrupts.Count(i => i.CustomAffinity);
        if (custom > 0)
        {
            findings.Add(new Finding("irq-custom-affinity", FindingSeverity.Info, "Interrupt",
                $"Affinità degli interrupt personalizzata su {custom} dispositivi",
                "È impostata probabilmente da un tool di ottimizzazione: se mal configurata può concentrare il lavoro su un " +
                "core già occupato dal gioco. L'effetto verrà misurato."));
        }
    }

    private static void AddGaming(Input input, List<Finding> findings)
    {
        var g = input.Gaming;
        if (!g.GameMode)
        {
            findings.Add(new Finding("gamemode-off", FindingSeverity.Suggestion, "Gioco",
                "Modalità gioco disattivata",
                "La modalità gioco riduce le attività in background di Windows (installazione di aggiornamenti, notifiche) mentre giochi.",
                "Attivala in Impostazioni > Giochi > Modalità gioco; l'effetto verrà misurato come per ogni altro intervento."));
        }

        if (g.BackgroundRecording)
        {
            findings.Add(new Finding("gamedvr-background-recording", FindingSeverity.Suggestion, "Gioco",
                "Registrazione in background di Xbox Game Bar attiva",
                "Registra di continuo gli ultimi minuti di gioco: usa encoder video, memoria e disco anche quando non salvi nulla.",
                "Se non la usi, disattiva \"Registra quello che è successo\" in Impostazioni > Giochi > Acquisizioni."));
        }

        if (g.Hags == HagsState.Off && input.Context.IsWindows11)
        {
            findings.Add(new Finding("hags-off", FindingSeverity.Info, "Gioco",
                "Pianificazione GPU con accelerazione hardware (HAGS) disattivata",
                "È necessaria per DLSS Frame Generation sulle GeForce RTX 40 e successive. L'effetto su latenza e frame time " +
                "varia da sistema a sistema e verrà misurato prima di consigliarla."));
        }

        if (g.WindowedOptimizations == false && input.Context.WindowsBuild >= 22621)
        {
            findings.Add(new Finding("windowed-optimizations-off", FindingSeverity.Suggestion, "Gioco",
                "Ottimizzazioni per i giochi in finestra disattivate",
                "Con l'opzione attiva i giochi DirectX 10/11 in finestra o senza bordi usano il modello di presentazione " +
                "\"flip\", con latenza simile allo schermo intero.",
                "Impostazioni > Sistema > Schermo > Grafica > Impostazioni grafiche predefinite."));
        }

        if (g.FsoDisabledApps.Count > 0)
        {
            findings.Add(new Finding("fso-disabled-apps", FindingSeverity.Info, "Gioco",
                $"Ottimizzazioni a schermo intero disattivate per {g.FsoDisabledApps.Count} programmi",
                string.Join(", ", g.FsoDisabledApps.Take(10)) + (g.FsoDisabledApps.Count > 10 ? "..." : "") +
                ". L'effetto di questa opzione cambia da gioco a gioco: va misurato sul titolo specifico."));
        }

        if (g.PowerSavingGpuApps.Count > 0 && input.Context.GpuCount >= 2)
        {
            findings.Add(new Finding("gpu-power-saving-apps", FindingSeverity.Warning, "Gioco",
                $"{g.PowerSavingGpuApps.Count} programmi impostati sulla GPU a risparmio energetico",
                string.Join(", ", g.PowerSavingGpuApps.Take(10)) + ": se tra questi c'è un gioco, gira sulla grafica integrata.",
                "Impostazioni > Sistema > Schermo > Grafica: scegli \"Prestazioni elevate\" per i giochi."));
        }

        if (g.MouseAcceleration == true)
        {
            findings.Add(new Finding("mouse-acceleration", FindingSeverity.Info, "Gioco",
                "\"Precisione puntatore avanzata\" attiva",
                "È un'accelerazione del mouse: la stessa distanza fisica sposta il puntatore in modo diverso secondo la velocità. " +
                "Molti giocatori la disattivano per una mira costante; non influisce sulla latenza.",
                "Impostazioni > Bluetooth e dispositivi > Mouse > Impostazioni aggiuntive del mouse > Opzioni puntatore."));
        }
    }

    private static void AddSecurity(Input input, List<Finding> findings)
    {
        var sec = input.Security;
        if (sec.AntivirusActive == false)
        {
            findings.Add(new Finding("antivirus-none", FindingSeverity.Warning, "Sicurezza",
                "Nessun antivirus attivo",
                "Né Microsoft Defender né un altro antivirus risultano attivi. Spesso è l'effetto di un altro \"ottimizzatore\".",
                "Riattiva la protezione in Sicurezza di Windows > Protezione da virus e minacce; se l'ha disattivata un altro " +
                "programma, controlla cos'altro ha modificato (vedi le impostazioni modificate)."));
        }

        if (sec.WindowsUpdateDisabled == true)
        {
            findings.Add(new Finding("windows-update-disabled", FindingSeverity.Warning, "Sicurezza",
                "Windows Update disattivato",
                "Senza aggiornamenti il sistema non riceve correzioni di sicurezza; nei giochi online è un rischio concreto. " +
                "Disattivarlo non migliora le prestazioni: la modalità gioco già rimanda le installazioni mentre giochi.",
                "Riattiva il servizio Windows Update e rimuovi i criteri che lo bloccano."));
        }

        if (sec.VbsRunning == true)
        {
            findings.Add(new Finding("vbs-running", FindingSeverity.Info, "Sicurezza",
                sec.HvciRunning == true
                    ? "Sicurezza basata su virtualizzazione (VBS) e integrità della memoria attive"
                    : "Sicurezza basata su virtualizzazione (VBS) attiva",
                "Proteggono il kernel da malware e driver malevoli. In alcuni giochi costano qualche punto percentuale di FPS " +
                "(Microsoft lo indica tra le opzioni per i PC da gioco).",
                "PC Optimizer Pro non le modifica da solo: se vuoi disattivarle la sezione Sicurezza spiega rischi e passaggi, " +
                "dopo aver verificato che i tuoi giochi non le richiedano."));
        }

        if (input.Snapshot.IsElevated && sec.TpmVersion == "assente")
        {
            findings.Add(new Finding("tpm-missing", FindingSeverity.Info, "Sicurezza",
                "TPM non rilevato",
                "Alcuni anti-cheat richiedono TPM 2.0 (ad esempio Riot Vanguard su Windows 11).",
                "Se un gioco lo richiede, attiva il TPM del firmware nel BIOS (Intel PTT o AMD fTPM)."));
        }
    }

    private static void AddSoftware(Input input, List<Finding> findings)
    {
        var sw = input.Software;
        var known = sw.Known;

        var antiCheats = known.Where(k => k.Category == SoftwareCategory.AntiCheat).Select(k => k.Name).ToList();
        if (antiCheats.Count > 0)
        {
            findings.Add(new Finding("anticheat-installed", FindingSeverity.Info, "Software",
                $"Anti-cheat installati: {string.Join(", ", antiCheats)}",
                "PC Optimizer Pro non interagisce con gli anti-cheat né con i processi dei giochi: li rileva solo per escluderli."));
        }

        var overlays = known.Where(k => k.Overlay && k.Running).Select(k => k.Name).ToList();
        if (overlays.Count >= 2)
        {
            findings.Add(new Finding("overlays-active", FindingSeverity.Suggestion, "Software",
                $"{overlays.Count} programmi con overlay in esecuzione: {string.Join(", ", overlays)}",
                "Ogni overlay si aggancia al gioco e aggiunge lavoro a ogni frame; più overlay insieme aumentano anche il rischio di conflitti.",
                "Tieni attivo solo l'overlay che usi davvero e disattivalo negli altri programmi."));
        }

        var recording = known.Where(k => k.Category == SoftwareCategory.Recording && k.Running).Select(k => k.Name).ToList();
        if (recording.Count > 0)
        {
            findings.Add(new Finding("recording-active", FindingSeverity.Suggestion, "Software",
                $"Software di registrazione in esecuzione: {string.Join(", ", recording)}",
                "La registrazione continua usa l'encoder della GPU, memoria e disco anche mentre non salvi clip.",
                "Chiudilo quando giochi in competitivo, se non ti serve."));
        }

        var optimizers = known.Where(k => k.Risk == SoftwareRisk.Conflict && (k.Running || k.FoundIn.Any(f => f == "avvio automatico"))).ToList();
        if (optimizers.Count > 0)
        {
            findings.Add(new Finding("other-optimizers", FindingSeverity.Warning, "Software",
                $"Altri ottimizzatori attivi: {string.Join(", ", optimizers.Select(o => o.Name))}",
                "Applicano modifiche proprie mentre il sistema è in uso: si sovrappongono agli interventi di questo programma " +
                "e rendono inaffidabili le misure prima/dopo.",
                "Chiudili (e toglili dall'avvio automatico) prima di misurare e ottimizzare."));
        }

        foreach (var risky in known.Where(k => k.Risk is SoftwareRisk.Latency or SoftwareRisk.Stability))
        {
            findings.Add(new Finding($"software-{risky.Id}", FindingSeverity.Warning, "Software",
                $"{risky.Name} rilevato",
                $"{risky.Note} Trovato in: {string.Join(", ", risky.FoundIn)}.",
                "Se non ti serve, disinstallalo o disattivane il servizio; le misure prima/dopo mostreranno l'effetto."));
        }

        foreach (var driver in sw.VulnerableDrivers)
        {
            var entry = known.FirstOrDefault(k => k.Risk == SoftwareRisk.Vulnerable && driver.Contains(k.Name, StringComparison.Ordinal));
            findings.Add(new Finding($"vulnerable-driver-{driver.Split(' ')[0]}", FindingSeverity.Warning, "Sicurezza",
                $"Driver con vulnerabilità nota: {driver}",
                entry?.Note ?? "Il driver compare negli elenchi di driver vulnerabili usati per attacchi al kernel.",
                "Aggiorna o disinstalla il programma che lo ha installato; con l'integrità della memoria attiva Windows lo blocca."));
        }

        if (sw.UnsignedDrivers.Count > 0)
        {
            findings.Add(new Finding("unsigned-drivers", FindingSeverity.Info, "Software",
                $"{sw.UnsignedDrivers.Count} driver senza firma digitale",
                string.Join(", ", sw.UnsignedDrivers.Take(10)) + ". I driver non firmati non sono verificati da Microsoft.",
                "Preferisci i driver ufficiali del produttore del dispositivo."));
        }

        var thirdPartyStartup = input.Snapshot.Startup.Count(s => s.Enabled && !(s.Company?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ?? false));
        if (thirdPartyStartup > 8)
        {
            var heavy = known
                .Where(k => k.FoundIn.Contains("avvio automatico") && k.Category is SoftwareCategory.Launcher or SoftwareCategory.Chat
                    or SoftwareCategory.Rgb or SoftwareCategory.Cloud or SoftwareCategory.Media or SoftwareCategory.Updater)
                .Select(k => k.Name)
                .ToList();
            findings.Add(new Finding("startup-many", FindingSeverity.Suggestion, "Software",
                $"{thirdPartyStartup} programmi di terze parti all'avvio",
                (heavy.Count > 0 ? $"Tra questi: {string.Join(", ", heavy)}. " : "") +
                "Ogni programma all'avvio rallenta l'accensione e resta in memoria mentre giochi.",
                "La gestione delle app in avvio è tra gli interventi proposti (con ripristino per singola voce)."));
        }

        var ram = input.Context.TotalRamBytes;
        var lowTier = input.Context.Tier == HardwareTier.Low;
        var threshold = Math.Max(lowTier ? OneGiB : 1.5 * OneGiB, ram * (lowTier ? 0.10 : 0.15));
        if (sw.BackgroundPrivateBytes > threshold)
        {
            var top = known
                .Where(k => k.Running && k.PrivateBytes > 0)
                .OrderByDescending(k => k.PrivateBytes)
                .Take(5)
                .Select(k => $"{k.Name} {ScoreFmt(k.PrivateBytes / OneGiB, "0.0")} GB");
            findings.Add(new Finding("background-memory", lowTier ? FindingSeverity.Warning : FindingSeverity.Suggestion, "Software",
                $"Programmi in background: {ScoreFmt(sw.BackgroundPrivateBytes / OneGiB, "0.0")} GB di RAM",
                $"I più pesanti: {string.Join(", ", top)}.",
                "Chiudi browser, launcher e app di chat che non ti servono mentre giochi" +
                (lowTier ? ": con poca RAM fa la differenza tra un gioco fluido e uno a scatti." : ".")));
        }

        if (sw.PageFileEnabled == false)
        {
            findings.Add(new Finding("pagefile-disabled", FindingSeverity.Warning, "Sistema",
                "File di paging disattivato",
                "Senza file di paging, quando la RAM si esaurisce giochi e programmi si chiudono di colpo. " +
                "Disattivarlo non rende il sistema più veloce.",
                "Riattivalo con la gestione automatica (Proprietà del sistema > Prestazioni > Avanzate > Memoria virtuale)."));
        }
    }

    private static void AddModified(Input input, List<Finding> findings)
    {
        var modified = input.Modified;
        if (modified.Count == 0)
        {
            return;
        }

        // Le modifiche dannose già spiegate da un'osservazione dedicata non si ripetono.
        foreach (var harmful in modified.Where(m => m.Effect == TweakEffect.Harmful && !m.Id.StartsWith("bcd.", StringComparison.Ordinal)))
        {
            findings.Add(new Finding($"modified-harmful-{harmful.Id}", FindingSeverity.Warning, "Sistema",
                $"Impostazione sfavorevole: {harmful.Title}",
                $"{harmful.Note} Valore attuale: {harmful.Current}, predefinito: {harmful.Default}.",
                "Il ripristino del valore predefinito è tra gli interventi proposti."));
        }

        var placebo = modified.Count(m => m.Effect == TweakEffect.Placebo);
        findings.Add(new Finding("modified-settings", FindingSeverity.Info, "Sistema",
            $"{modified.Count} impostazioni diverse dai valori predefiniti di Windows",
            string.Join("; ", modified.Select(m => m.Title)) + "." +
            placebo switch
            {
                0 => "",
                1 => " Una di queste non ha alcun effetto (placebo).",
                _ => $" {placebo} di queste non hanno alcun effetto (placebo).",
            },
            "Sono probabilmente il risultato di tweak o di altri ottimizzatori: ognuna verrà valutata con misure reali " +
            "e potrai ripristinare il valore predefinito."));
    }

    private static void AddCollection(Input input, List<Finding> findings)
    {
        var s = input.Snapshot;
        if (!s.IsElevated)
        {
            findings.Add(new Finding("analysis-not-elevated", FindingSeverity.Info, "Sistema",
                "Analisi eseguita senza privilegi di amministratore",
                "Alcune informazioni (configurazione di avvio, TPM, parte delle attività pianificate) non sono disponibili.",
                "Avvia l'analisi come amministratore per il quadro completo."));
        }

        if (s.Errors.Count > 0)
        {
            findings.Add(new Finding("analysis-partial", FindingSeverity.Info, "Sistema",
                "Alcune parti dell'analisi non sono state completate",
                string.Join("; ", s.Errors),
                "La valutazione usa i dati disponibili; i dettagli sono nel log."));
        }
    }

    private static string ScoreFmt(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture).Replace('.', ',');
}
