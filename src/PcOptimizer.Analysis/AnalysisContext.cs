using PcOptimizer.Core.Model;
using PcOptimizer.Hardware.Model;

namespace PcOptimizer.Analysis;

/// <summary>Dati hardware che servono a interpretare la configurazione (es. portatile o desktop).</summary>
public sealed record AnalysisContext
{
    public bool IsLaptop { get; init; }

    public bool IsVirtualMachine { get; init; }

    public int WindowsBuild { get; init; }

    public long TotalRamBytes { get; init; }

    public HardwareTier? Tier { get; init; }

    /// <summary>GPU reali presenti (2 o più = grafica ibrida o integrata + dedicata).</summary>
    public int GpuCount { get; init; }

    public bool IsWindows11 => WindowsBuild >= 22000;

    public static AnalysisContext From(HardwareProfile profile, HardwareAssessment assessment) => new()
    {
        IsLaptop = profile.Board.IsLaptop,
        IsVirtualMachine = assessment.Board.IsVirtualMachine,
        WindowsBuild = profile.Os.Build,
        TotalRamBytes = assessment.Memory.InstalledBytes,
        Tier = assessment.Tier.Tier,
        GpuCount = assessment.Gpus.Count(g => !g.IsVirtual),
    };
}
