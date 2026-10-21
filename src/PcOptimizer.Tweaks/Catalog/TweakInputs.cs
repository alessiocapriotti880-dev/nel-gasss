using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Tweaks.Context;

namespace PcOptimizer.Tweaks.Catalog;

/// <summary>Accesso ai dati del contesto usati dagli interventi (con valori vuoti se un dato manca).</summary>
internal static class TweakInputs
{
    public static SystemSnapshot Snapshot(this TweakContext c) => c.Get<SystemSnapshot>() ?? new SystemSnapshot();

    public static SystemReport? Report(this TweakContext c) => c.Get<SystemReport>();

    public static HardwareProfile Profile(this TweakContext c) => c.Get<HardwareProfile>() ?? new HardwareProfile();

    public static TweakEnvironment Env(this TweakContext c) => c.Get<TweakEnvironment>() ?? new TweakEnvironment();

    public static GameLibrary Games(this TweakContext c) => c.Get<GameLibrary>() ?? GameLibrary.Empty;

    public static KnownSoftwareCatalog? Software(this TweakContext c) => c.Get<KnownSoftwareCatalog>();

    public static bool IsLaptop(this TweakContext c) => c.Profile().Board.IsLaptop;

    public static bool HasHdd(this TweakContext c) => c.Profile().Disks.Any(d => d.MediaType == DiskMediaType.Hdd);

    public static bool SystemOnHdd(this TweakContext c) => c.Profile().Disks.Any(d => d.IsSystemDisk && d.MediaType == DiskMediaType.Hdd);

    public static bool HasSsd(this TweakContext c) => c.Profile().Disks.Any(d => d.MediaType is DiskMediaType.Ssd or DiskMediaType.Scm);

    public static double RamGb(this TweakContext c) => c.Profile().Memory.VisibleBytes / (1024.0 * 1024 * 1024);

    /// <summary>Piano energetico su cui scrivere: quello attivo aggiornato dalla sessione, altrimenti quello dell'analisi.</summary>
    public static Guid? ActiveScheme(this TweakContext c) => c.Env().ActivePowerScheme ?? c.Snapshot().Power.ActiveScheme;
}

/// <summary>Costruzione dei target di registro.</summary>
internal static class Reg
{
    public static RegistryValueTarget Machine(string key, string value, RegValueKind kind = RegValueKind.DWord) =>
        new(RegHive.LocalMachine, key, value, kind);

    public static RegistryValueTarget User(string key, string value, RegValueKind kind = RegValueKind.DWord) =>
        new(RegHive.CurrentUser, key, value, kind);

    public static SettingChange Set(RegistryValueTarget target, uint value) => new(target, SettingValue.Of(value));

    public static SettingChange Set(RegistryValueTarget target, string value) => new(target, SettingValue.Of(value));

    public static SettingChange Delete(RegistryValueTarget target) => new(target, SettingValue.Absent);
}

/// <summary>Valori di registro letti in più per decidere l'applicabilità di alcuni interventi.</summary>
public static class TweakProbes
{
    public const string HvciLocked = "security.hvci.locked";
    public const string HvciPolicy = "security.hvci.policy";
    public const string DisableDeleteNotify = "storage.trim";
    public const string PagingFiles = "memory.pagingfiles";

    public static readonly IReadOnlyDictionary<string, RegistryValueTarget> All = new Dictionary<string, RegistryValueTarget>
    {
        [HvciLocked] = Reg.Machine(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Locked"),
        [HvciPolicy] = Reg.Machine(@"SOFTWARE\Policies\Microsoft\Windows\DeviceGuard", "HypervisorEnforcedCodeIntegrity"),
        [DisableDeleteNotify] = Reg.Machine(@"SYSTEM\CurrentControlSet\Control\FileSystem", "DisableDeleteNotification"),
        [PagingFiles] = Reg.Machine(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "PagingFiles", RegValueKind.MultiString),
    };
}
