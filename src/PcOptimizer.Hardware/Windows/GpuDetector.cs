using System.Runtime.Versioning;
using Microsoft.Win32;
using PcOptimizer.Core.Windows;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Hardware.Parsing;

namespace PcOptimizer.Hardware.Windows;

/// <summary>
/// GPU: elenco da WMI (Win32_VideoController), memoria video e versione del pannello AMD
/// dalla chiave di classe del driver nel registro (WMI riporta al massimo 4 GB di VRAM).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class GpuDetector
{
    public static IReadOnlyList<GpuInfo> Detect()
    {
        var result = new List<GpuInfo>();
        var rows = Wmi.Query(Wmi.Cimv2,
            "SELECT Name, PNPDeviceID, DriverVersion, DriverDate, AdapterRAM, AdapterCompatibility FROM Win32_VideoController");

        foreach (var row in rows)
        {
            var name = row.Str("Name") ?? "Scheda video sconosciuta";
            var pnp = row.Str("PNPDeviceID");
            var (vendorId, deviceId) = PciIdParser.Parse(pnp);
            var vendor = PciIdParser.GpuVendorFromPci(vendorId);
            if (vendor == GpuVendor.Unknown)
            {
                vendor = VendorFromName($"{row.Str("AdapterCompatibility")} {name}");
            }

            var physical = pnp?.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) == true;
            var basicDriver = name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase);
            var (memory, radeonVersion) = ReadDriverClassKey(pnp);
            var adapterRam = row.Long("AdapterRAM");
            var driverVersion = row.Str("DriverVersion");

            result.Add(new GpuInfo
            {
                Name = name,
                Vendor = vendor,
                PciVendorId = vendorId,
                PciDeviceId = deviceId,
                PnpDeviceId = pnp,
                DedicatedMemoryBytes = memory ?? (adapterRam > 0 ? adapterRam : null),
                DriverVersion = driverVersion,
                VendorDriverVersion = vendor switch
                {
                    GpuVendor.Nvidia => DriverVersionParser.NvidiaFromWindows(driverVersion),
                    GpuVendor.Amd => radeonVersion,
                    _ => null,
                },
                DriverDate = row.CimDate("DriverDate"),
                IsPhysicalPciDevice = physical,
                UsesBasicDisplayDriver = basicDriver && physical,
            });
        }

        return result;
    }

    private static (long? Memory, string? RadeonVersion) ReadDriverClassKey(string? pnpDeviceId)
    {
        if (pnpDeviceId is null)
        {
            return (null, null);
        }

        // Dati facoltativi: se una chiave non è leggibile si perde solo la VRAM, non la GPU.
        try
        {
            return ReadDriverClassKeyUnsafe(pnpDeviceId);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return (null, null);
        }
    }

    private static (long? Memory, string? RadeonVersion) ReadDriverClassKeyUnsafe(string pnpDeviceId)
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var device = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + pnpDeviceId);
        if (device?.GetValue("Driver") is not string driverKey)
        {
            return (null, null);
        }

        using var driverClass = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + driverKey);
        if (driverClass is null)
        {
            return (null, null);
        }

        var memory = ToSize(driverClass.GetValue("HardwareInformation.qwMemorySize"))
                     ?? ToSize(driverClass.GetValue("HardwareInformation.MemorySize"));
        return (memory, driverClass.GetValue("RadeonSoftwareVersion") as string);
    }

    // I driver salvano la dimensione come QWORD, DWORD o binario a seconda del produttore e della versione.
    private static long? ToSize(object? value) => value switch
    {
        long l when l > 0 => l,
        int i when i != 0 => (uint)i,
        byte[] { Length: >= 8 } b => BitConverter.ToInt64(b, 0) is > 0 and var v ? v : null,
        byte[] { Length: >= 4 } b => BitConverter.ToUInt32(b, 0) is > 0 and var v ? v : null,
        _ => null,
    };

    private static GpuVendor VendorFromName(string text) => text.ToUpperInvariant() switch
    {
        var t when t.Contains("NVIDIA") => GpuVendor.Nvidia,
        var t when t.Contains("AMD") || t.Contains("ADVANCED MICRO") || t.Contains("ATI ") || t.Contains("RADEON") => GpuVendor.Amd,
        var t when t.Contains("INTEL") => GpuVendor.Intel,
        _ => GpuVendor.Unknown,
    };
}
