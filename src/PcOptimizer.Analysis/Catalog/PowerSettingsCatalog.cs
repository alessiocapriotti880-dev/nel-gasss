using PcOptimizer.Core.Settings;

namespace PcOptimizer.Analysis.Catalog;

/// <summary>Impostazione energetica letta dal piano attivo.</summary>
public sealed record PowerSettingDefinition(string Key, Guid SubGroup, Guid Setting, string Label);

/// <summary>Impostazioni del piano energetico rilevanti per latenza e prestazioni.</summary>
public static class PowerSettingsCatalog
{
    public const string ProcessorMin = "processor.min";
    public const string ProcessorMax = "processor.max";
    public const string CoreParkingMinCores = "processor.parking.mincores";
    public const string BoostMode = "processor.boost";
    public const string IdleDisable = "processor.idledisable";
    public const string EnergyPerformancePreference = "processor.epp";
    public const string UsbSelectiveSuspend = "usb.selectivesuspend";
    public const string PcieAspm = "pcie.aspm";
    public const string DiskIdle = "disk.idle";

    public static IReadOnlyList<PowerSettingDefinition> Settings { get; } =
    [
        new(ProcessorMin, PowerGuids.SubProcessor, PowerGuids.ProcessorMinState, "Stato minimo del processore"),
        new(ProcessorMax, PowerGuids.SubProcessor, PowerGuids.ProcessorMaxState, "Stato massimo del processore"),
        new(CoreParkingMinCores, PowerGuids.SubProcessor, PowerGuids.CoreParkingMinCores, "Core parking: core minimi attivi"),
        new(BoostMode, PowerGuids.SubProcessor, PowerGuids.BoostMode, "Modalità turbo del processore"),
        new(IdleDisable, PowerGuids.SubProcessor, PowerGuids.IdleDisable, "Disattivazione inattività del processore"),
        new(EnergyPerformancePreference, PowerGuids.SubProcessor, PowerGuids.EnergyPerformancePreference, "Preferenza prestazioni/energia"),
        new(UsbSelectiveSuspend, PowerGuids.SubUsb, PowerGuids.UsbSelectiveSuspend, "Sospensione selettiva USB"),
        new(PcieAspm, PowerGuids.SubPciExpress, PowerGuids.PcieAspm, "Risparmio energia collegamento PCI Express"),
        new(DiskIdle, PowerGuids.SubDisk, PowerGuids.DiskIdle, "Spegnimento disco rigido dopo"),
    ];
}
