namespace PcOptimizer.Hardware.Model;

// Dati GREZZI rilevati dal sistema, senza interpretazioni: si possono salvare in JSON,
// confrontare tra una scansione e l'altra e allegare a una segnalazione di bug.
// L'interpretazione (architettura, fascia, osservazioni) sta in HardwareAssessment.

public enum CpuVendor
{
    Unknown,
    Intel,
    Amd,
}

public sealed record CpuInfo
{
    public CpuVendor Vendor { get; init; }

    /// <summary>Stringa CPUID del produttore, es. "GenuineIntel".</summary>
    public string VendorId { get; init; } = "";

    /// <summary>Nome commerciale, es. "Intel(R) Core(TM) i9-14900KF".</summary>
    public string BrandString { get; init; } = "";

    /// <summary>Famiglia CPUID "display" (già combinata con la famiglia estesa).</summary>
    public int Family { get; init; }

    /// <summary>Modello CPUID "display" (già combinato con il modello esteso), es. 0xB7.</summary>
    public int Model { get; init; }

    public int Stepping { get; init; }

    public int Packages { get; init; } = 1;

    public int PhysicalCores { get; init; }

    public int LogicalProcessors { get; init; }

    /// <summary>Core P (su CPU non ibride coincide con tutti i core fisici).</summary>
    public int PerformanceCores { get; init; }

    /// <summary>Core E (0 su CPU non ibride).</summary>
    public int EfficiencyCores { get; init; }

    /// <summary>Hyper-Threading / SMT attivo su almeno un core.</summary>
    public bool HasSmt { get; init; }

    public bool IsHybrid => EfficiencyCores > 0;

    /// <summary>Revisione del microcode caricato (null se non leggibile).</summary>
    public uint? MicrocodeRevision { get; init; }

    public int? BaseClockMHz { get; init; }

    /// <summary>Frequenza massima dichiarata da CPUID 0x16 (solo Intel 6ª gen+); null se non disponibile.</summary>
    public int? MaxClockMHz { get; init; }

    public long? L3CacheBytes { get; init; }

    /// <summary>Bit "hypervisor" di CPUID: attivo in macchina virtuale ma anche con VBS/Hyper-V sull'host.</summary>
    public bool HypervisorPresent { get; init; }
}

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel,
    Microsoft,
    Other,
}

public sealed record GpuInfo
{
    public string Name { get; init; } = "";

    public GpuVendor Vendor { get; init; }

    /// <summary>ID PCI del produttore in esadecimale (es. "10DE").</summary>
    public string? PciVendorId { get; init; }

    public string? PciDeviceId { get; init; }

    public string? PnpDeviceId { get; init; }

    /// <summary>Memoria video dedicata (dal registro: precisa anche oltre 4 GB, a differenza di WMI).</summary>
    public long? DedicatedMemoryBytes { get; init; }

    /// <summary>Versione driver nel formato Windows, es. "32.0.15.6094".</summary>
    public string? DriverVersion { get; init; }

    /// <summary>Versione nel formato del produttore, es. NVIDIA "560.94" o AMD Adrenalin "24.3.1".</summary>
    public string? VendorDriverVersion { get; init; }

    public DateTime? DriverDate { get; init; }

    /// <summary>Dispositivo PCI reale (esclude adattatori virtuali/indiretti come Parsec, DisplayLink, desktop remoto).</summary>
    public bool IsPhysicalPciDevice { get; init; }

    /// <summary>La GPU usa il driver generico "Microsoft Basic Display Adapter": driver del produttore mancante.</summary>
    public bool UsesBasicDisplayDriver { get; init; }
}

public enum MemoryType
{
    Unknown,
    Ddr3,
    Ddr4,
    Ddr5,
    Lpddr3,
    Lpddr4,
    Lpddr5,
}

public sealed record MemoryModule
{
    public string? Manufacturer { get; init; }

    public string? PartNumber { get; init; }

    public long CapacityBytes { get; init; }

    /// <summary>Velocità dichiarata dal firmware (SMBIOS "Speed"), in MT/s.</summary>
    public int? SpeedMTs { get; init; }

    /// <summary>Velocità a cui la RAM lavora adesso, in MT/s (già corretta se il BIOS la riporta dimezzata).</summary>
    public int? ConfiguredSpeedMTs { get; init; }

    public MemoryType Type { get; init; }

    /// <summary>Nome dello slot, es. "DIMM_A2" o "ChannelA-DIMM0".</summary>
    public string? DeviceLocator { get; init; }

    public string? BankLabel { get; init; }

    public int? ConfiguredVoltageMv { get; init; }
}

public sealed record MemoryInfo
{
    public IReadOnlyList<MemoryModule> Modules { get; init; } = [];

    /// <summary>Memoria vista da Windows (installata meno quella riservata, es. alla grafica integrata).</summary>
    public long VisibleBytes { get; init; }
}

public enum DiskMediaType
{
    Unknown,
    Hdd,
    Ssd,
    Scm,
}

public enum DiskBusType
{
    Unknown,
    Sata,
    Nvme,
    Usb,
    Raid,
    Sas,
    Scsi,
    Sd,
    Mmc,
    Virtual,
    Other,
}

public enum DiskHealth
{
    Unknown,
    Healthy,
    Warning,
    Unhealthy,
}

public sealed record DiskInfo
{
    public string DeviceId { get; init; } = "";

    public string FriendlyName { get; init; } = "";

    public DiskMediaType MediaType { get; init; }

    public DiskBusType BusType { get; init; }

    public long SizeBytes { get; init; }

    public DiskHealth Health { get; init; }

    /// <summary>Disco che contiene la partizione di Windows.</summary>
    public bool IsSystemDisk { get; init; }

    public string? FirmwareVersion { get; init; }
}

public enum FirmwareType
{
    Unknown,
    LegacyBios,
    Uefi,
}

public sealed record BoardInfo
{
    public string BoardManufacturer { get; init; } = "";

    /// <summary>Modello della scheda madre, es. "ROG STRIX Z790-E GAMING WIFI".</summary>
    public string BoardProduct { get; init; } = "";

    public string? BoardVersion { get; init; }

    /// <summary>Produttore del PC (diverso dalla scheda madre sui PC di marca: Dell, HP, Lenovo...).</summary>
    public string SystemManufacturer { get; init; } = "";

    public string SystemModel { get; init; } = "";

    public string BiosVendor { get; init; } = "";

    public string BiosVersion { get; init; } = "";

    public DateTime? BiosReleaseDate { get; init; }

    public FirmwareType Firmware { get; init; }

    public bool? SecureBootEnabled { get; init; }

    public bool IsLaptop { get; init; }

    /// <summary>True se il portatile sta funzionando a batteria (null se non determinabile).</summary>
    public bool? OnBattery { get; init; }
}

public enum NetworkMedium
{
    Unknown,
    Ethernet,
    WiFi,
    Other,
}

public sealed record NetworkAdapterInfo
{
    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public NetworkMedium Medium { get; init; }

    public long? LinkSpeedBps { get; init; }

    public bool IsConnected { get; init; }

    /// <summary>Scheda usata da Windows per raggiungere Internet.</summary>
    public bool IsPrimary { get; init; }

    public string? DriverVersion { get; init; }

    public string? DriverDate { get; init; }

    public string? PnpDeviceId { get; init; }

    public string? InterfaceId { get; init; }
}

public enum DisplayConnection
{
    Unknown,
    Vga,
    Dvi,
    Hdmi,
    DisplayPort,
    Internal,
    Wireless,
    Indirect,
    Other,
}

public sealed record DisplayInfo
{
    public string Name { get; init; } = "";

    /// <summary>Nome GDI, es. "\\.\DISPLAY1".</summary>
    public string? GdiDeviceName { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Frequenza di aggiornamento attuale (es. 143.98); 0 se non nota.</summary>
    public double RefreshHz { get; init; }

    /// <summary>Frequenza massima offerta dal monitor alla risoluzione attuale.</summary>
    public int? MaxRefreshHz { get; init; }

    public DisplayConnection Connection { get; init; }

    public bool IsPrimary { get; init; }

    /// <summary>ID PCI della GPU a cui il monitor è fisicamente collegato.</summary>
    public string? AdapterPciVendorId { get; init; }

    public string? AdapterPciDeviceId { get; init; }
}

public sealed record OsInfo
{
    /// <summary>Nome dal registro: su Windows 11 riporta ancora "Windows 10" (vedi <see cref="FriendlyName"/>).</summary>
    public string ProductName { get; init; } = "";

    /// <summary>Versione commerciale, es. "23H2".</summary>
    public string? DisplayVersion { get; init; }

    public int Build { get; init; }

    /// <summary>Update Build Revision: numero dell'aggiornamento cumulativo installato.</summary>
    public int Ubr { get; init; }

    public string? Edition { get; init; }

    public long SystemDriveTotalBytes { get; init; }

    public long SystemDriveFreeBytes { get; init; }

    public bool IsWindows11 => Build >= 22000;

    public string FriendlyName => IsWindows11 && ProductName.StartsWith("Windows 10", StringComparison.Ordinal)
        ? "Windows 11" + ProductName["Windows 10".Length..]
        : ProductName;
}

/// <summary>Istantanea completa dell'hardware.</summary>
public sealed record HardwareProfile
{
    public CpuInfo Cpu { get; init; } = new();

    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];

    public MemoryInfo Memory { get; init; } = new();

    public IReadOnlyList<DiskInfo> Disks { get; init; } = [];

    public BoardInfo Board { get; init; } = new();

    public IReadOnlyList<NetworkAdapterInfo> NetworkAdapters { get; init; } = [];

    public IReadOnlyList<DisplayInfo> Displays { get; init; } = [];

    public OsInfo Os { get; init; } = new();

    public DateTimeOffset ScannedAt { get; init; }

    /// <summary>Parti non rilevate (es. WMI non disponibile): la valutazione lo tiene presente.</summary>
    public IReadOnlyList<string> ScanErrors { get; init; } = [];
}
