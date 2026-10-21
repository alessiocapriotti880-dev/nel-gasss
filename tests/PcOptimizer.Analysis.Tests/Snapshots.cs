using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Analysis.Tests;

/// <summary>Istantanee di prova: un Windows 11 "pulito" con i valori predefiniti, da modificare nei singoli test.</summary>
internal static class Snapshots
{
    public const long GiB = 1024L * 1024 * 1024;

    private static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");
    private static readonly Guid NetClass = new("4d36e972-e325-11ce-bfc1-08002be10318");
    private static readonly Guid MediaClass = new("4d36e96c-e325-11ce-bfc1-08002be10318");

    public const long MsiIrq = 4294967286; // -10 come intero a 32 bit: interrupt MSI

    public static AnalysisContext Desktop(HardwareTier tier = HardwareTier.High, long ramGb = 32, int gpus = 1) => new()
    {
        WindowsBuild = 26100,
        TotalRamBytes = ramGb * GiB,
        Tier = tier,
        GpuCount = gpus,
    };

    /// <summary>Configurazione predefinita di Windows: nessuna osservazione rilevante attesa.</summary>
    public static SystemSnapshot Clean() => new()
    {
        IsElevated = true,
        Services =
        [
            Service("wuauserv", ServiceStartValues.Manual, "Stopped"),
            Service("WinDefend", ServiceStartValues.Automatic, "Running"),
            Service("Audiosrv", ServiceStartValues.Automatic, "Running"),
        ],
        Startup =
        [
            new StartupEntry
            {
                Name = "SecurityHealth", Source = StartupSource.RegistryMachine, Company = "Microsoft Corporation", Enabled = true,
                Command = @"%windir%\system32\SecurityHealthSystray.exe",
            },
        ],
        ScheduledTasks = [new ScheduledTaskEntry { Path = @"\Microsoft\Windows\Defrag\ScheduledDefrag", Enabled = true }],
        Processes = [Process("explorer", 150), Process("MsMpEng", 250)],
        Power = new PowerSnapshot
        {
            ActiveScheme = PowerGuids.Balanced,
            ActiveSchemeName = "Bilanciato",
            Schemes = [new(PowerGuids.Balanced, "Bilanciato"), new(PowerGuids.PowerSaver, "Risparmio di energia"), new(PowerGuids.HighPerformance, "Prestazioni elevate")],
            Values =
            [
                new(PowerSettingsCatalog.ProcessorMin, 5, 5),
                new(PowerSettingsCatalog.ProcessorMax, 100, 100),
                new(PowerSettingsCatalog.BoostMode, 2, 1),
                new(PowerSettingsCatalog.IdleDisable, 0, 0),
                new(PowerSettingsCatalog.CoreParkingMinCores, 10, 10),
                new(PowerSettingsCatalog.UsbSelectiveSuspend, 1, 1),
                new(PowerSettingsCatalog.PcieAspm, 1, 2),
            ],
        },
        Timer = new TimerSnapshot { CurrentMs = 15.625, FinestMs = 0.5, CoarsestMs = 15.625, BcdReadable = true, HpetDeviceEnabled = true },
        PciDevices =
        [
            Pci("NVIDIA GeForce RTX 4070", DisplayClass, MsiIrq),
            Pci("Intel(R) Ethernet Controller I226-V", NetClass, MsiIrq - 1),
            Pci("High Definition Audio Controller", MediaClass, MsiIrq - 2),
        ],
        GpuScheduling = [new GpuSchedulingEntry { AdapterName = "NVIDIA GeForce RTX 4070", HwSchSupported = true, HwSchEnabled = true, HwSchEnabledByDefault = true, WddmVersion = 3100 }],
        Registry = DefaultRegistry(),
        Security = new SecuritySnapshot
        {
            VbsStatus = 0, HypervisorPresent = false, DefenderRealTimeProtection = true, DefenderAntivirusEnabled = true,
            AntivirusProducts = [new AntivirusEntry("Windows Defender", true)], TpmPresent = true, TpmSpecVersion = "2.0, 0, 1.59",
        },
        PageFile = new PageFileSnapshot { AutomaticManaged = true, Files = [new PageFileEntry(@"C:\pagefile.sys", 16384)] },
    };

    /// <summary>Valori predefiniti delle modifiche note (quelli che Windows ha già scritti nel registro).</summary>
    public static Dictionary<string, SettingValue> DefaultRegistry() =>
        RegistryProbes.KnownTweaks
            .Where(t => t.Defaults.FirstOrDefault(d => d is not null) is not null && !t.Defaults.Contains(null))
            .ToDictionary(t => t.Probe.Id, t => SettingValue.Of(t.Defaults.First(d => d is not null)!));

    public static SystemSnapshot WithRegistry(this SystemSnapshot s, params (string Id, string? Value)[] values)
    {
        var registry = new Dictionary<string, SettingValue>(s.Registry);
        foreach (var (id, value) in values)
        {
            registry[id] = value is null ? SettingValue.Absent : SettingValue.Of(value);
        }

        return s with { Registry = registry };
    }

    public static SystemSnapshot WithPower(this SystemSnapshot s, string key, uint ac) => s with
    {
        Power = s.Power with { Values = s.Power.Values.Where(v => v.Key != key).Append(new PowerValue(key, ac, ac)).ToList() },
    };

    public static ServiceEntry Service(string name, string startMode, string state, string? company = "Microsoft Corporation") => new()
    {
        Name = name, DisplayName = name, StartMode = startMode, State = state, Company = company,
    };

    public static ProcessEntry Process(string name, long privateMb) => new()
    {
        Name = name, Id = name.GetHashCode() & 0xFFFF, PrivateBytes = privateMb * 1024 * 1024, WorkingSetBytes = privateMb * 1024 * 1024,
    };

    public static PciDeviceEntry Pci(string name, Guid classGuid, params long[] irqs) => new()
    {
        Name = name, PnpDeviceId = @"PCI\VEN_0000&DEV_" + name.Length.ToString("X4"), ClassGuid = classGuid, Irqs = irqs,
    };

    public static Guid Display => DisplayClass;

    public static Guid Media => MediaClass;
}
