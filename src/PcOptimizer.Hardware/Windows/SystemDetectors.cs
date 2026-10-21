using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Microsoft.Win32;
using PcOptimizer.Core.Windows;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Hardware.Parsing;

namespace PcOptimizer.Hardware.Windows;

/// <summary>RAM: moduli da SMBIOS (Win32_PhysicalMemory) e memoria visibile a Windows.</summary>
[SupportedOSPlatform("windows")]
internal static class MemoryDetector
{
    public static MemoryInfo Detect()
    {
        var modules = Wmi.Query(Wmi.Cimv2,
                "SELECT Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, SMBIOSMemoryType, " +
                "DeviceLocator, BankLabel, ConfiguredVoltage FROM Win32_PhysicalMemory")
            .Select(row =>
            {
                var type = MemoryParser.FromSmbiosType(row.Int("SMBIOSMemoryType"));
                return new MemoryModule
                {
                    Manufacturer = row.Str("Manufacturer"),
                    PartNumber = row.Str("PartNumber"),
                    CapacityBytes = row.Long("Capacity") ?? 0,
                    SpeedMTs = MemoryParser.NormalizeDataRate(row.Int("Speed"), type),
                    ConfiguredSpeedMTs = MemoryParser.NormalizeDataRate(row.Int("ConfiguredClockSpeed"), type),
                    Type = type,
                    DeviceLocator = row.Str("DeviceLocator"),
                    BankLabel = row.Str("BankLabel"),
                    ConfiguredVoltageMv = row.Int("ConfiguredVoltage") is > 0 and var mv ? mv : null,
                };
            })
            .ToList();

        var visible = Wmi.Query(Wmi.Cimv2, "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem")
            .Select(r => r.Long("TotalPhysicalMemory") ?? 0)
            .FirstOrDefault();

        return new MemoryInfo { Modules = modules, VisibleBytes = visible };
    }
}

/// <summary>Dischi: Storage Management API (MSFT_PhysicalDisk) e partizione di Windows.</summary>
[SupportedOSPlatform("windows")]
internal static class StorageDetector
{
    public static IReadOnlyList<DiskInfo> Detect()
    {
        var systemLetter = char.ToUpperInvariant((Environment.GetEnvironmentVariable("SystemDrive") ?? "C:")[0]);
        string? systemDisk = null;
        foreach (var row in Wmi.Query(Wmi.Storage, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
        {
            if (row.TryGetValue("DriveLetter", out var letter) && ToLetter(letter) == systemLetter)
            {
                systemDisk = row.Str("DiskNumber");
            }
        }

        return Wmi.Query(Wmi.Storage,
                "SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus, FirmwareVersion FROM MSFT_PhysicalDisk")
            .Select(row => new DiskInfo
            {
                DeviceId = row.Str("DeviceId") ?? "",
                FriendlyName = row.Str("FriendlyName") ?? "Disco",
                MediaType = StorageParser.MediaType(row.Int("MediaType")),
                BusType = StorageParser.BusType(row.Int("BusType")),
                SizeBytes = row.Long("Size") ?? 0,
                Health = StorageParser.Health(row.Int("HealthStatus")),
                IsSystemDisk = systemDisk is not null && row.Str("DeviceId") == systemDisk,
                FirmwareVersion = row.Str("FirmwareVersion"),
            })
            .ToList();
    }

    // DriveLetter è un CIM Char16: a seconda della versione arriva come char o come numero.
    private static char? ToLetter(object? value) => value switch
    {
        char c when c != '\0' => char.ToUpperInvariant(c),
        ushort u when u != 0 => char.ToUpperInvariant((char)u),
        string { Length: > 0 } s => char.ToUpperInvariant(s[0]),
        _ => null,
    };
}

/// <summary>Scheda madre, BIOS/UEFI, Secure Boot, tipo di PC e alimentazione.</summary>
[SupportedOSPlatform("windows")]
internal static class BoardDetector
{
    public static BoardInfo Detect()
    {
        var empty = new Dictionary<string, object?>();
        var board = Wmi.Query(Wmi.Cimv2, "SELECT Manufacturer, Product, Version FROM Win32_BaseBoard").FirstOrDefault() ?? empty;
        var bios = Wmi.Query(Wmi.Cimv2, "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault() ?? empty;
        var system = Wmi.Query(Wmi.Cimv2, "SELECT Manufacturer, Model, PCSystemType FROM Win32_ComputerSystem").FirstOrDefault() ?? empty;
        var chassis = Wmi.Query(Wmi.Cimv2, "SELECT ChassisTypes FROM Win32_SystemEnclosure")
            .SelectMany(r => r.TryGetValue("ChassisTypes", out var v) && v is ushort[] types ? types.Select(t => (int)t) : [])
            .ToList();

        var isLaptop = FormFactorParser.IsLaptop(system.Int("PCSystemType"), chassis);
        bool? onBattery = null;
        if (isLaptop && NativeMethods.GetSystemPowerStatus(out var power))
        {
            onBattery = power.AcLineStatus switch
            {
                0 => true,
                1 => false,
                _ => null,
            };
        }

        return new BoardInfo
        {
            BoardManufacturer = board.Str("Manufacturer") ?? "",
            BoardProduct = board.Str("Product") ?? "",
            BoardVersion = board.Str("Version"),
            SystemManufacturer = system.Str("Manufacturer") ?? "",
            SystemModel = system.Str("Model") ?? "",
            BiosVendor = bios.Str("Manufacturer") ?? "",
            BiosVersion = bios.Str("SMBIOSBIOSVersion") ?? "",
            BiosReleaseDate = bios.CimDate("ReleaseDate"),
            Firmware = ReadFirmwareType(),
            SecureBootEnabled = ReadSecureBoot(),
            IsLaptop = isLaptop,
            OnBattery = onBattery,
        };
    }

    private static FirmwareType ReadFirmwareType() =>
        NativeMethods.GetFirmwareType(out var type)
            ? type switch
            {
                1 => FirmwareType.LegacyBios,
                2 => FirmwareType.Uefi,
                _ => FirmwareType.Unknown,
            }
            : FirmwareType.Unknown;

    private static bool? ReadSecureBoot()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
        return key?.GetValue("UEFISecureBootEnabled") is int value ? value != 0 : null;
    }
}

/// <summary>
/// Rete: schede fisiche da MSFT_NetAdapter; la scheda "primaria" è quella che Windows usa
/// per raggiungere Internet (GetBestInterface verso 8.8.8.8: solo un calcolo di route, nessun pacchetto inviato).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NetworkDetector
{
    public static IReadOnlyList<NetworkAdapterInfo> Detect()
    {
        int? primaryIndex = NativeMethods.GetBestInterface(0x08080808, out var best) == 0 ? (int)best : null;
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();

        List<Dictionary<string, object?>> rows;
        try
        {
            rows = Wmi.Query(Wmi.StandardCimv2,
                "SELECT Name, InterfaceDescription, InterfaceGuid, NdisPhysicalMedium, Speed, MediaConnectState, " +
                "DriverVersionString, DriverDate, PnPDeviceID FROM MSFT_NetAdapter WHERE HardwareInterface = TRUE");
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or
                                       System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            rows = []; // si ripiega sulle informazioni di .NET (senza versione del driver)
        }

        if (rows.Count == 0)
        {
            return interfaces
                .Where(n => n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Where(n => !NetworkParser.LooksVirtual(n.Description))
                .Select(n => new NetworkAdapterInfo
                {
                    Name = n.Name,
                    Description = n.Description,
                    Medium = NetworkParser.FromInterfaceType(n.NetworkInterfaceType),
                    LinkSpeedBps = n.Speed > 0 ? n.Speed : null,
                    IsConnected = n.OperationalStatus == OperationalStatus.Up,
                    IsPrimary = primaryIndex is not null && Ipv4Index(n) == primaryIndex,
                    InterfaceId = n.Id,
                })
                .ToList();
        }

        return rows.Select(row =>
        {
            var guid = row.Str("InterfaceGuid");
            var ni = interfaces.FirstOrDefault(n => string.Equals(n.Id, guid, StringComparison.OrdinalIgnoreCase));
            var medium = ni is null ? NetworkMedium.Unknown : NetworkParser.FromInterfaceType(ni.NetworkInterfaceType);
            if (medium is NetworkMedium.Unknown or NetworkMedium.Other)
            {
                medium = NetworkParser.FromNdisMedium(row.Int("NdisPhysicalMedium"));
            }

            return new NetworkAdapterInfo
            {
                Name = row.Str("Name") ?? ni?.Name ?? "",
                Description = row.Str("InterfaceDescription") ?? ni?.Description ?? "",
                Medium = medium,
                LinkSpeedBps = row.Long("Speed") is > 0 and var speed ? speed : null,
                IsConnected = row.Int("MediaConnectState") == 1 || ni?.OperationalStatus == OperationalStatus.Up,
                IsPrimary = ni is not null && primaryIndex is not null && Ipv4Index(ni) == primaryIndex,
                DriverVersion = row.Str("DriverVersionString"),
                DriverDate = row.Str("DriverDate"),
                PnpDeviceId = row.Str("PnPDeviceID"),
                InterfaceId = guid,
            };
        }).ToList();
    }

    private static int? Ipv4Index(NetworkInterface ni)
    {
        try
        {
            return ni.GetIPProperties().GetIPv4Properties()?.Index;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }
}

/// <summary>Sistema operativo e spazio sul disco di sistema.</summary>
[SupportedOSPlatform("windows")]
internal static class OsDetector
{
    public static OsInfo Detect()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var b) ? b : Environment.OSVersion.Version.Build;

        long total = 0, free = 0;
        try
        {
            var drive = new DriveInfo(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:");
            (total, free) = (drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Lasciamo 0: le osservazioni sullo spazio libero vengono saltate.
        }

        return new OsInfo
        {
            ProductName = key?.GetValue("ProductName") as string ?? "Windows",
            DisplayVersion = (key?.GetValue("DisplayVersion") ?? key?.GetValue("ReleaseId")) as string,
            Build = build,
            Ubr = key?.GetValue("UBR") is int ubr ? ubr : 0,
            Edition = key?.GetValue("EditionID") as string,
            SystemDriveTotalBytes = total,
            SystemDriveFreeBytes = free,
        };
    }
}
