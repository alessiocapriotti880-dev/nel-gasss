using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Core.Windows;

namespace PcOptimizer.Benchmark.Windows;

/// <summary>Contesto della misura: versione di Windows, piano energetico, timer, alimentazione, tempo dall'avvio.</summary>
[SupportedOSPlatform("windows")]
public static class EnvironmentReader
{
    public static BenchmarkEnvironment Read(IReadOnlyList<string> gpus)
    {
        Guid? scheme = null;
        try
        {
            scheme = PowerSettingProvider.ActiveScheme();
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or System.ComponentModel.Win32Exception)
        {
        }

        return new BenchmarkEnvironment
        {
            WindowsBuild = WindowsBuild(),
            IsElevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator),
            Cpu = (Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string)?.Trim(),
            Gpus = gpus,
            LogicalProcessors = Environment.ProcessorCount,
            PowerScheme = scheme,
            PowerSchemeName = scheme is { } s ? SchemeName(s) : null,
            TimerResolutionMs = BenchmarkNative.NtQueryTimerResolution(out _, out _, out var current) == 0 ? current / 10_000.0 : null,
            OnAcPower = AcPower(),
            UptimeMinutes = Environment.TickCount64 / 60_000.0,
        };
    }

    private static string? WindowsBuild()
    {
        const string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var build = Registry.GetValue(key, "CurrentBuild", null) as string;
        return build is null ? null : Registry.GetValue(key, "UBR", null) is int ubr ? $"{build}.{ubr}" : build;
    }

    /// <summary>null sui PC senza batteria: la domanda "a batteria?" non ha senso.</summary>
    private static bool? AcPower()
    {
        if (!BenchmarkNative.GetSystemPowerStatus(out var status) || (status.BatteryFlag & 128) != 0 || status.AcLineStatus == 255)
        {
            return null;
        }

        return status.AcLineStatus == 1;
    }

    private static string? SchemeName(Guid scheme)
    {
        uint size = 0;
        if (BenchmarkNative.PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size) != 0 || size == 0)
        {
            return null;
        }

        var buffer = new byte[size];
        return BenchmarkNative.PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
            ? Encoding.Unicode.GetString(buffer).TrimEnd('\0')
            : null;
    }
}
