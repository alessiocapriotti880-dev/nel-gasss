using PcOptimizer.Core.Model;
using PcOptimizer.Hardware.Database;
using PcOptimizer.Hardware.Model;

namespace PcOptimizer.Hardware.Assessment;

/// <summary>
/// Classificazione in fascia: media pesata dei punteggi dei componenti, poi i limiti
/// ("caps") per i colli di bottiglia che nessun punteggio alto può compensare
/// (es. 8 GB di RAM o Windows su disco meccanico non sono mai fascia Alta).
/// </summary>
public static class TierClassifier
{
    private const double OneGiB = 1024.0 * 1024 * 1024;

    public static TierResult Classify(
        TierScoringConfig cfg, CpuAssessment cpu, GpuAssessment? gpu, MemoryAssessment memory, StorageAssessment storage)
    {
        var components = new List<ComponentScore>
        {
            new("CPU", cpu.Score, cfg.Weights.Cpu, CpuText(cpu), cpu.Confidence),
            new("GPU", gpu?.Score ?? 0, cfg.Weights.Gpu, GpuText(gpu), gpu?.Confidence ?? Confidence.Low),
            new("RAM", memory.Score, cfg.Weights.Memory, MemoryText(memory), memory.Confidence),
            new("Disco", storage.Score, cfg.Weights.Storage, StorageText(storage),
                storage.Kind == StorageKind.Unknown ? Confidence.Low : Confidence.High),
        };

        var score = components.Sum(c => c.Score * c.Weight) / components.Sum(c => c.Weight);
        var tier = score >= cfg.Thresholds.High ? HardwareTier.High
            : score >= cfg.Thresholds.Mid ? HardwareTier.Mid
            : HardwareTier.Low;

        // Limiti: si registrano solo quelli che abbassano davvero la fascia calcolata.
        var caps = new List<string>();
        var final = tier;
        void Cap(HardwareTier limit, string reason)
        {
            if (limit < final)
            {
                final = limit;
                caps.Add(reason);
            }
        }

        // Tolleranza di 0,25 GB: se si conosce solo la memoria visibile, 8 GB installati appaiono come ~7,9.
        var memoryGB = memory.InstalledBytes / OneGiB + 0.25;
        if (memory.InstalledBytes > 0 && memoryGB < cfg.Caps.LowIfMemoryUnderGB)
        {
            Cap(HardwareTier.Low, $"RAM inferiore a {ScoreMath.Fmt(cfg.Caps.LowIfMemoryUnderGB)} GB: fascia massima Bassa.");
        }
        else if (memory.InstalledBytes > 0 && memoryGB < cfg.Caps.MidIfMemoryUnderGB)
        {
            Cap(HardwareTier.Mid, $"RAM inferiore a {ScoreMath.Fmt(cfg.Caps.MidIfMemoryUnderGB)} GB: fascia massima Media.");
        }

        if (cfg.Caps.MidIfSystemDiskIsHdd && storage.Kind is StorageKind.Hdd or StorageKind.UsbHdd)
        {
            Cap(HardwareTier.Mid, "Windows su disco meccanico (HDD): fascia massima Media.");
        }

        if ((gpu?.Score ?? 0) < cfg.Caps.LowIfGpuScoreUnder)
        {
            Cap(HardwareTier.Low, "GPU integrata o di fascia molto bassa: fascia massima Bassa.");
        }

        return new TierResult
        {
            Tier = final,
            Score = Math.Round(score, 1),
            Components = components,
            AppliedCaps = caps,
            Confidence = components.Min(c => c.Confidence),
        };
    }

    public static string TierName(HardwareTier tier) => tier switch
    {
        HardwareTier.Low => "Bassa",
        HardwareTier.Mid => "Media",
        _ => "Alta",
    };

    private static string CpuText(CpuAssessment cpu)
    {
        var cores = cpu.EfficiencyCores > 0
            ? $"{cpu.PerformanceCores}P + {cpu.EfficiencyCores}E / {cpu.Threads} thread"
            : $"{cpu.PerformanceCores} core / {cpu.Threads} thread";
        var clock = cpu.BoostFromCpuid ? $"{cpu.BoostMHz} MHz" : $"~{cpu.BoostMHz} MHz (tipico)";
        return $"{cpu.ArchitectureName}, {cores}, {clock}: single-thread {cpu.SingleThreadScore:0}, multi-thread {cpu.MultiThreadScore:0}";
    }

    private static string GpuText(GpuAssessment? gpu) => gpu switch
    {
        null => "Nessuna GPU utilizzabile rilevata",
        { Index: null } => $"{gpu.Gpu.Name}: prestazioni non stimabili",
        _ => $"{gpu.ModelName ?? gpu.Gpu.Name}: indice {gpu.Index:0} (GTX 1060 6GB = 100)",
    };

    private static string MemoryText(MemoryAssessment m)
    {
        var type = m.Type == MemoryType.Unknown ? "" : $" {m.Type.ToString().ToUpperInvariant()}";
        var speed = m.ConfiguredMTs is { } s ? $", {s} MT/s" : "";
        var channels = m.Channels switch
        {
            null => "",
            1 => ", canale singolo",
            2 => m.ChannelsAssumed ? ", dual channel (stimato)" : ", dual channel",
            var n => $", {n} canali",
        };
        return $"{ScoreMath.Fmt(m.InstalledGB)} GB{type}{speed}{channels}";
    }

    private static string StorageText(StorageAssessment s) => s.Kind switch
    {
        StorageKind.Nvme => "Windows su SSD NVMe",
        StorageKind.SataSsd => "Windows su SSD SATA",
        StorageKind.UsbSsd => "Windows su SSD USB",
        StorageKind.Hdd => "Windows su disco meccanico (HDD)",
        StorageKind.UsbHdd => "Windows su disco meccanico USB",
        _ => "Tipo di disco di sistema non determinato",
    };
}
