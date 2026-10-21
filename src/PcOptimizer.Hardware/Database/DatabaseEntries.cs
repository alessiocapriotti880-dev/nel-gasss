using PcOptimizer.Core.Model;
using PcOptimizer.Hardware.Model;

namespace PcOptimizer.Hardware.Database;

// Forma delle tabelle JSON in Data/. I campi "_comment" nei file sono ignorati.

public sealed record CpuDatabaseFile
{
    /// <summary>Clock di riferimento per l'indice per core (i5-6500: 3600 MHz in turbo).</summary>
    public double ReferenceClockMHz { get; init; } = 3600;

    public IReadOnlyList<CpuBrandModifier> BrandModifiers { get; init; } = [];

    public IReadOnlyList<CpuArchitectureEntry> Architectures { get; init; } = [];
}

public sealed record CpuArchitectureEntry
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public CpuVendor Vendor { get; init; }

    /// <summary>Famiglia CPUID in esadecimale, es. "0x6" o "0x19".</summary>
    public string Family { get; init; } = "";

    /// <summary>Modelli CPUID: singoli ("0xB7") o intervalli ("0x60-0x6F").</summary>
    public IReadOnlyList<string> Models { get; init; } = [];

    /// <summary>Se presente, la voce vale solo per questi stepping (es. Kaby Lake vs Coffee Lake su 0x9E).</summary>
    public IReadOnlyList<int>? Steppings { get; init; }

    public string Generation { get; init; } = "";

    public int Year { get; init; }

    /// <summary>Prestazioni per core a parità di clock, Skylake = 1.00.</summary>
    public double Ipc { get; init; }

    /// <summary>IPC dei core E sulle CPU ibride.</summary>
    public double? ECoreIpc { get; init; }

    public int TypicalBoostMHz { get; init; }

    public int? TypicalECoreBoostMHz { get; init; }

    public CpuSegment Segment { get; init; }

    public IReadOnlyList<CpuAdvisoryEntry> Advisories { get; init; } = [];
}

/// <summary>Problema noto di un'architettura, verificabile da microcode o data del BIOS.</summary>
public sealed record CpuAdvisoryEntry
{
    public string Id { get; init; } = "";

    public FindingSeverity Severity { get; init; } = FindingSeverity.Critical;

    /// <summary>Revisione microcode minima sicura, in esadecimale (es. "0x12B").</summary>
    public string? MinimumMicrocode { get; init; }

    /// <summary>Segnala i BIOS rilasciati prima di questa data.</summary>
    public DateTime? BiosReleasedBefore { get; init; }

    public string Title { get; init; } = "";

    public string Detail { get; init; } = "";

    public string Recommendation { get; init; } = "";
}

public sealed record CpuBrandModifier
{
    /// <summary>Testo da cercare nel nome commerciale (es. "X3D").</summary>
    public string Contains { get; init; } = "";

    public double GamingFactor { get; init; } = 1;

    public string Note { get; init; } = "";
}

public sealed record GpuDatabaseFile
{
    public IReadOnlyList<GpuEntry> Gpus { get; init; } = [];

    public IReadOnlyList<GpuModifier> Modifiers { get; init; } = [];

    /// <summary>Stima per GPU sconosciute in base alla VRAM (ordinata dalla soglia più alta).</summary>
    public IReadOnlyList<GpuVramFallback> VramFallback { get; init; } = [];
}

public sealed record GpuEntry
{
    /// <summary>Sequenza di parole da trovare nel nome normalizzato (es. "RTX 4070 TI SUPER").</summary>
    public string Match { get; init; } = "";

    public string Name { get; init; } = "";

    public GpuVendor Vendor { get; init; }

    /// <summary>Indice approssimato di prestazioni nei giochi, GTX 1060 6GB = 100.</summary>
    public double Index { get; init; }

    public int Year { get; init; }

    public bool Integrated { get; init; }
}

public sealed record GpuModifier
{
    /// <summary>Parola che, se presente nel nome, applica il fattore (es. "LAPTOP").</summary>
    public string Token { get; init; } = "";

    public double Factor { get; init; } = 1;

    public string Label { get; init; } = "";
}

public sealed record GpuVramFallback
{
    public double MinGB { get; init; }

    public double Index { get; init; }
}

public sealed record MemoryDatabaseFile
{
    public IReadOnlyList<MemoryPartPattern> Patterns { get; init; } = [];

    /// <summary>Velocità standard (MT/s) usate per arrotondare i codici abbreviati (es. 266 → 2666).</summary>
    public IReadOnlyList<int> StandardSpeeds { get; init; } = [];
}

public sealed record MemoryPartPattern
{
    public string Brand { get; init; } = "";

    /// <summary>Espressione regolare con gruppi "speed" (oppure "code") e opzionalmente "cl".</summary>
    public string Regex { get; init; } = "";

    /// <summary>Moltiplicatore per il gruppo "speed" (es. 100 per Kingston "KF436" → 3600).</summary>
    public double SpeedScale { get; init; } = 1;

    public bool SnapToStandard { get; init; }

    /// <summary>Moduli a specifica JEDEC (di serie): non hanno un profilo XMP/EXPO da attivare.</summary>
    public bool Jedec { get; init; }

    /// <summary>Codici velocità (es. Samsung "-CWE" → 3200) per il gruppo "code".</summary>
    public IReadOnlyDictionary<string, int>? Codes { get; init; }
}

public sealed record ChipsetDatabaseFile
{
    public IReadOnlyList<ChipsetEntry> Chipsets { get; init; } = [];
}

public sealed record ChipsetEntry
{
    public CpuVendor CpuVendor { get; init; }

    public IReadOnlyList<string> Names { get; init; } = [];

    public OcSupport CpuOverclock { get; init; }

    public OcSupport MemoryOverclock { get; init; }

    public string? Note { get; init; }
}

/// <summary>Parametri della classificazione in fascia (Data/tier-scoring.json).</summary>
public sealed record TierScoringConfig
{
    public ComponentWeights Weights { get; init; } = new();

    public TierThresholds Thresholds { get; init; } = new();

    public CpuScoring Cpu { get; init; } = new();

    public GpuScoring Gpu { get; init; } = new();

    public MemoryScoring Memory { get; init; } = new();

    public StorageScoring Storage { get; init; } = new();

    public TierCaps Caps { get; init; } = new();
}

public sealed record ComponentWeights
{
    public double Cpu { get; init; } = 0.35;
    public double Gpu { get; init; } = 0.35;
    public double Memory { get; init; } = 0.15;
    public double Storage { get; init; } = 0.15;
}

public sealed record TierThresholds
{
    public double Mid { get; init; } = 45;
    public double High { get; init; } = 85;
}

public sealed record CpuScoring
{
    /// <summary>Indice per core che vale 0 punti single-thread.</summary>
    public double SingleThreadMin { get; init; } = 0.6;

    /// <summary>Indice per core che vale 100 punti single-thread.</summary>
    public double SingleThreadMax { get; init; } = 2.0;

    /// <summary>Unità multi-thread (core "Skylake equivalenti") che valgono 100 punti.</summary>
    public double MultiThreadUnitsMax { get; init; } = 16;

    public double SingleThreadWeight { get; init; } = 0.6;

    /// <summary>Guadagno medio dato da Hyper-Threading/SMT per core.</summary>
    public double SmtFactor { get; init; } = 1.25;

    /// <summary>Riduzioni per i portatili (limiti di potenza e temperatura).</summary>
    public double LaptopSingleThreadFactor { get; init; } = 0.92;

    public double LaptopMultiThreadFactor { get; init; } = 0.75;
}

public sealed record GpuScoring
{
    /// <summary>Indice che vale 0 punti (scala logaritmica).</summary>
    public double IndexLow { get; init; } = 15;

    /// <summary>Indice che vale 100 punti.</summary>
    public double IndexHigh { get; init; } = 500;
}

public sealed record MemoryScoring
{
    /// <summary>Punti per quantità: coppie [GB, punti] in ordine crescente.</summary>
    public IReadOnlyList<double[]> CapacityPoints { get; init; } = [[4, 10], [8, 40], [16, 70], [32, 100]];

    public double BandwidthLowGBs { get; init; } = 12.8;

    public double BandwidthHighGBs { get; init; } = 76.8;

    public double CapacityWeight { get; init; } = 0.6;
}

public sealed record StorageScoring
{
    public double Nvme { get; init; } = 100;
    public double SataSsd { get; init; } = 70;
    public double UsbSsd { get; init; } = 40;
    public double Hdd { get; init; } = 10;
    public double UsbHdd { get; init; }
    public double Unknown { get; init; } = 50;
}

public sealed record TierCaps
{
    /// <summary>Sotto questa RAM (GB) la fascia massima è Bassa.</summary>
    public double LowIfMemoryUnderGB { get; init; } = 8;

    /// <summary>Sotto questa RAM (GB) la fascia massima è Media.</summary>
    public double MidIfMemoryUnderGB { get; init; } = 16;

    /// <summary>Con Windows su disco meccanico la fascia massima è Media.</summary>
    public bool MidIfSystemDiskIsHdd { get; init; } = true;

    /// <summary>Sotto questo punteggio GPU la fascia massima è Bassa.</summary>
    public double LowIfGpuScoreUnder { get; init; } = 20;
}
