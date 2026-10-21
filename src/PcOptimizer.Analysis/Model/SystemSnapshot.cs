using PcOptimizer.Core.Settings;

namespace PcOptimizer.Analysis.Model;

// Dati GREZZI della configurazione di Windows, raccolti in sola lettura.
// L'interpretazione (osservazioni, modifiche rispetto ai default) sta in SystemReport.

public sealed record ServiceEntry
{
    public string Name { get; init; } = "";

    public string DisplayName { get; init; } = "";

    /// <summary>Tipo di avvio come in <see cref="ServiceStartValues"/> (Automatic, AutomaticDelayed, Manual, Disabled...).</summary>
    public string StartMode { get; init; } = "";

    /// <summary>Stato WMI: Running, Stopped, Start Pending...</summary>
    public string State { get; init; } = "";

    /// <summary>File eseguibile (o DLL per i servizi ospitati da svchost).</summary>
    public string? ImagePath { get; init; }

    /// <summary>Produttore dichiarato nelle informazioni di versione del file.</summary>
    public string? Company { get; init; }

    public bool IsRunning => State.Equals("Running", StringComparison.OrdinalIgnoreCase);

    public bool IsMicrosoft => Company?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true;
}

public enum StartupSource
{
    /// <summary>HKLM\...\Run (tutti gli utenti, 64 bit).</summary>
    RegistryMachine,
    /// <summary>HKLM\...\WOW6432Node\...\Run (tutti gli utenti, 32 bit).</summary>
    RegistryMachine32,
    /// <summary>HKCU\...\Run (utente corrente).</summary>
    RegistryUser,
    /// <summary>Cartella Esecuzione automatica comune.</summary>
    StartupFolderCommon,
    /// <summary>Cartella Esecuzione automatica dell'utente.</summary>
    StartupFolderUser,
    /// <summary>App dello Store con attività di avvio.</summary>
    StoreApp,
}

public sealed record StartupEntry
{
    public string Name { get; init; } = "";

    public StartupSource Source { get; init; }

    public string? Command { get; init; }

    public string? ExecutablePath { get; init; }

    public string? Company { get; init; }

    /// <summary>Stato come in Gestione attività (StartupApproved).</summary>
    public bool Enabled { get; init; }
}

public sealed record ScheduledTaskEntry
{
    /// <summary>Percorso completo, es. "\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser".</summary>
    public string Path { get; init; } = "";

    public bool Enabled { get; init; }

    /// <summary>TASK_STATE: 1 disabilitata, 2 in coda, 3 pronta, 4 in esecuzione.</summary>
    public int State { get; init; }

    /// <summary>Tipi di trigger (Avvio, Accesso, Inattività, Pianificazione, Evento...).</summary>
    public IReadOnlyList<string> Triggers { get; init; } = [];

    public string? Executable { get; init; }

    public string? Author { get; init; }

    public bool IsMicrosoft => Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);
}

public sealed record KernelDriverEntry
{
    public string Name { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string? FilePath { get; init; }

    public string State { get; init; } = "";

    public string StartMode { get; init; } = "";

    public string? Company { get; init; }

    public bool IsRunning => State.Equals("Running", StringComparison.OrdinalIgnoreCase);

    /// <summary>Nome del file del driver (i percorsi sono in formato Windows su qualsiasi sistema).</summary>
    public string FileName => FilePath is null ? Name : FilePath[(FilePath.LastIndexOfAny(['\\', '/']) + 1)..];
}

/// <summary>Driver di un dispositivo (Win32_PnPSignedDriver): versione, data, firma.</summary>
public sealed record DeviceDriverEntry
{
    public string DeviceName { get; init; } = "";

    /// <summary>Classe del dispositivo: NET, MEDIA, DISPLAY...</summary>
    public string DeviceClass { get; init; } = "";

    public string? Version { get; init; }

    public DateTime? Date { get; init; }

    public string? Provider { get; init; }

    public bool? IsSigned { get; init; }
}

/// <summary>Processo in esecuzione: solo dati dell'istantanea di sistema, nessun handle aperto sul processo.</summary>
public sealed record ProcessEntry
{
    public string Name { get; init; } = "";

    public int Id { get; init; }

    public int SessionId { get; init; }

    public long WorkingSetBytes { get; init; }

    public long PrivateBytes { get; init; }
}

public sealed record PowerSchemeEntry(Guid Id, string Name);

/// <summary>Valore di un'impostazione energetica del piano attivo (null se non presente).</summary>
public sealed record PowerValue(string Key, uint? Ac, uint? Dc);

public sealed record PowerSnapshot
{
    public Guid? ActiveScheme { get; init; }

    public string? ActiveSchemeName { get; init; }

    public IReadOnlyList<PowerSchemeEntry> Schemes { get; init; } = [];

    public IReadOnlyList<PowerValue> Values { get; init; } = [];

    public uint? Ac(string key) => Values.FirstOrDefault(v => v.Key == key)?.Ac;
}

public sealed record TimerSnapshot
{
    /// <summary>Risoluzione attuale del timer di sistema in millisecondi.</summary>
    public double? CurrentMs { get; init; }

    /// <summary>Risoluzione più fine supportata (es. 0,5 ms).</summary>
    public double? FinestMs { get; init; }

    /// <summary>Risoluzione predefinita più grossolana (es. 15,625 ms).</summary>
    public double? CoarsestMs { get; init; }

    /// <summary>True se la configurazione di avvio (bcdedit) è stata letta (richiede amministratore).</summary>
    public bool BcdReadable { get; init; }

    /// <summary>Elementi BCD rilevanti per il timer, con il valore così come lo stampa bcdedit.</summary>
    public IReadOnlyDictionary<string, string> BcdElements { get; init; } = new Dictionary<string, string>();

    /// <summary>Stato del dispositivo HPET in Gestione dispositivi: null se assente.</summary>
    public bool? HpetDeviceEnabled { get; init; }
}

/// <summary>Dispositivo PCI con configurazione degli interrupt.</summary>
public sealed record PciDeviceEntry
{
    public string Name { get; init; } = "";

    public string PnpDeviceId { get; init; } = "";

    /// <summary>GUID della classe del dispositivo (Display, Net, Media, USB...).</summary>
    public Guid? ClassGuid { get; init; }

    /// <summary>Valore MSISupported nel registro (null se assente: decide il driver).</summary>
    public bool? MsiSupported { get; init; }

    public int? MessageNumberLimit { get; init; }

    /// <summary>IRQ assegnati (valori a 32 bit senza segno: dal 2^31 in su sono interrupt MSI).</summary>
    public IReadOnlyList<long> Irqs { get; init; } = [];

    /// <summary>Criterio di affinità (Affinity Policy\DevicePolicy), null se predefinito.</summary>
    public int? AffinityPolicy { get; init; }

    public int? DevicePriority { get; init; }

    public ulong? AffinityMask { get; init; }
}

/// <summary>Stato della pianificazione GPU con accelerazione hardware (HAGS) letto dal driver.</summary>
public sealed record GpuSchedulingEntry
{
    public string AdapterName { get; init; } = "";

    public bool HwSchSupported { get; init; }

    public bool HwSchEnabled { get; init; }

    public bool HwSchEnabledByDefault { get; init; }

    /// <summary>Versione WDDM del driver (es. 3100 = WDDM 3.1).</summary>
    public int? WddmVersion { get; init; }
}

/// <summary>Preferenza GPU per app (Impostazioni > Schermo > Grafica): 1 = risparmio energia, 2 = prestazioni elevate.</summary>
public sealed record GpuPreferenceEntry(string Application, int Preference);

/// <summary>Impostazioni TCP per interfaccia (Nagle): presenti solo se modificate.</summary>
public sealed record TcpInterfaceEntry(string InterfaceId, uint? TcpAckFrequency, uint? TcpNoDelay, uint? TcpDelAckTicks);

public sealed record AntivirusEntry(string Name, bool Enabled);

public sealed record SecuritySnapshot
{
    /// <summary>VirtualizationBasedSecurityStatus: 0 non attiva, 1 configurata ma non in esecuzione, 2 in esecuzione.</summary>
    public int? VbsStatus { get; init; }

    /// <summary>Servizi di sicurezza in esecuzione: 1 Credential Guard, 2 HVCI (integrità della memoria), 3 System Guard...</summary>
    public IReadOnlyList<long> SecurityServicesRunning { get; init; } = [];

    public bool? HypervisorPresent { get; init; }

    public bool? DefenderRealTimeProtection { get; init; }

    public bool? DefenderAntivirusEnabled { get; init; }

    public bool? DefenderTamperProtected { get; init; }

    /// <summary>Modalità di Defender: "Normal", "Passive" (altro antivirus attivo), ecc.</summary>
    public string? DefenderMode { get; init; }

    public IReadOnlyList<AntivirusEntry> AntivirusProducts { get; init; } = [];

    public bool? TpmPresent { get; init; }

    public string? TpmSpecVersion { get; init; }

    public bool? TpmEnabled { get; init; }
}

public sealed record PageFileEntry(string Path, int AllocatedMb);

public sealed record PageFileSnapshot
{
    public bool? AutomaticManaged { get; init; }

    /// <summary>File di paging attivi.</summary>
    public IReadOnlyList<PageFileEntry> Files { get; init; } = [];
}

/// <summary>Istantanea completa della configurazione del sistema.</summary>
public sealed record SystemSnapshot
{
    public DateTimeOffset CollectedAt { get; init; }

    /// <summary>Raccolta eseguita come amministratore (alcune informazioni lo richiedono).</summary>
    public bool IsElevated { get; init; }

    public IReadOnlyList<ServiceEntry> Services { get; init; } = [];

    public IReadOnlyList<StartupEntry> Startup { get; init; } = [];

    public IReadOnlyList<ScheduledTaskEntry> ScheduledTasks { get; init; } = [];

    public IReadOnlyList<KernelDriverEntry> KernelDrivers { get; init; } = [];

    public IReadOnlyList<DeviceDriverEntry> DeviceDrivers { get; init; } = [];

    public IReadOnlyList<ProcessEntry> Processes { get; init; } = [];

    public PowerSnapshot Power { get; init; } = new();

    public TimerSnapshot Timer { get; init; } = new();

    public IReadOnlyList<PciDeviceEntry> PciDevices { get; init; } = [];

    public IReadOnlyList<GpuSchedulingEntry> GpuScheduling { get; init; } = [];

    /// <summary>Valori di registro letti, per identificativo di sonda (vedi <c>RegistryProbes</c>).</summary>
    public IReadOnlyDictionary<string, SettingValue> Registry { get; init; } = new Dictionary<string, SettingValue>();

    /// <summary>Eseguibili con "Disattiva ottimizzazioni a schermo intero" nelle proprietà di compatibilità.</summary>
    public IReadOnlyList<string> FsoDisabledApps { get; init; } = [];

    public IReadOnlyList<GpuPreferenceEntry> GpuPreferences { get; init; } = [];

    public IReadOnlyList<TcpInterfaceEntry> TcpInterfaces { get; init; } = [];

    public SecuritySnapshot Security { get; init; } = new();

    public PageFileSnapshot PageFile { get; init; } = new();

    /// <summary>Parti non raccolte (es. bcdedit senza privilegi): la valutazione ne tiene conto.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    public SettingValue Reg(string probeId) =>
        Registry.TryGetValue(probeId, out var value) ? value : SettingValue.Absent;
}
