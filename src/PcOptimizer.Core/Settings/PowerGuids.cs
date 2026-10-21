namespace PcOptimizer.Core.Settings;

/// <summary>
/// GUID standard di Windows per piani e impostazioni energetiche (gli stessi alias di powercfg).
/// Condivisi tra analisi (lettura) e interventi (scrittura con rollback).
/// </summary>
public static class PowerGuids
{
    // Piani energetici predefiniti.
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    // Processore (SUB_PROCESSOR).
    public static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    public static readonly Guid ProcessorMinState = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    public static readonly Guid ProcessorMaxState = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    public static readonly Guid CoreParkingMinCores = new("0cc5b647-c1df-4637-891a-dec35c318583");
    public static readonly Guid BoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");
    public static readonly Guid IdleDisable = new("5d76a2ca-e8c0-402f-a133-2158492d58ad");
    public static readonly Guid EnergyPerformancePreference = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");

    // USB, PCI Express, disco.
    public static readonly Guid SubUsb = new("2a737441-1930-4402-8d77-b2bebba308a3");
    public static readonly Guid UsbSelectiveSuspend = new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226");
    public static readonly Guid SubPciExpress = new("501a4d13-42af-4429-9fd1-a8218c268e20");
    public static readonly Guid PcieAspm = new("ee12f906-d277-404b-b6da-e5fa1a576df5");
    public static readonly Guid SubDisk = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
    public static readonly Guid DiskIdle = new("6738e2c4-e8a5-4a42-b16a-e040e769756e");

    // Modalità di alimentazione (cursore di Windows 10/11) applicata sopra il piano Bilanciato.
    public static readonly Guid OverlayBestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid OverlayBetterPerformance = new("3af9b8d9-7c97-431d-ad78-34a8bfea439f");
    public static readonly Guid OverlayBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
}
