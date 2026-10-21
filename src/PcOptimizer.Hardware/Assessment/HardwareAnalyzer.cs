using PcOptimizer.Hardware.Database;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Hardware.Parsing;

namespace PcOptimizer.Hardware.Assessment;

/// <summary>
/// Trasforma i dati grezzi in una valutazione: identifica i componenti, stima XMP e canali RAM,
/// calcola i punteggi, la fascia e le osservazioni. Non accede al sistema.
/// </summary>
public sealed class HardwareAnalyzer(HardwareDatabase db)
{
    private const long OneGiB = 1024L * 1024 * 1024;

    // Velocità DDR4 standard JEDEC: una velocità fuori elenco (es. 3000, 3600) implica XMP o overclock.
    private static readonly int[] Ddr4JedecSpeeds = [1600, 1866, 2133, 2400, 2666, 2933, 3200];

    public HardwareAssessment Assess(HardwareProfile profile)
    {
        var board = AssessBoard(profile);
        var (cpu, architecture) = AssessCpu(profile);
        var gpus = profile.Gpus.Select(AssessGpu).ToList();
        var primary = gpus
            .Where(g => !g.IsVirtual)
            .OrderByDescending(g => g.Index ?? 0)
            .ThenBy(g => g.IsIntegrated)
            .FirstOrDefault();
        var memory = AssessMemory(profile, board);
        var storage = AssessStorage(profile);
        var tier = TierClassifier.Classify(db.Scoring, cpu, primary, memory, storage);
        var findings = HardwareFindings.Build(new HardwareFindings.Input(
            profile, db, architecture, cpu, gpus, primary, memory, storage, board));

        return new HardwareAssessment
        {
            Cpu = cpu,
            Gpus = gpus,
            PrimaryGpu = primary,
            Memory = memory,
            Storage = storage,
            Board = board,
            Tier = tier,
            Findings = findings,
        };
    }

    internal (CpuAssessment Assessment, CpuArchitectureEntry? Architecture) AssessCpu(HardwareProfile profile)
    {
        var cpu = profile.Cpu;
        var cfg = db.Scoring.Cpu;
        var known = db.FindCpuArchitecture(cpu.Vendor, cpu.Family, cpu.Model, cpu.Stepping);
        var arch = known ?? FallbackArchitecture(cpu);

        var pCores = cpu.PerformanceCores > 0 ? cpu.PerformanceCores
            : cpu.PhysicalCores > 0 ? cpu.PhysicalCores
            : Math.Max(cpu.LogicalProcessors, 1);
        var eCores = cpu.EfficiencyCores;
        var threads = cpu.LogicalProcessors > 0 ? cpu.LogicalProcessors : pCores + eCores;
        var smt = cpu.HasSmt || threads > pCores + eCores;

        // Clock reale da CPUID quando disponibile, altrimenti quello tipico dell'architettura.
        var boostFromCpuid = cpu.MaxClockMHz is > 0;
        var boost = boostFromCpuid ? cpu.MaxClockMHz!.Value : arch.TypicalBoostMHz;
        var reference = db.Cpu.ReferenceClockMHz;
        var gaming = db.FindBrandModifier(cpu.BrandString)?.GamingFactor ?? 1;

        var perCore = arch.Ipc * boost / reference;
        var ePerCore = (arch.ECoreIpc ?? arch.Ipc * 0.7) * (arch.TypicalECoreBoostMHz ?? boost * 0.8) / reference;

        // Portatili e CPU mobile lavorano con limiti di potenza più stretti.
        var powerLimited = profile.Board.IsLaptop || arch.Segment is CpuSegment.Mobile or CpuSegment.LowPower;
        var singleIndex = perCore * gaming * (powerLimited ? cfg.LaptopSingleThreadFactor : 1);
        var multiUnits = (pCores * perCore * (smt ? cfg.SmtFactor : 1) + eCores * ePerCore)
                         * (powerLimited ? cfg.LaptopMultiThreadFactor : 1);

        var single = ScoreMath.Linear(singleIndex, cfg.SingleThreadMin, cfg.SingleThreadMax);
        var multi = ScoreMath.Linear(multiUnits, 0, cfg.MultiThreadUnitsMax);

        var assessment = new CpuAssessment
        {
            ArchitectureId = known?.Id,
            ArchitectureName = arch.Name,
            Generation = arch.Generation,
            Segment = arch.Segment,
            PerformanceCores = pCores,
            EfficiencyCores = eCores,
            Threads = threads,
            BoostMHz = boost,
            BoostFromCpuid = boostFromCpuid,
            PerCoreIndex = Math.Round(singleIndex, 2),
            GamingFactor = gaming,
            SingleThreadScore = single,
            MultiThreadScore = multi,
            Score = cfg.SingleThreadWeight * single + (1 - cfg.SingleThreadWeight) * multi,
            Confidence = known is null ? Confidence.Low : boostFromCpuid ? Confidence.High : Confidence.Medium,
        };
        return (assessment, known);
    }

    internal GpuAssessment AssessGpu(GpuInfo gpu)
    {
        var cfg = db.Scoring.Gpu;
        if (gpu.UsesBasicDisplayDriver)
        {
            return new GpuAssessment
            {
                Gpu = gpu,
                IsVirtual = true,
                IndexNote = "driver del produttore non installato",
                Confidence = Confidence.High,
            };
        }

        if (!gpu.IsPhysicalPciDevice || gpu.Vendor == GpuVendor.Microsoft)
        {
            return new GpuAssessment
            {
                Gpu = gpu,
                IsVirtual = true,
                IndexNote = "adattatore virtuale (desktop remoto, streaming o monitor virtuale)",
                Confidence = Confidence.High,
            };
        }

        if (db.FindGpu(gpu.Vendor, gpu.Name) is { } match)
        {
            var (entry, modifier) = match;
            var index = entry.Index * (modifier?.Factor ?? 1);
            return new GpuAssessment
            {
                Gpu = gpu,
                ModelName = modifier is null ? entry.Name : $"{entry.Name} ({modifier.Label})",
                Index = index,
                IndexNote = modifier is null ? null : $"Prestazioni variabili in base al TGP: {modifier.Label}.",
                Year = entry.Year,
                IsIntegrated = entry.Integrated,
                Score = ScoreMath.Log(index, cfg.IndexLow, cfg.IndexHigh),
                Confidence = modifier is null ? Confidence.High : Confidence.Medium,
            };
        }

        // Modello sconosciuto: stima grossolana dalla memoria video, segnalata come incerta.
        var integrated = IsLikelyIntegrated(gpu);
        var estimate = db.EstimateGpuIndexFromVram(gpu.DedicatedMemoryBytes);
        if (integrated)
        {
            estimate = Math.Min(estimate ?? 10, 20);
        }

        return new GpuAssessment
        {
            Gpu = gpu,
            Index = estimate,
            IndexNote = "modello non presente nelle tabelle: stima in base alla memoria video",
            IsIntegrated = integrated,
            Score = estimate is null ? 0 : ScoreMath.Log(estimate.Value, cfg.IndexLow, cfg.IndexHigh),
            Confidence = Confidence.Low,
        };
    }

    internal MemoryAssessment AssessMemory(HardwareProfile profile, BoardAssessment board)
    {
        var cfg = db.Scoring.Memory;
        var modules = profile.Memory.Modules.Where(m => m.CapacityBytes > 0).ToList();
        var installed = modules.Count > 0 ? modules.Sum(m => m.CapacityBytes) : profile.Memory.VisibleBytes;
        var type = modules.GroupBy(m => m.Type).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        var configured = modules.Select(m => m.ConfiguredSpeedMTs).Where(s => s > 0).Min();

        var (channels, assumed) = EstimateChannels(modules);
        double? bandwidth = configured is > 0 && channels is > 0 ? configured.Value * 8.0 * channels.Value / 1000 : null;

        var parts = modules.Select(m => db.ParseMemoryPartNumber(m.PartNumber)).ToList();
        var (xmp, explanation, rated, cl) = EstimateXmp(profile, board, modules, parts, configured, type);

        var capacityScore = ScoreMath.Piecewise(installed / (double)OneGiB, cfg.CapacityPoints);
        var bandwidthScore = bandwidth is null ? 50 : ScoreMath.Log(bandwidth.Value, cfg.BandwidthLowGBs, cfg.BandwidthHighGBs);

        return new MemoryAssessment
        {
            InstalledBytes = installed,
            Type = type,
            ModuleCount = modules.Count,
            ConfiguredMTs = configured,
            RatedMTs = rated,
            RatedCl = cl,
            Channels = channels,
            ChannelsAssumed = assumed,
            BandwidthGBs = bandwidth,
            Xmp = xmp,
            XmpExplanation = explanation,
            Score = cfg.CapacityWeight * capacityScore + (1 - cfg.CapacityWeight) * bandwidthScore,
            Confidence = modules.Count == 0 ? Confidence.Low
                : configured is null || assumed ? Confidence.Medium
                : Confidence.High,
        };
    }

    internal StorageAssessment AssessStorage(HardwareProfile profile)
    {
        var cfg = db.Scoring.Storage;
        var disk = profile.Disks.FirstOrDefault(d => d.IsSystemDisk);
        var kind = disk is null ? StorageKind.Unknown : ClassifyDisk(disk);
        return new StorageAssessment
        {
            SystemDisk = disk,
            Kind = kind,
            Score = kind switch
            {
                StorageKind.Nvme => cfg.Nvme,
                StorageKind.SataSsd => cfg.SataSsd,
                StorageKind.UsbSsd => cfg.UsbSsd,
                StorageKind.Hdd => cfg.Hdd,
                StorageKind.UsbHdd => cfg.UsbHdd,
                _ => cfg.Unknown,
            },
        };
    }

    internal BoardAssessment AssessBoard(HardwareProfile profile)
    {
        var b = profile.Board;
        var vendor = BoardParser.ParseVendor(b.BoardManufacturer);
        if (vendor is BoardVendor.Unknown or BoardVendor.Other &&
            BoardParser.ParseVendor(b.SystemManufacturer) is var system and not BoardVendor.Unknown and not BoardVendor.Other)
        {
            vendor = system;
        }

        // Sui portatili il "prodotto" è il codice del modello, non un chipset.
        if (b.IsLaptop)
        {
            return new BoardAssessment
            {
                Vendor = vendor,
                ChipsetNote = "Portatile: overclock e profili di memoria dipendono dal produttore e di solito non sono disponibili.",
            };
        }

        var chipset = BoardParser.ExtractChipset(b.BoardProduct);
        var entry = db.FindChipset(profile.Cpu.Vendor, chipset);
        return new BoardAssessment
        {
            Vendor = vendor,
            Chipset = entry is null ? null : chipset,
            CpuOverclock = entry?.CpuOverclock ?? OcSupport.Unknown,
            MemoryOverclock = entry?.MemoryOverclock ?? OcSupport.Unknown,
            ChipsetNote = entry?.Note,
        };
    }

    internal static StorageKind ClassifyDisk(DiskInfo disk)
    {
        var usb = disk.BusType == DiskBusType.Usb;
        if (!usb && (disk.BusType == DiskBusType.Nvme || disk.FriendlyName.Contains("NVMe", StringComparison.OrdinalIgnoreCase)))
        {
            return StorageKind.Nvme;
        }

        return disk.MediaType switch
        {
            DiskMediaType.Ssd or DiskMediaType.Scm => usb ? StorageKind.UsbSsd : StorageKind.SataSsd,
            DiskMediaType.Hdd => usb ? StorageKind.UsbHdd : StorageKind.Hdd,
            _ => StorageKind.Unknown,
        };
    }

    /// <summary>
    /// Numero di canali dai nomi degli slot. Con un solo modulo è certo (canale singolo);
    /// con più moduli serve che ogni slot sia interpretabile e che i nomi siano distinti,
    /// altrimenti si assume il dual channel tipico dei PC consumer e lo si dichiara.
    /// </summary>
    internal static (int? Channels, bool Assumed) EstimateChannels(IReadOnlyList<MemoryModule> modules)
    {
        if (modules.Count == 0)
        {
            return (null, true);
        }

        if (modules.Count == 1)
        {
            return (1, false);
        }

        var keys = modules.Select(m => MemoryParser.ParseChannel(m.DeviceLocator, m.BankLabel)).ToList();
        var distinctSlots = modules
            .Select(m => $"{m.DeviceLocator}|{m.BankLabel}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == modules.Count;

        return keys.All(k => k is not null) && distinctSlots
            ? (keys.Distinct().Count(), false)
            : (2, true);
    }

    private static (XmpState State, string Explanation, int? Rated, int? Cl) EstimateXmp(
        HardwareProfile profile, BoardAssessment board, IReadOnlyList<MemoryModule> modules,
        IReadOnlyList<ModulePartInfo?> parts, int? configured, MemoryType type)
    {
        var xmpKits = parts.Where(p => p is { IsJedec: false }).Select(p => p!).ToList();
        var jedecKits = parts.Where(p => p is { IsJedec: true }).Select(p => p!).ToList();
        int? rated = xmpKits.Count > 0 ? xmpKits.Min(p => p.RatedSpeedMTs)
            : jedecKits.Count > 0 ? jedecKits.Min(p => p.RatedSpeedMTs)
            : null;
        var cl = xmpKits.Select(p => p.CasLatency).FirstOrDefault(c => c is not null);

        if (configured is null)
        {
            return (XmpState.Unknown, "Velocità attuale della RAM non disponibile.", rated, cl);
        }

        if (profile.Board.IsLaptop || type is MemoryType.Lpddr3 or MemoryType.Lpddr4 or MemoryType.Lpddr5)
        {
            return (XmpState.NotAvailable,
                "Memoria di un portatile o saldata: la velocità è fissata dal produttore, non c'è un profilo XMP/EXPO da attivare.",
                rated, cl);
        }

        if (xmpKits.Count == 0 && jedecKits.Count > 0)
        {
            return (XmpState.NotAvailable,
                $"Moduli standard JEDEC ({rated} MT/s): lavorano già alla velocità di serie e non hanno un profilo XMP/EXPO.",
                rated, cl);
        }

        // Senza codice prodotto riconosciuto, usiamo la velocità dichiarata dal firmware (indizio più debole).
        var firmwareRated = modules.Select(m => m.SpeedMTs ?? 0).DefaultIfEmpty(0).Max();
        var fromFirmware = rated is null && firmwareRated > configured * 1.03;
        var target = rated ?? (fromFirmware ? (int?)firmwareRated : null);

        if (target is { } r)
        {
            var source = fromFirmware ? "secondo il firmware" : "velocità certificata del kit";
            if (configured >= r * 0.98)
            {
                return (XmpState.LikelyEnabled,
                    $"La RAM lavora a {configured} MT/s, la velocità certificata del kit: profilo XMP/EXPO attivo.",
                    r, cl);
            }

            if (board.MemoryOverclock == OcSupport.No)
            {
                return (XmpState.NotSupportedByPlatform,
                    $"Il kit supporta {r} MT/s ({source}), ma il chipset {board.Chipset} non consente di superare la velocità standard: " +
                    $"la RAM lavora a {configured} MT/s. È un limite della piattaforma, non un errore di configurazione.",
                    r, cl);
            }

            var limited = board.MemoryOverclock == OcSupport.Limited
                ? $" Sul chipset {board.Chipset} il supporto ai profili di memoria dipende dalla scheda madre."
                : "";
            return (XmpState.LikelyDisabled,
                $"Il kit supporta {r} MT/s ({source}) ma la RAM lavora a {configured} MT/s: il profilo XMP/EXPO probabilmente non è attivo.{limited}",
                r, cl);
        }

        if (IsAboveJedec(configured.Value, type))
        {
            return (XmpState.LikelyEnabled,
                $"{configured} MT/s è oltre le velocità standard JEDEC: profilo XMP/EXPO (o overclock manuale) attivo.",
                null, cl);
        }

        return (XmpState.Unknown,
            $"La RAM lavora a {configured} MT/s. Codice prodotto non riconosciuto: verifica nel BIOS se è disponibile un profilo XMP/EXPO.",
            null, cl);
    }

    // Tolleranza di qualche MT/s: molti BIOS riportano 2667 invece di 2666 o 2934 invece di 2933.
    private static bool IsAboveJedec(int configured, MemoryType type) => type switch
    {
        MemoryType.Ddr3 => configured > 1610,
        MemoryType.Ddr4 => configured > 3210 || !Ddr4JedecSpeeds.Any(s => Math.Abs(s - configured) <= 3),
        MemoryType.Ddr5 => configured > 5610,
        _ => false,
    };

    private static bool IsLikelyIntegrated(GpuInfo gpu)
    {
        var tokens = HardwareDatabase.NormalizeGpuName(gpu.Name);
        var arcIndex = Array.IndexOf(tokens, "ARC");
        var discreteArc = arcIndex >= 0 && arcIndex + 1 < tokens.Length &&
                          tokens[arcIndex + 1] is [ 'A' or 'B', var digit, ..] && char.IsAsciiDigit(digit);
        return (gpu.Vendor == GpuVendor.Intel && !discreteArc) ||
               gpu.DedicatedMemoryBytes is > 0 and < 512L * 1024 * 1024;
    }

    /// <summary>Valori prudenti per CPU non in tabella (confidenza bassa, segnalata all'utente).</summary>
    private static CpuArchitectureEntry FallbackArchitecture(CpuInfo cpu)
    {
        (string Name, double Ipc, double? EIpc, int Boost) fallback = (cpu.Vendor, cpu.Family) switch
        {
            (CpuVendor.Intel, 6) when cpu.IsHybrid || cpu.Model > 0xC6 => ("Intel recente (non in tabella)", 1.45, 1.1, 5000),
            (CpuVendor.Intel, _) => ("Intel (non in tabella)", 0.9, null, 3500),
            (CpuVendor.Amd, >= 0x1A) => ("AMD recente (non in tabella)", 1.5, null, 5200),
            (CpuVendor.Amd, 0x19) => ("AMD Zen 3/4 (non in tabella)", 1.3, null, 4700),
            (CpuVendor.Amd, 0x17) => ("AMD Zen/Zen 2 (non in tabella)", 1.0, null, 4000),
            (CpuVendor.Amd, _) => ("AMD (non in tabella)", 0.6, null, 3500),
            _ => ("CPU non riconosciuta", 1.0, null, 3500),
        };

        return new CpuArchitectureEntry
        {
            Id = "unknown",
            Name = fallback.Name,
            Vendor = cpu.Vendor,
            Generation = "sconosciuta",
            Ipc = fallback.Ipc,
            ECoreIpc = fallback.EIpc,
            TypicalBoostMHz = fallback.Boost,
            TypicalECoreBoostMHz = fallback.EIpc is null ? null : 4000,
            Segment = CpuSegment.Desktop,
        };
    }
}
