namespace PcOptimizer.Benchmark.Sensors;

/// <summary>Motivi di limitazione delle frequenze GPU di NVML (nvmlClocksEventReasons / ThrottleReasons).</summary>
public static class NvmlLimits
{
    private static readonly (ulong Bit, string Name)[] Reasons =
    [
        (0x4, "limite di potenza"),
        (0x8, "rallentamento hardware"),
        (0x20, "temperatura (driver)"),
        (0x40, "temperatura (hardware)"),
        (0x80, "freno di potenza esterno (alimentatore)"),
    ];

    /// <summary>Nomi leggibili dei motivi attivi. Inattività (0x1) e impostazioni delle applicazioni non sono limiti.</summary>
    public static IReadOnlyList<string> Describe(ulong reasons) =>
        Reasons.Where(r => (reasons & r.Bit) != 0).Select(r => r.Name).ToList();
}
