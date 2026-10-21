using PcOptimizer.Core.Model;

namespace PcOptimizer.Hardware.Model;

// INTERPRETAZIONE dei dati grezzi: identificazione dei componenti, punteggi, fascia, osservazioni.
// Prodotta da HardwareAnalyzer, è pura (nessun accesso al sistema) e quindi interamente testabile.

/// <summary>Quanto è affidabile una stima: bassa se il componente non è nelle tabelle.</summary>
public enum Confidence
{
    Low,
    Medium,
    High,
}

public enum CpuSegment
{
    Desktop,
    Mobile,
    LowPower,
    Workstation,
}

/// <summary>Supporto all'overclock di una piattaforma (chipset).</summary>
public enum OcSupport
{
    Unknown,
    No,
    /// <summary>Non ufficiale o dipendente dal modello di scheda madre.</summary>
    Limited,
    Yes,
}

/// <summary>Stima dello stato del profilo XMP/EXPO: non leggibile direttamente senza driver kernel.</summary>
public enum XmpState
{
    Unknown,
    LikelyEnabled,
    LikelyDisabled,
    /// <summary>La RAM supporterebbe di più, ma il chipset non consente l'overclock della memoria.</summary>
    NotSupportedByPlatform,
    /// <summary>Moduli JEDEC (di serie) o memoria di portatile: non c'è un profilo da attivare.</summary>
    NotAvailable,
}

public enum StorageKind
{
    Unknown,
    Hdd,
    UsbHdd,
    SataSsd,
    UsbSsd,
    Nvme,
}

public enum BoardVendor
{
    Unknown,
    Asus,
    Msi,
    Gigabyte,
    AsRock,
    Biostar,
    Dell,
    Hp,
    Lenovo,
    Acer,
    Other,
}

public sealed record CpuAssessment
{
    public string? ArchitectureId { get; init; }

    public required string ArchitectureName { get; init; }

    public required string Generation { get; init; }

    public CpuSegment Segment { get; init; }

    public int PerformanceCores { get; init; }

    public int EfficiencyCores { get; init; }

    public int Threads { get; init; }

    /// <summary>Clock massimo usato per il calcolo.</summary>
    public int BoostMHz { get; init; }

    /// <summary>True se il clock viene da CPUID (misurato), false se dalla tabella (tipico).</summary>
    public bool BoostFromCpuid { get; init; }

    /// <summary>Prestazioni per core stimate (i5-6500 = 1.00), già comprensive del fattore gioco.</summary>
    public double PerCoreIndex { get; init; }

    /// <summary>Fattore specifico per i giochi (es. 1.15 per le CPU X3D).</summary>
    public double GamingFactor { get; init; } = 1;

    public double SingleThreadScore { get; init; }

    public double MultiThreadScore { get; init; }

    /// <summary>Punteggio 0-100.</summary>
    public double Score { get; init; }

    public Confidence Confidence { get; init; }
}

public sealed record GpuAssessment
{
    public required GpuInfo Gpu { get; init; }

    /// <summary>Modello riconosciuto nella tabella (null se sconosciuto).</summary>
    public string? ModelName { get; init; }

    /// <summary>Indice di prestazioni approssimato (GTX 1060 6GB = 100); null se non stimabile.</summary>
    public double? Index { get; init; }

    public string? IndexNote { get; init; }

    public int? Year { get; init; }

    public bool IsIntegrated { get; init; }

    /// <summary>Adattatore virtuale/indiretto o driver mancante: non usabile per giocare.</summary>
    public bool IsVirtual { get; init; }

    /// <summary>Punteggio 0-100.</summary>
    public double Score { get; init; }

    public Confidence Confidence { get; init; }
}

/// <summary>Informazioni ricavate dal codice prodotto di un modulo RAM.</summary>
public sealed record ModulePartInfo(string Brand, int RatedSpeedMTs, int? CasLatency, bool IsJedec);

public sealed record MemoryAssessment
{
    public long InstalledBytes { get; init; }

    public double InstalledGB => InstalledBytes / (1024.0 * 1024 * 1024);

    public MemoryType Type { get; init; }

    public int ModuleCount { get; init; }

    public int? ConfiguredMTs { get; init; }

    /// <summary>Velocità certificata del kit (dal codice prodotto o dal firmware).</summary>
    public int? RatedMTs { get; init; }

    /// <summary>CAS latency dichiarata dal codice prodotto. I timing reali non si leggono senza driver kernel.</summary>
    public int? RatedCl { get; init; }

    public int? Channels { get; init; }

    /// <summary>True se il numero di canali è stimato (nomi degli slot non interpretabili).</summary>
    public bool ChannelsAssumed { get; init; }

    public double? BandwidthGBs { get; init; }

    public XmpState Xmp { get; init; }

    public string XmpExplanation { get; init; } = "";

    public double Score { get; init; }

    public Confidence Confidence { get; init; }
}

public sealed record StorageAssessment
{
    public DiskInfo? SystemDisk { get; init; }

    public StorageKind Kind { get; init; }

    public double Score { get; init; }
}

public sealed record BoardAssessment
{
    public BoardVendor Vendor { get; init; }

    /// <summary>Chipset ricavato dal nome della scheda madre (es. "Z790"); null sui PC di marca/portatili.</summary>
    public string? Chipset { get; init; }

    public OcSupport CpuOverclock { get; init; }

    public OcSupport MemoryOverclock { get; init; }

    public string? ChipsetNote { get; init; }

    /// <summary>Il sistema è una macchina virtuale: niente ottimizzazioni né misure significative.</summary>
    public bool IsVirtualMachine { get; init; }
}

/// <summary>Contributo di un componente al punteggio totale.</summary>
public sealed record ComponentScore(string Component, double Score, double Weight, string Explanation, Confidence Confidence);

public sealed record TierResult
{
    public required HardwareTier Tier { get; init; }

    /// <summary>Punteggio pesato 0-100 prima dei limiti.</summary>
    public required double Score { get; init; }

    public required IReadOnlyList<ComponentScore> Components { get; init; }

    /// <summary>Limiti applicati (es. "RAM sotto 12 GB: fascia massima Media").</summary>
    public required IReadOnlyList<string> AppliedCaps { get; init; }

    public required Confidence Confidence { get; init; }
}

public sealed record HardwareAssessment
{
    public required CpuAssessment Cpu { get; init; }

    public required IReadOnlyList<GpuAssessment> Gpus { get; init; }

    /// <summary>GPU usata per giocare: la più potente tra quelle reali.</summary>
    public GpuAssessment? PrimaryGpu { get; init; }

    public required MemoryAssessment Memory { get; init; }

    public required StorageAssessment Storage { get; init; }

    public required BoardAssessment Board { get; init; }

    public required TierResult Tier { get; init; }

    public required IReadOnlyList<Finding> Findings { get; init; }
}
