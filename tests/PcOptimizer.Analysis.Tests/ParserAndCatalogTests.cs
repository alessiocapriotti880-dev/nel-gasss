using System.Runtime.InteropServices;
using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Analysis.Parsing;
using PcOptimizer.Analysis.Windows;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Analysis.Tests;

public sealed class ParserTests
{
    private const string Root = @"C:\Windows";

    [Theory]
    [InlineData(@"C:\WINDOWS\system32\svchost.exe -k netsvcs -p", @"C:\WINDOWS\system32\svchost.exe")]
    [InlineData(@"""C:\Program Files\NVIDIA Corporation\Display.NvContainer\NVDisplay.Container.exe"" -s NVDisplay.ContainerLocalSystem",
        @"C:\Program Files\NVIDIA Corporation\Display.NvContainer\NVDisplay.Container.exe")]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe -silent", @"C:\Program Files (x86)\Steam\steam.exe")]
    [InlineData(@"\SystemRoot\System32\drivers\vgk.sys", @"C:\Windows\System32\drivers\vgk.sys")]
    [InlineData(@"System32\drivers\WinRing0x64.sys", @"C:\Windows\System32\drivers\WinRing0x64.sys")]
    [InlineData(@"\??\C:\Program Files\FanControl\WinRing0x64.sys", @"C:\Program Files\FanControl\WinRing0x64.sys")]
    [InlineData(@"%SystemRoot%\System32\svchost.exe -k LocalService", @"C:\Windows\System32\svchost.exe")]
    [InlineData(@"rundll32.exe C:\Tools\x.dll,Start", "rundll32.exe")]
    [InlineData("", null)]
    public void Executable_path_is_extracted_from_commands(string command, string? expected) =>
        Assert.Equal(expected, ImagePathParser.ExecutablePath(command, Root));

    [Fact]
    public void Startup_approved_state_follows_task_manager_encoding()
    {
        Assert.True(StartupStateParser.IsApprovedEnabled(null));
        Assert.True(StartupStateParser.IsApprovedEnabled([0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.True(StartupStateParser.IsApprovedEnabled([0x06, 0, 0, 0]));
        Assert.False(StartupStateParser.IsApprovedEnabled([0x03, 0, 0, 0, 0x10, 0x8A, 0x2B, 0x1C, 0xF0, 0x3A, 0xDA, 0x01]));
        Assert.True(StartupStateParser.IsStoreTaskEnabled(2));
        Assert.False(StartupStateParser.IsStoreTaskEnabled(1));
    }

    [Fact]
    public void Irq_allocations_are_parsed_from_wmi_references()
    {
        var allocation = IrqParser.ParseAllocation(
            @"\\PC\root\cimv2:Win32_IRQResource.IRQNumber=4294967286",
            @"\\PC\root\cimv2:Win32_PnPEntity.DeviceID=""PCI\\VEN_10DE&DEV_2704&SUBSYS_88A01043&REV_A1\\4&2B6B2ACD&0&0008""");

        Assert.Equal((4294967286L, @"PCI\VEN_10DE&DEV_2704&SUBSYS_88A01043&REV_A1\4&2B6B2ACD&0&0008"), allocation);
        Assert.Null(IrqParser.ParseAllocation(@"\\PC\root\cimv2:Win32_DeviceMemoryAddress.StartingAddress=1", @"x"));
        Assert.Equal(InterruptMode.Msi, IrqParser.Mode([4294967286L]));
        Assert.Equal(InterruptMode.LineBased, IrqParser.Mode([16]));
        Assert.Equal(InterruptMode.Unknown, IrqParser.Mode([]));
    }

    [Fact]
    public void Bcd_elements_are_parsed_from_english_and_localized_output()
    {
        const string output = """

            Caricatore di avvio di Windows
            -------------------------------
            identificatore          {current}
            device                  partition=C:
            path                    \Windows\system32\winload.efi
            description             Windows 11
            useplatformclock        Sì
            disabledynamictick      No
            tscsyncpolicy           Enhanced
            """;

        var elements = BcdParser.ParseElements(output, ["useplatformclock", "disabledynamictick", "tscsyncpolicy", "useplatformtick"]);

        Assert.Equal(3, elements.Count);
        Assert.Equal(true, BcdParser.ToBool(elements["useplatformclock"]));
        Assert.Equal(false, BcdParser.ToBool(elements["disabledynamictick"]));
        Assert.Equal("Enhanced", elements["tscsyncpolicy"]);
        Assert.Equal(true, BcdParser.ToBool("Yes"));
        Assert.Null(BcdParser.ToBool("???"));
    }

    [Fact]
    public void DirectX_settings_strings_are_parsed()
    {
        var settings = DirectXSettingsParser.Parse("SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;");

        Assert.Equal(true, DirectXSettingsParser.Flag(settings, "SwapEffectUpgradeEnable"));
        Assert.Equal(false, DirectXSettingsParser.Flag(settings, "VRROptimizeEnable"));
        Assert.Null(DirectXSettingsParser.Flag(settings, "AutoHDREnable"));
        Assert.Equal("2", DirectXSettingsParser.Parse("GpuPreference=2;")["GpuPreference"]);
    }

    [Fact]
    public void Device_classes_and_power_plans_are_mapped()
    {
        Assert.Equal(DeviceCategory.Gpu, DeviceClassParser.Category(new Guid("4d36e968-e325-11ce-bfc1-08002be10318")));
        Assert.Equal(DeviceCategory.Network, DeviceClassParser.Category(new Guid("4d36e972-e325-11ce-bfc1-08002be10318")));
        Assert.Equal(DeviceCategory.Other, DeviceClassParser.Category(null));

        Assert.Equal(PowerPlanKind.HighPerformance, PowerPlanParser.Kind(PowerGuids.HighPerformance, null));
        Assert.Equal(PowerPlanKind.UltimatePerformance, PowerPlanParser.Kind(Guid.NewGuid(), "Ultimate Performance"));
        Assert.Equal(PowerPlanKind.Custom, PowerPlanParser.Kind(Guid.NewGuid(), "Piano del produttore"));
        Assert.Equal("Prestazioni massime", PowerPlanParser.OverlayName("ded574b5-45a0-4f42-8737-46345c09c238"));
        Assert.Equal("Bilanciata", PowerPlanParser.OverlayName("00000000-0000-0000-0000-000000000000"));
    }

    [Fact]
    public void Miscellaneous_values_are_decoded()
    {
        Assert.Equal(15.625, MiscParsers.TimerMs(156250));
        Assert.Equal(0.5, MiscParsers.TimerMs(5000));
        Assert.True(MiscParsers.DisablesFullscreenOptimizations("~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE"));
        Assert.Equal(0b1100UL, MiscParsers.AffinityMask([0b1100, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Equal(false, MiscParsers.MouseAcceleration("0"));
        Assert.Equal(true, MiscParsers.MouseAcceleration("1"));
    }

    /// <summary>Le strutture D3DKMT devono coincidere al byte con quelle di Windows (x64).</summary>
    [Fact]
    public void D3dkmt_structures_match_windows_sizes()
    {
        Assert.Equal(20, Marshal.SizeOf<D3dkmtAdapterInfo>());
        Assert.Equal(16, Marshal.SizeOf<D3dkmtEnumAdapters2>());
        Assert.Equal(24, Marshal.SizeOf<D3dkmtQueryAdapterInfo>());
        Assert.Equal(4, Marshal.SizeOf<D3dkmtCloseAdapter>());
    }
}

public sealed class CatalogTests
{
    private static readonly KnownSoftwareCatalog Catalog = KnownSoftwareCatalog.Load();

    [Fact]
    public void Known_software_catalog_loads_and_is_consistent()
    {
        Assert.True(Catalog.Entries.Count > 50);
        Assert.All(Catalog.Entries.Where(e => e.Category == SoftwareCategory.AntiCheat), e => Assert.True(e.NeverTouch, e.Id));
        Assert.All(Catalog.Entries.Where(e => e.Risk == SoftwareRisk.Vulnerable), e => Assert.NotEmpty(e.Drivers));
    }

    [Fact]
    public void Software_is_matched_by_names_only()
    {
        Assert.Equal("discord", Catalog.MatchProcess("Discord")?.Id);
        Assert.Equal("discord", Catalog.MatchProcess("discord.exe")?.Id);
        Assert.Equal("easyanticheat", Catalog.MatchService("EasyAntiCheat_EOS")?.Id);
        Assert.Equal("winring0", Catalog.MatchDriver("WinRing0_1_2_0", "WinRing0x64.sys")?.Id);
        Assert.Equal("vanguard", Catalog.MatchDriver("vgk", "vgk.sys")?.Id);
        Assert.Equal("google-update", Catalog.MatchTask(@"\GoogleUpdateTaskMachineCore{9A1B}", null)?.Id);
        Assert.Null(Catalog.MatchProcess("notepad"));
    }

    [Fact]
    public void Startup_match_prefers_the_most_specific_pattern()
    {
        Assert.Equal("razer-cortex", Catalog.MatchStartup("Razer Cortex", null)?.Id);
        Assert.Equal("razer-synapse", Catalog.MatchStartup("Razer Synapse", null)?.Id);
    }

    [Fact]
    public void Invalid_catalog_override_is_rejected()
    {
        var dir = Directory.CreateTempSubdirectory("pco-catalog-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, KnownSoftwareCatalog.FileName),
                """{ "entries": [ { "id": "a", "name": "A", "processes": ["a.exe"] }, { "id": "a", "name": "B", "processes": ["b.exe"] } ] }""");

            Assert.Throws<InvalidDataException>(() => KnownSoftwareCatalog.Load(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Registry_probes_have_unique_ids_and_default_semantics()
    {
        var tweakIds = RegistryProbes.KnownTweaks.Select(t => t.Probe.Id).ToList();
        Assert.Equal(tweakIds.Count, tweakIds.Distinct().Count());
        Assert.All(RegistryProbes.All.GroupBy(p => p.Id), g => Assert.Single(g.Select(p => p.Target).Distinct()));

        var throttling = RegistryProbes.KnownTweaks.Single(t => t.Probe.Id == "mmcss.networkthrottling");
        Assert.True(throttling.IsDefault(SettingValue.Of(10u)));
        Assert.False(throttling.IsDefault(SettingValue.Of(uint.MaxValue)));
        Assert.False(throttling.IsDefault(SettingValue.Absent));

        var irq8 = RegistryProbes.KnownTweaks.Single(t => t.Probe.Id == "placebo.irq8priority");
        Assert.True(irq8.IsDefault(SettingValue.Absent));
        Assert.False(irq8.IsDefault(SettingValue.Of(1u)));
    }
}
