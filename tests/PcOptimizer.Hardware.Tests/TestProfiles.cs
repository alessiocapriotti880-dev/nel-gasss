using PcOptimizer.Hardware.Model;

namespace PcOptimizer.Hardware.Tests;

/// <summary>PC di riferimento costruiti a mano, con i dati che Windows riporterebbe.</summary>
internal static class TestProfiles
{
    public const long GiB = 1024L * 1024 * 1024;

    private static readonly DateTimeOffset ScanTime = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>PC datato: i5-6500, GT 730, 2x4 GB DDR4-2133 JEDEC, HDD, scheda H110.</summary>
    public static HardwareProfile OldPc() => new()
    {
        ScannedAt = ScanTime,
        Cpu = new CpuInfo
        {
            Vendor = CpuVendor.Intel, VendorId = "GenuineIntel", BrandString = "Intel(R) Core(TM) i5-6500 CPU @ 3.20GHz",
            Family = 6, Model = 0x5E, Stepping = 3,
            PhysicalCores = 4, LogicalProcessors = 4, PerformanceCores = 4,
            BaseClockMHz = 3200, MaxClockMHz = 3600, MicrocodeRevision = 0xF0,
        },
        Gpus =
        [
            new GpuInfo
            {
                Name = "NVIDIA GeForce GT 730", Vendor = GpuVendor.Nvidia, PciVendorId = "10DE", PciDeviceId = "1287",
                IsPhysicalPciDevice = true, DedicatedMemoryBytes = 2 * GiB,
                DriverVersion = "30.0.14.7212", VendorDriverVersion = "472.12", DriverDate = new DateTime(2021, 9, 1),
            },
        ],
        Memory = new MemoryInfo
        {
            Modules =
            [
                Module("KVR21N15S8/4", 4, MemoryType.Ddr4, 2133, "ChannelA-DIMM0", "BANK 0"),
                Module("KVR21N15S8/4", 4, MemoryType.Ddr4, 2133, "ChannelB-DIMM0", "BANK 2"),
            ],
        },
        Disks = [SystemDisk("ST1000DM010-2EP102", DiskMediaType.Hdd, DiskBusType.Sata)],
        Board = new BoardInfo
        {
            BoardManufacturer = "ASUSTeK COMPUTER INC.", BoardProduct = "H110M-K", BiosVersion = "3805",
            BiosReleaseDate = new DateTime(2018, 5, 1), Firmware = FirmwareType.Uefi, SecureBootEnabled = true,
        },
        NetworkAdapters = [Ethernet()],
        Displays = [Display("HP 22w", 1920, 1080, 60, 60, "10DE", "1287")],
        Os = WindowsOs(19045, "Windows 10 Pro"),
    };

    /// <summary>PC medio: Ryzen 5 3600, GTX 1660 SUPER, 2x8 GB DDR4-3200 XMP, SSD SATA, scheda B450.</summary>
    public static HardwareProfile MidPc() => new()
    {
        ScannedAt = ScanTime,
        Cpu = new CpuInfo
        {
            Vendor = CpuVendor.Amd, VendorId = "AuthenticAMD", BrandString = "AMD Ryzen 5 3600 6-Core Processor",
            Family = 0x17, Model = 0x71, Stepping = 0,
            PhysicalCores = 6, LogicalProcessors = 12, PerformanceCores = 6, HasSmt = true,
            MicrocodeRevision = 0x8701021,
        },
        Gpus =
        [
            new GpuInfo
            {
                Name = "NVIDIA GeForce GTX 1660 SUPER", Vendor = GpuVendor.Nvidia, PciVendorId = "10DE", PciDeviceId = "21C4",
                IsPhysicalPciDevice = true, DedicatedMemoryBytes = 6 * GiB,
                DriverVersion = "32.0.15.6094", VendorDriverVersion = "560.94", DriverDate = new DateTime(2026, 8, 1),
            },
        ],
        Memory = new MemoryInfo
        {
            Modules =
            [
                Module("F4-3200C16-8GIS", 8, MemoryType.Ddr4, 3200, "DIMM 0", "P0 CHANNEL A"),
                Module("F4-3200C16-8GIS", 8, MemoryType.Ddr4, 3200, "DIMM 1", "P0 CHANNEL B"),
            ],
        },
        Disks = [SystemDisk("Samsung SSD 860 EVO 500GB", DiskMediaType.Ssd, DiskBusType.Sata)],
        Board = new BoardInfo
        {
            BoardManufacturer = "Micro-Star International Co., Ltd.", BoardProduct = "B450 TOMAHAWK MAX (MS-7C02)",
            BiosVersion = "1.E0", BiosReleaseDate = new DateTime(2024, 3, 1), Firmware = FirmwareType.Uefi, SecureBootEnabled = true,
        },
        NetworkAdapters = [Ethernet()],
        Displays = [Display("AOC 24G2", 1920, 1080, 144, 144, "10DE", "21C4")],
        Os = WindowsOs(22631, "Windows 10 Pro"),
    };

    /// <summary>PC di fascia alta: i9-14900KF, RTX 4080 SUPER, 2x16 GB DDR5-6000 XMP, NVMe, scheda Z790.</summary>
    public static HardwareProfile HighPc() => new()
    {
        ScannedAt = ScanTime,
        Cpu = new CpuInfo
        {
            Vendor = CpuVendor.Intel, VendorId = "GenuineIntel", BrandString = "Intel(R) Core(TM) i9-14900KF",
            Family = 6, Model = 0xB7, Stepping = 1,
            PhysicalCores = 24, LogicalProcessors = 32, PerformanceCores = 8, EfficiencyCores = 16, HasSmt = true,
            BaseClockMHz = 3200, MaxClockMHz = 6000, MicrocodeRevision = 0x12B, L3CacheBytes = 36L * 1024 * 1024,
        },
        Gpus =
        [
            new GpuInfo
            {
                Name = "NVIDIA GeForce RTX 4080 SUPER", Vendor = GpuVendor.Nvidia, PciVendorId = "10DE", PciDeviceId = "2702",
                IsPhysicalPciDevice = true, DedicatedMemoryBytes = 16 * GiB,
                DriverVersion = "32.0.15.8129", VendorDriverVersion = "581.29", DriverDate = new DateTime(2026, 9, 10),
            },
        ],
        Memory = new MemoryInfo
        {
            Modules =
            [
                Module("CMK32GX5M2B6000C30", 16, MemoryType.Ddr5, 6000, "DIMM_A2", "BANK 0", speed: 4800),
                Module("CMK32GX5M2B6000C30", 16, MemoryType.Ddr5, 6000, "DIMM_B2", "BANK 0", speed: 4800),
            ],
        },
        Disks = [SystemDisk("Samsung SSD 990 PRO 2TB", DiskMediaType.Ssd, DiskBusType.Nvme)],
        Board = new BoardInfo
        {
            BoardManufacturer = "ASUSTeK COMPUTER INC.", BoardProduct = "ROG STRIX Z790-E GAMING WIFI",
            BiosVersion = "2801", BiosReleaseDate = new DateTime(2025, 6, 1), Firmware = FirmwareType.Uefi, SecureBootEnabled = true,
        },
        NetworkAdapters = [Ethernet()],
        Displays = [Display("ROG XG27AQM", 2560, 1440, 240, 240, "10DE", "2702")],
        Os = WindowsOs(26100, "Windows 10 Pro"),
    };

    public static MemoryModule Module(
        string partNumber, int gb, MemoryType type, int configured, string locator, string bank, int? speed = null) => new()
    {
        PartNumber = partNumber,
        CapacityBytes = gb * GiB,
        Type = type,
        ConfiguredSpeedMTs = configured,
        SpeedMTs = speed ?? configured,
        DeviceLocator = locator,
        BankLabel = bank,
    };

    public static DiskInfo SystemDisk(string name, DiskMediaType media, DiskBusType bus) => new()
    {
        DeviceId = "0", FriendlyName = name, MediaType = media, BusType = bus,
        SizeBytes = 1000 * GiB, Health = DiskHealth.Healthy, IsSystemDisk = true,
    };

    public static NetworkAdapterInfo Ethernet(long bps = 1_000_000_000) => new()
    {
        Name = "Ethernet", Description = "Intel(R) Ethernet Controller I225-V", Medium = NetworkMedium.Ethernet,
        LinkSpeedBps = bps, IsConnected = true, IsPrimary = true,
    };

    public static DisplayInfo Display(string name, int w, int h, double hz, int? maxHz, string ven, string dev) => new()
    {
        Name = name, Width = w, Height = h, RefreshHz = hz, MaxRefreshHz = maxHz,
        Connection = DisplayConnection.DisplayPort, IsPrimary = true, AdapterPciVendorId = ven, AdapterPciDeviceId = dev,
    };

    public static OsInfo WindowsOs(int build, string product) => new()
    {
        ProductName = product, Build = build, DisplayVersion = build >= 22000 ? "24H2" : "22H2",
        SystemDriveTotalBytes = 1000 * GiB, SystemDriveFreeBytes = 500 * GiB,
    };
}
