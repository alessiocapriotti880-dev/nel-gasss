using PcOptimizer.Analysis;
using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Evaluation;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Tweaks.Context;

namespace PcOptimizer.Tweaks.Tests;

/// <summary>Sistemi di prova: un desktop Windows 11 con valori predefiniti, da variare nei singoli test.</summary>
internal static class Fixtures
{
    public const long GiB = 1024L * 1024 * 1024;
    public static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");
    public static readonly Guid NetClass = new("4d36e972-e325-11ce-bfc1-08002be10318");
    public static readonly Guid MediaClass = new("4d36e96c-e325-11ce-bfc1-08002be10318");
    public static readonly Guid StorageClass = new("4d36e97b-e325-11ce-bfc1-08002be10318");
    public static readonly KnownSoftwareCatalog Software = KnownSoftwareCatalog.Load();

    public static HardwareProfile Profile(bool laptop = false, DiskMediaType systemDisk = DiskMediaType.Ssd, string cpu = "Intel(R) Core(TM) i5-12400F",
        int pCores = 6, int eCores = 0, int ramGb = 16, params GpuInfo[] gpus) => new()
    {
        Cpu = new CpuInfo { BrandString = cpu, PerformanceCores = pCores, EfficiencyCores = eCores, HasSmt = true, PhysicalCores = pCores + eCores, LogicalProcessors = pCores * 2 + eCores },
        Gpus = gpus.Length > 0 ? gpus : [new GpuInfo { Name = "NVIDIA GeForce RTX 3060", Vendor = GpuVendor.Nvidia, IsPhysicalPciDevice = true }],
        Memory = new MemoryInfo { VisibleBytes = ramGb * GiB },
        Disks = [new DiskInfo { FriendlyName = "Disco", MediaType = systemDisk, IsSystemDisk = true }],
        Board = new BoardInfo { IsLaptop = laptop },
        NetworkAdapters = [new NetworkAdapterInfo { Name = "Ethernet", Description = "Realtek PCIe GbE", Medium = NetworkMedium.Ethernet, IsPrimary = true, IsConnected = true, InterfaceId = "{11111111-2222-3333-4444-555555555555}" }],
        Os = new OsInfo { Build = 22631 },
    };

    public static SystemSnapshot Snapshot() => new()
    {
        IsElevated = true,
        Services =
        [
            Service("wuauserv", ServiceStartValues.Manual),
            Service("WinDefend", ServiceStartValues.Automatic),
            Service("DiagTrack", ServiceStartValues.Automatic),
            Service("SysMain", ServiceStartValues.Automatic),
            Service("WSearch", ServiceStartValues.AutomaticDelayed),
        ],
        Startup =
        [
            new StartupEntry { Name = "SecurityHealth", Source = StartupSource.RegistryMachine, Company = "Microsoft Corporation", Enabled = true, ApprovalName = "SecurityHealth" },
        ],
        ScheduledTasks =
        [
            new ScheduledTaskEntry { Path = @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser", Enabled = true },
            new ScheduledTaskEntry { Path = @"\Microsoft\Windows\Defrag\ScheduledDefrag", Enabled = true },
        ],
        Power = new PowerSnapshot
        {
            ActiveScheme = PowerGuids.Balanced,
            ActiveSchemeName = "Bilanciato",
            Schemes = [new(PowerGuids.Balanced, "Bilanciato"), new(PowerGuids.HighPerformance, "Prestazioni elevate")],
        },
        GpuScheduling = [new GpuSchedulingEntry { AdapterName = "NVIDIA GeForce RTX 3060", HwSchSupported = true, HwSchEnabled = false }],
        Registry = new Dictionary<string, SettingValue>
        {
            [RegistryProbes.GameMode.Id] = SettingValue.Of(1u),
            [RegistryProbes.HistoricalCapture.Id] = SettingValue.Of(1u),
            [RegistryProbes.DirectXGlobal.Id] = SettingValue.Of("VRROptimizeEnable=0;"),
            [RegistryProbes.MouseSpeed.Id] = SettingValue.Of("1"),
        },
        PageFile = new PageFileSnapshot { AutomaticManaged = true, Files = [new PageFileEntry(@"C:\pagefile.sys", 4096)] },
    };

    public static ServiceEntry Service(string name, string mode, string? display = null) =>
        new() { Name = name, DisplayName = display ?? name, StartMode = mode, State = "Running" };

    public static TweakEnvironment Environment(Guid? scheme = null) => new()
    {
        PowerOverlaySupported = true,
        ActivePowerScheme = scheme,
        PrimaryAdapter = new NetworkAdapterConfig("Realtek PCIe GbE", "{11111111-2222-3333-4444-555555555555}",
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\0001", false,
            [
                new AdapterParameter("*EEE", "Energy Efficient Ethernet", "1", "1", "enum", ["0", "1"], null, null),
                new AdapterParameter("EnableGreenEthernet", "Green Ethernet", "1", "1", "enum", ["0", "1"], null, null),
                new AdapterParameter("*InterruptModeration", "Interrupt Moderation", "1", "1", "enum", ["0", "1"], null, null),
                new AdapterParameter("*ReceiveBuffers", "Receive Buffers", "512", "512", "int", [], 80, 1024),
                new AdapterParameter("*TransmitBuffers", "Transmit Buffers", "128", "128", "int", [], 80, 1024),
                new AdapterParameter("PowerSavingMode", "Power Saving Mode", "1", "1", "enum", ["0", "1"], null, null),
            ]),
        Displays = [new DisplayModes(@"\\.\DISPLAY1", 60, [60, 120, 144])],
        Registry = new Dictionary<string, SettingValue>(),
        LaunchScriptFolder = @"C:\Users\Prova\Desktop",
    };

    public static TweakContext Context(
        HardwareTier tier = HardwareTier.Mid, int build = 22631, HardwareProfile? profile = null, SystemSnapshot? snapshot = null,
        TweakEnvironment? environment = null, GameLibrary? games = null)
    {
        profile ??= Profile();
        snapshot ??= Snapshot();
        var analysisContext = new AnalysisContext
        {
            IsLaptop = profile.Board.IsLaptop,
            WindowsBuild = build,
            TotalRamBytes = profile.Memory.VisibleBytes,
            Tier = tier,
            GpuCount = profile.Gpus.Count,
        };
        var report = new SystemAnalyzer(Software).Analyze(snapshot, analysisContext);
        return new TweakContext(tier, build)
            .With(profile)
            .With(snapshot)
            .With(report)
            .With(environment ?? Environment())
            .With(Software)
            .With(games ?? GameLibrary.Empty);
    }

    public static PciDeviceEntry Pci(string name, Guid classGuid, params long[] irqs) => new()
    {
        Name = name,
        PnpDeviceId = $@"PCI\VEN_10DE&DEV_2504&SUBSYS_00000000&REV_A1\4&{name.Length}",
        ClassGuid = classGuid,
        Irqs = irqs,
    };
}

/// <summary>Sistema finto in memoria per provare applicazione e rollback dell'intero catalogo.</summary>
internal sealed class InMemoryProvider : ISettingProvider
{
    public Dictionary<SettingTarget, SettingValue> Values { get; } = [];

    public bool CanHandle(SettingTarget target) => true;

    public SettingValue Read(SettingTarget target) => Values.TryGetValue(target, out var v) ? v : SettingValue.Absent;

    public void Write(SettingTarget target, SettingValue value)
    {
        if (value.Exists)
        {
            Values[target] = value;
        }
        else
        {
            Values.Remove(target);
        }
    }
}
