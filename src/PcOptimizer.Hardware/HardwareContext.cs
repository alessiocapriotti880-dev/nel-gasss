using PcOptimizer.Core.Tweaks;
using PcOptimizer.Hardware.Model;

namespace PcOptimizer.Hardware;

/// <summary>Scansione dell'hardware. L'implementazione reale è <c>Windows.WindowsHardwareScanner</c>.</summary>
public interface IHardwareScanner
{
    Task<HardwareProfile> ScanAsync(CancellationToken cancellationToken = default);
}

public static class HardwareContext
{
    /// <summary>
    /// Contesto per gli interventi: fascia e build di Windows, più profilo e valutazione
    /// come facet (gli interventi li leggono con <c>context.Get&lt;HardwareAssessment&gt;()</c>).
    /// </summary>
    public static TweakContext CreateTweakContext(HardwareProfile profile, HardwareAssessment assessment) =>
        new TweakContext(assessment.Tier.Tier, profile.Os.Build)
            .With(profile)
            .With(assessment);
}
