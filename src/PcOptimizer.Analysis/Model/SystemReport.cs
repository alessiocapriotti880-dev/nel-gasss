using PcOptimizer.Core.Model;

namespace PcOptimizer.Analysis.Model;

// INTERPRETAZIONE della configurazione: riepiloghi per la dashboard, modifiche rispetto ai
// default di Windows e osservazioni. Prodotta da SystemAnalyzer, pura e testabile.

public enum PowerPlanKind
{
    Unknown,
    PowerSaver,
    Balanced,
    HighPerformance,
    UltimatePerformance,
    Custom,
}

public sealed record PowerReport
{
    public PowerPlanKind Kind { get; init; }

    public string? Name { get; init; }

    public bool HighPerformanceAvailable { get; init; }

    public bool UltimatePerformanceAvailable { get; init; }

    /// <summary>Modalità di alimentazione (cursore) attiva sul piano Bilanciato, se nota.</summary>
    public string? OverlayMode { get; init; }

    public uint? MinProcessorPercent { get; init; }

    public uint? MaxProcessorPercent { get; init; }

    public uint? CoreParkingMinCoresPercent { get; init; }

    /// <summary>Modalità turbo (0 = disattivato, 1 = attivato, 2 = aggressivo...).</summary>
    public uint? BoostMode { get; init; }

    /// <summary>True se gli stati di inattività della CPU (C-state) sono disattivati.</summary>
    public bool? IdleDisabled { get; init; }

    public bool? UsbSelectiveSuspend { get; init; }

    /// <summary>Risparmio energia del collegamento PCI Express: 0 off, 1 moderato, 2 massimo.</summary>
    public uint? PcieAspm { get; init; }
}

public sealed record TimerReport
{
    public double? CurrentMs { get; init; }

    public double? FinestMs { get; init; }

    public bool GlobalResolutionRequests { get; init; }

    public bool BcdReadable { get; init; }

    /// <summary>HPET forzato come sorgente del tempo (useplatformclock).</summary>
    public bool? UsePlatformClock { get; init; }

    public bool? UsePlatformTick { get; init; }

    public bool? DisableDynamicTick { get; init; }

    public string? TscSyncPolicy { get; init; }

    public bool? HpetDeviceEnabled { get; init; }
}

public enum DeviceCategory
{
    Other,
    Gpu,
    Network,
    Audio,
    Usb,
    Storage,
}

public enum InterruptMode
{
    Unknown,
    LineBased,
    Msi,
}

public sealed record InterruptReport
{
    public required string Name { get; init; }

    public DeviceCategory Category { get; init; }

    public InterruptMode Mode { get; init; }

    /// <summary>Valore MSISupported nel registro (null = deciso dal driver).</summary>
    public bool? MsiSupportedSetting { get; init; }

    /// <summary>Affinità o priorità degli interrupt modificate rispetto al predefinito.</summary>
    public bool CustomAffinity { get; init; }

    /// <summary>Dispositivi con cui condivide un IRQ line-based.</summary>
    public IReadOnlyList<string> SharesIrqWith { get; init; } = [];
}

public enum HagsState
{
    Unknown,
    NotSupported,
    Off,
    On,
}

public sealed record GamingReport
{
    /// <summary>Modalità gioco attiva (predefinito: sì).</summary>
    public bool GameMode { get; init; }

    public HagsState Hags { get; init; }

    public bool? HagsEnabledByDefault { get; init; }

    /// <summary>Registrazione in background di Xbox Game Bar ("Registra quello che è successo").</summary>
    public bool BackgroundRecording { get; init; }

    public bool GameDvr { get; init; }

    /// <summary>Firma del tweak che disattiva globalmente le ottimizzazioni a schermo intero.</summary>
    public bool FsoGloballyDisabled { get; init; }

    public IReadOnlyList<string> FsoDisabledApps { get; init; } = [];

    /// <summary>"Ottimizzazioni per i giochi in finestra" (null = valore predefinito del sistema).</summary>
    public bool? WindowedOptimizations { get; init; }

    public bool? VrrOptimizations { get; init; }

    /// <summary>App forzate sulla GPU a risparmio energetico (rilevante sui portatili con due GPU).</summary>
    public IReadOnlyList<string> PowerSavingGpuApps { get; init; } = [];

    /// <summary>Multiplane Overlay disattivato (OverlayTestMode = 5).</summary>
    public bool MpoDisabled { get; init; }

    /// <summary>"Precisione puntatore avanzata" (accelerazione del mouse).</summary>
    public bool? MouseAcceleration { get; init; }
}

public sealed record SecurityReport
{
    public bool? VbsRunning { get; init; }

    public bool? HvciRunning { get; init; }

    public bool? HypervisorPresent { get; init; }

    /// <summary>Almeno un antivirus attivo (Defender o di terze parti), null se non determinabile.</summary>
    public bool? AntivirusActive { get; init; }

    public IReadOnlyList<string> ActiveAntivirus { get; init; } = [];

    public bool? WindowsUpdateDisabled { get; init; }

    public string? TpmVersion { get; init; }
}

public enum SoftwareCategory
{
    Other,
    AntiCheat,
    Security,
    Overlay,
    Recording,
    Launcher,
    Chat,
    Rgb,
    Monitoring,
    Audio,
    Network,
    Updater,
    Cloud,
    Media,
    Optimizer,
    Telemetry,
    Xbox,
    Browser,
}

public enum SoftwareRisk
{
    None,
    /// <summary>Può aumentare latenza o jitter.</summary>
    Latency,
    /// <summary>Segnalato per crash o problemi di compatibilità con i giochi.</summary>
    Stability,
    /// <summary>Installa un driver con vulnerabilità nota.</summary>
    Vulnerable,
    /// <summary>Altro ottimizzatore: modifiche sovrapposte e misure falsate.</summary>
    Conflict,
}

/// <summary>Software riconosciuto nel sistema, con i punti in cui è stato trovato.</summary>
public sealed record SoftwareMatch
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public SoftwareCategory Category { get; init; }

    public SoftwareRisk Risk { get; init; }

    public bool Overlay { get; init; }

    /// <summary>Software da non toccare mai (anti-cheat, sicurezza).</summary>
    public bool NeverTouch { get; init; }

    public string? Note { get; init; }

    /// <summary>Dove è stato trovato: "processo Discord.exe", "avvio automatico", "servizio X".</summary>
    public IReadOnlyList<string> FoundIn { get; init; } = [];

    public bool Running { get; init; }

    /// <summary>Memoria privata dei suoi processi in esecuzione.</summary>
    public long PrivateBytes { get; init; }
}

public sealed record SoftwareReport
{
    public int ServicesTotal { get; init; }

    public int ServicesRunning { get; init; }

    public int ThirdPartyServicesRunning { get; init; }

    public int StartupEnabled { get; init; }

    public int ScheduledTasksEnabled { get; init; }

    public int ThirdPartyTasksEnabled { get; init; }

    public IReadOnlyList<SoftwareMatch> Known { get; init; } = [];

    /// <summary>Memoria privata dei programmi riconosciuti come "in background" (launcher, chat, RGB...).</summary>
    public long BackgroundPrivateBytes { get; init; }

    public IReadOnlyList<string> VulnerableDrivers { get; init; } = [];

    public IReadOnlyList<string> UnsignedDrivers { get; init; } = [];

    /// <summary>False se Windows non ha alcun file di paging attivo.</summary>
    public bool? PageFileEnabled { get; init; }
}

public enum TweakEffect
{
    /// <summary>Nessun effetto: Windows ignora il valore.</summary>
    Placebo,
    /// <summary>Effetto non dimostrato o dipendente dal sistema: va misurato.</summary>
    Debatable,
    /// <summary>Di norma peggiora prestazioni o stabilità su un PC da gioco.</summary>
    Harmful,
    /// <summary>Non riguarda le prestazioni (privacy, sicurezza, workaround specifici).</summary>
    Other,
}

/// <summary>Impostazione diversa dal valore predefinito di Windows (spesso lasciata da tweak o altri ottimizzatori).</summary>
public sealed record ModifiedSetting(string Id, string Title, string Current, string Default, TweakEffect Effect, string Note);

public sealed record SystemReport
{
    public required PowerReport Power { get; init; }

    public required TimerReport Timer { get; init; }

    public required IReadOnlyList<InterruptReport> Interrupts { get; init; }

    public required GamingReport Gaming { get; init; }

    public required SecurityReport Security { get; init; }

    public required SoftwareReport Software { get; init; }

    public required IReadOnlyList<ModifiedSetting> ModifiedSettings { get; init; }

    public required IReadOnlyList<Finding> Findings { get; init; }
}
