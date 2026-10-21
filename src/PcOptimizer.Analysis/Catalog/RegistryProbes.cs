using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Analysis.Catalog;

/// <summary>Valore di registro da leggere, con un identificativo stabile usato dalla valutazione.</summary>
public sealed record RegistryProbe(string Id, RegistryValueTarget Target);

/// <summary>
/// Impostazione spesso modificata da tweak e ottimizzatori, con i valori predefiniti di Windows.
/// <c>null</c> in <see cref="Defaults"/> significa "valore assente" (il default per molte chiavi).
/// </summary>
public sealed record KnownTweak(RegistryProbe Probe, string Title, IReadOnlyList<string?> Defaults, TweakEffect Effect, string Note)
{
    public bool IsDefault(SettingValue value) => Defaults.Any(d => d is null
        ? !value.Exists
        : value.Exists && string.Equals(value.Data, d, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Catalogo delle chiavi di registro lette dall'analisi. Gli stessi target serviranno agli
/// interventi (modulo 5) per scrivere con rollback, così lettura e scrittura non divergono.
/// </summary>
public static class RegistryProbes
{
    private const string GameConfigStore = @"System\GameConfigStore";
    private const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string SystemProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GamesTask = SystemProfile + @"\Tasks\Games";
    private const string PriorityControl = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string MemoryManagement = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";

    // --- Impostazioni di gioco ---
    public static readonly RegistryProbe GameMode = User("gaming.gamemode", @"Software\Microsoft\GameBar", "AutoGameModeEnabled");
    public static readonly RegistryProbe GameDvrEnabled = User("gaming.gamedvr.enabled", GameConfigStore, "GameDVR_Enabled");
    public static readonly RegistryProbe AppCapture = User("gaming.gamedvr.appcapture", GameDvr, "AppCaptureEnabled");
    public static readonly RegistryProbe HistoricalCapture = User("gaming.gamedvr.historical", GameDvr, "HistoricalCaptureEnabled");
    public static readonly RegistryProbe GameDvrPolicy = Machine("gaming.gamedvr.policy", @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR");
    public static readonly RegistryProbe FseBehavior = User("gaming.fse.behavior", GameConfigStore, "GameDVR_FSEBehavior");
    public static readonly RegistryProbe FseHonorUser = User("gaming.fse.honoruser", GameConfigStore, "GameDVR_HonorUserFSEBehaviorMode");
    public static readonly RegistryProbe DirectXGlobal = User("gaming.directx.global", @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", RegValueKind.String);
    public static readonly RegistryProbe HwSchMode = Machine("gaming.hags", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
    public static readonly RegistryProbe MouseSpeed = User("input.mousespeed", @"Control Panel\Mouse", "MouseSpeed", RegValueKind.String);

    // --- Alimentazione ---
    public static readonly RegistryProbe OverlayAc = Machine("power.overlay.ac", @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", "ActiveOverlayAcPowerScheme", RegValueKind.String);
    public static readonly RegistryProbe FastStartup = Machine("power.faststartup", @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled");

    // --- Sicurezza e aggiornamenti ---
    public static readonly RegistryProbe HvciEnabled = Machine("security.hvci", @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
    public static readonly RegistryProbe NoAutoUpdate = Machine("update.noautoupdate", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate");
    public static readonly RegistryProbe DisableWindowsUpdateAccess = Machine("update.disableaccess", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DisableWindowsUpdateAccess");

    // --- App in background ed effetti visivi ---
    public static readonly RegistryProbe BackgroundAppsUser = User("background.user", @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled");
    public static readonly RegistryProbe BackgroundAppsPolicy = Machine("background.policy", @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground");
    public static readonly RegistryProbe VisualEffects = User("visual.fx", @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting");
    public static readonly RegistryProbe Transparency = User("visual.transparency", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency");

    /// <summary>Modifiche note: confrontate con i valori predefiniti di Windows.</summary>
    public static IReadOnlyList<KnownTweak> KnownTweaks { get; } =
    [
        new(Machine("mmcss.networkthrottling", SystemProfile, "NetworkThrottlingIndex"),
            "Limitazione rete MMCSS (NetworkThrottlingIndex)", ["10"], TweakEffect.Debatable,
            "Limita i pacchetti di rete non multimediali mentre è attiva la riproduzione multimediale. Il valore " +
            "0xFFFFFFFF dei tweak toglie il limite: effetto sui giochi non dimostrato."),
        new(Machine("mmcss.systemresponsiveness", SystemProfile, "SystemResponsiveness"),
            "Riserva CPU per i processi in background (SystemResponsiveness)", ["20"], TweakEffect.Debatable,
            "Quota di CPU garantita ai processi a bassa priorità quando MMCSS è attivo; agisce solo sulle app che usano MMCSS."),
        new(Machine("mmcss.games.priority", GamesTask, "Priority"),
            "Priorità MMCSS della classe Giochi", ["2"], TweakEffect.Debatable,
            "Vale solo per i thread che si registrano a MMCSS come 'Games': pochi giochi lo fanno."),
        new(Machine("mmcss.games.gpupriority", GamesTask, "GPU Priority"),
            "Priorità GPU MMCSS della classe Giochi", ["8"], TweakEffect.Debatable,
            "Vale solo per i thread registrati a MMCSS come 'Games'."),
        new(Machine("mmcss.games.category", GamesTask, "Scheduling Category", RegValueKind.String),
            "Categoria di pianificazione MMCSS della classe Giochi", ["Medium"], TweakEffect.Debatable,
            "Vale solo per i thread registrati a MMCSS come 'Games'."),
        new(Machine("mmcss.games.sfio", GamesTask, "SFIO Priority", RegValueKind.String),
            "Priorità I/O MMCSS della classe Giochi", ["Normal"], TweakEffect.Debatable,
            "Vale solo per i thread registrati a MMCSS come 'Games'."),
        new(Machine("scheduler.priorityseparation", PriorityControl, "Win32PrioritySeparation"),
            "Ripartizione della CPU tra primo piano e background (Win32PrioritySeparation)", ["2", "38", "24"], TweakEffect.Debatable,
            "2 e 38 corrispondono a 'Programmi', 24 a 'Servizi in background' (Proprietà del sistema > Prestazioni). " +
            "Altri valori sono personalizzazioni da misurare."),
        new(Machine("placebo.irq8priority", PriorityControl, "IRQ8Priority"),
            "IRQ8Priority", [null], TweakEffect.Placebo,
            "Valore ignorato dalle versioni di Windows basate su NT: non ha alcun effetto."),
        new(Machine("placebo.irq16priority", PriorityControl, "IRQ16Priority"),
            "IRQ16Priority", [null], TweakEffect.Placebo,
            "Valore ignorato dalle versioni di Windows basate su NT: non ha alcun effetto."),
        new(Machine("memory.pagingexecutive", MemoryManagement, "DisablePagingExecutive"),
            "Kernel e driver sempre in RAM (DisablePagingExecutive)", [null, "0"], TweakEffect.Debatable,
            "Con RAM sufficiente Windows non sposta comunque il codice del kernel su disco: effetto trascurabile."),
        new(Machine("memory.largesystemcache", MemoryManagement, "LargeSystemCache"),
            "Cache di sistema estesa (LargeSystemCache)", [null, "0"], TweakEffect.Harmful,
            "Pensata per i file server: su un PC da gioco sottrae memoria alle applicazioni."),
        new(Machine("memory.clearpagefile", MemoryManagement, "ClearPageFileAtShutdown"),
            "Svuotamento del file di paging allo spegnimento", [null, "0"], TweakEffect.Other,
            "Impostazione di sicurezza, non di prestazioni: rende più lento lo spegnimento."),
        new(Machine("memory.prefetcher", MemoryManagement + @"\PrefetchParameters", "EnablePrefetcher"),
            "Prefetch dei programmi (EnablePrefetcher)", [null, "3"], TweakEffect.Debatable,
            "Accelera l'avvio dei programmi usati spesso; disattivarlo ha senso solo in casi particolari."),
        new(Machine("services.svchostsplit", @"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB"),
            "Raggruppamento dei servizi svchost", [null, "3670016"], TweakEffect.Debatable,
            "Raggruppare i servizi risparmia poca RAM, ma un servizio bloccato può trascinare con sé gli altri."),
        new(Machine("input.mousequeue", @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize"),
            "Coda eventi del mouse (MouseDataQueueSize)", [null, "100"], TweakEffect.Placebo,
            "È un buffer che in uso normale non si riempie: ridurlo non abbassa la latenza e valori troppo bassi possono perdere input."),
        new(Machine("input.keyboardqueue", @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "KeyboardDataQueueSize"),
            "Coda eventi della tastiera (KeyboardDataQueueSize)", [null, "100"], TweakEffect.Placebo,
            "È un buffer che in uso normale non si riempie: ridurlo non abbassa la latenza e valori troppo bassi possono perdere input."),
        new(Machine("network.taskoffload", @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "DisableTaskOffload"),
            "Offload TCP disattivato (DisableTaskOffload)", [null, "0"], TweakEffect.Debatable,
            "Sposta sulla CPU calcoli che la scheda di rete farebbe da sola."),
        new(Machine("network.qosreserve", @"SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit"),
            "Banda riservata QoS (NonBestEffortLimit)", [null], TweakEffect.Placebo,
            "Il '20% di banda riservata' è un mito: Windows non riserva banda se nessuna applicazione usa QoS."),
        new(Machine("timer.globalrequests", @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests"),
            "Risoluzione timer globale (GlobalTimerResolutionRequests)", [null, "0"], TweakEffect.Debatable,
            "Su Windows 11 fa valere per tutto il sistema la risoluzione del timer chiesta da una singola app: aumenta i consumi, " +
            "effetto sulla latenza da misurare."),
        new(Machine("graphics.mpo", @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode"),
            "Multiplane Overlay disattivato (OverlayTestMode)", [null], TweakEffect.Other,
            "Workaround noto per sfarfallii o scatti con alcune GPU: da lasciare solo se risolve un problema reale."),
        new(Machine("power.throttlingoff", @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff"),
            "Power Throttling disattivato", [null, "0"], TweakEffect.Debatable,
            "Windows non limita comunque il gioco in primo piano: disattivarlo per tutto aumenta soprattutto i consumi."),
        new(Machine("privacy.telemetrypolicy", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry"),
            "Criterio di telemetria", [null], TweakEffect.Other,
            "Riguarda la privacy, non le prestazioni."),
    ];

    /// <summary>Tutte le sonde da leggere (impostazioni e modifiche note), senza duplicati.</summary>
    public static IReadOnlyList<RegistryProbe> All { get; } =
        new[]
        {
            GameMode, GameDvrEnabled, AppCapture, HistoricalCapture, GameDvrPolicy, FseBehavior, FseHonorUser,
            DirectXGlobal, HwSchMode, MouseSpeed, OverlayAc, FastStartup, HvciEnabled, NoAutoUpdate,
            DisableWindowsUpdateAccess, BackgroundAppsUser, BackgroundAppsPolicy, VisualEffects, Transparency,
        }
        .Concat(KnownTweaks.Select(t => t.Probe))
        .DistinctBy(p => p.Id)
        .ToList();

    private static RegistryProbe User(string id, string key, string value, RegValueKind kind = RegValueKind.DWord) =>
        new(id, new RegistryValueTarget(RegHive.CurrentUser, key, value, kind));

    private static RegistryProbe Machine(string id, string key, string value, RegValueKind kind = RegValueKind.DWord) =>
        new(id, new RegistryValueTarget(RegHive.LocalMachine, key, value, kind));
}
