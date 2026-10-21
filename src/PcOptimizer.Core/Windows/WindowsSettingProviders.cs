using System.Runtime.Versioning;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Windows;

/// <summary>Insieme dei provider reali per Windows. I moduli successivi aggiungono i propri qui.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsSettingProviders
{
    public static SettingStore CreateStore() => new(
    [
        new RegistrySettingProvider(),
        new ServiceStartProvider(),
        new PowerSettingProvider(),
    ]);
}
