using PcOptimizer.Core.Model;
using PcOptimizer.Hardware.Assessment;
using PcOptimizer.Hardware.Database;
using PcOptimizer.Hardware.Model;
using static PcOptimizer.Hardware.Tests.TestProfiles;

namespace PcOptimizer.Hardware.Tests;

public sealed class AnalyzerTests
{
    private static readonly HardwareAnalyzer Analyzer = new(HardwareDatabase.Load());

    private static HardwareAssessment Assess(HardwareProfile profile) => Analyzer.Assess(profile);

    private static IEnumerable<string> Ids(HardwareAssessment a) => a.Findings.Select(f => f.Id);

    // --- Fasce dei PC di riferimento ---

    [Fact]
    public void Old_pc_is_low_tier()
    {
        var a = Assess(OldPc());

        Assert.Equal(HardwareTier.Low, a.Tier.Tier);
        Assert.Equal("intel-skylake", a.Cpu.ArchitectureId);
        Assert.Equal("GeForce GT 730", a.PrimaryGpu?.ModelName);
        Assert.Equal(StorageKind.Hdd, a.Storage.Kind);
    }

    [Fact]
    public void Mid_pc_is_mid_tier()
    {
        var a = Assess(MidPc());

        Assert.Equal(HardwareTier.Mid, a.Tier.Tier);
        Assert.Equal("amd-zen2", a.Cpu.ArchitectureId);
        Assert.False(a.Cpu.BoostFromCpuid); // AMD non espone il clock massimo via CPUID: valore tipico
        Assert.Empty(a.Tier.AppliedCaps);
    }

    [Fact]
    public void High_end_pc_is_high_tier_with_all_components_identified()
    {
        var a = Assess(HighPc());

        Assert.Equal(HardwareTier.High, a.Tier.Tier);
        Assert.Equal(Confidence.High, a.Tier.Confidence);
        Assert.Equal("intel-raptorlake-s", a.Cpu.ArchitectureId);
        Assert.Equal((8, 16, 32), (a.Cpu.PerformanceCores, a.Cpu.EfficiencyCores, a.Cpu.Threads));
        Assert.Equal("GeForce RTX 4080 SUPER", a.PrimaryGpu?.ModelName);
        Assert.Equal(StorageKind.Nvme, a.Storage.Kind);
        Assert.Equal(("Z790", OcSupport.Yes), (a.Board.Chipset, a.Board.CpuOverclock));
    }

    public static TheoryData<string, HardwareTier> CalibrationSystems => new()
    {
        { "i5-12400F + RTX 3060", HardwareTier.Mid },
        { "Ryzen 5 5600 + RX 6700 XT", HardwareTier.Mid },
        { "i5-13600K + RTX 4070", HardwareTier.High },
    };

    /// <summary>Casi intermedi che fissano il significato delle soglie (vedi tier-scoring.json).</summary>
    [Theory]
    [MemberData(nameof(CalibrationSystems))]
    public void Intermediate_systems_fall_in_expected_tier(string system, HardwareTier expected)
    {
        var nvme = SystemDisk("NVMe SSD 1TB", DiskMediaType.Ssd, DiskBusType.Nvme);
        var profile = system switch
        {
            "i5-12400F + RTX 3060" => MidPc() with
            {
                Cpu = new CpuInfo { Vendor = CpuVendor.Intel, BrandString = "12th Gen Intel(R) Core(TM) i5-12400F", Family = 6, Model = 0x97, Stepping = 5,
                                    PhysicalCores = 6, LogicalProcessors = 12, PerformanceCores = 6, HasSmt = true, MaxClockMHz = 4400 },
                Gpus = [Gpu("NVIDIA GeForce RTX 3060", GpuVendor.Nvidia, 12)],
                Disks = [nvme],
            },
            "Ryzen 5 5600 + RX 6700 XT" => MidPc() with
            {
                Cpu = new CpuInfo { Vendor = CpuVendor.Amd, BrandString = "AMD Ryzen 5 5600 6-Core Processor", Family = 0x19, Model = 0x21, Stepping = 2,
                                    PhysicalCores = 6, LogicalProcessors = 12, PerformanceCores = 6, HasSmt = true },
                Gpus = [Gpu("AMD Radeon RX 6700 XT", GpuVendor.Amd, 12)],
                Disks = [nvme],
            },
            _ => HighPc() with
            {
                Cpu = new CpuInfo { Vendor = CpuVendor.Intel, BrandString = "13th Gen Intel(R) Core(TM) i5-13600K", Family = 6, Model = 0xB7, Stepping = 1,
                                    PhysicalCores = 14, LogicalProcessors = 20, PerformanceCores = 6, EfficiencyCores = 8, HasSmt = true,
                                    MaxClockMHz = 5100, MicrocodeRevision = 0x12B },
                Gpus = [Gpu("NVIDIA GeForce RTX 4070", GpuVendor.Nvidia, 12)],
            },
        };

        Assert.Equal(expected, Assess(profile).Tier.Tier);
    }

    private static GpuInfo Gpu(string name, GpuVendor vendor, int vramGb) => new()
    {
        Name = name, Vendor = vendor, IsPhysicalPciDevice = true, PciVendorId = vendor == GpuVendor.Amd ? "1002" : "10DE",
        PciDeviceId = "0001", DedicatedMemoryBytes = vramGb * GiB,
    };

    [Fact]
    public void Scores_increase_from_old_to_high_end()
    {
        var (low, mid, high) = (Assess(OldPc()).Tier.Score, Assess(MidPc()).Tier.Score, Assess(HighPc()).Tier.Score);

        Assert.True(low < mid && mid < high, $"{low} < {mid} < {high}");
    }

    [Fact]
    public void X3d_cpu_gets_gaming_bonus()
    {
        var x3d = HighPc() with
        {
            Cpu = new CpuInfo
            {
                Vendor = CpuVendor.Amd, BrandString = "AMD Ryzen 7 7800X3D 8-Core Processor",
                Family = 0x19, Model = 0x61, Stepping = 2,
                PhysicalCores = 8, LogicalProcessors = 16, PerformanceCores = 8, HasSmt = true,
            },
        };

        Assert.Equal(1.15, Assess(x3d).Cpu.GamingFactor);
    }

    // --- Limiti di fascia ---

    [Fact]
    public void Eight_gb_of_ram_caps_high_end_to_mid()
    {
        var profile = HighPc() with
        {
            Memory = new MemoryInfo
            {
                Modules = [Module("CMK8GX5M2B6000C30", 4, MemoryType.Ddr5, 6000, "DIMM_A2", "BANK 0"),
                           Module("CMK8GX5M2B6000C30", 4, MemoryType.Ddr5, 6000, "DIMM_B2", "BANK 0")],
            },
        };

        var tier = Assess(profile).Tier;

        Assert.Equal(HardwareTier.Mid, tier.Tier);
        Assert.Contains(tier.AppliedCaps, c => c.Contains("RAM"));
    }

    [Fact]
    public void Windows_on_hdd_caps_high_end_to_mid()
    {
        var profile = HighPc() with { Disks = [SystemDisk("WDC WD20EZAZ", DiskMediaType.Hdd, DiskBusType.Sata)] };

        var a = Assess(profile);

        Assert.Equal(HardwareTier.Mid, a.Tier.Tier);
        Assert.Contains("disk-system-hdd", Ids(a));
    }

    [Fact]
    public void Missing_gpu_driver_caps_to_low_and_is_critical()
    {
        var profile = HighPc() with
        {
            Gpus = [new GpuInfo
            {
                Name = "Microsoft Basic Display Adapter", Vendor = GpuVendor.Nvidia, PciVendorId = "10DE", PciDeviceId = "2702",
                IsPhysicalPciDevice = true, UsesBasicDisplayDriver = true,
            }],
        };

        var a = Assess(profile);

        Assert.Null(a.PrimaryGpu);
        Assert.Equal(HardwareTier.Low, a.Tier.Tier);
        Assert.Equal("gpu-driver-missing", a.Findings[0].Id);
        Assert.Equal(FindingSeverity.Critical, a.Findings[0].Severity);
    }

    // --- RAM: XMP e canali ---

    [Fact]
    public void Xmp_state_is_estimated()
    {
        Assert.Equal(XmpState.NotAvailable, Assess(OldPc()).Memory.Xmp);   // moduli JEDEC Kingston ValueRAM
        Assert.Equal(XmpState.LikelyEnabled, Assess(MidPc()).Memory.Xmp);
        Assert.Equal(XmpState.LikelyEnabled, Assess(HighPc()).Memory.Xmp);
    }

    [Fact]
    public void Xmp_kit_running_at_jedec_speed_is_reported_as_disabled()
    {
        var profile = MidPc() with
        {
            Memory = new MemoryInfo
            {
                Modules = [Module("F4-3200C16-8GIS", 8, MemoryType.Ddr4, 2133, "DIMM 0", "P0 CHANNEL A"),
                           Module("F4-3200C16-8GIS", 8, MemoryType.Ddr4, 2133, "DIMM 1", "P0 CHANNEL B")],
            },
        };

        var a = Assess(profile);

        Assert.Equal(XmpState.LikelyDisabled, a.Memory.Xmp);
        Assert.Equal((3200, 16), (a.Memory.RatedMTs, a.Memory.RatedCl));
        Assert.Contains("ram-xmp-disabled", Ids(a));
    }

    [Fact]
    public void Xmp_kit_on_locked_chipset_is_a_platform_limit_not_a_problem()
    {
        var profile = OldPc() with
        {
            Memory = new MemoryInfo
            {
                Modules = [Module("CMK16GX4M2B3000C15", 8, MemoryType.Ddr4, 2133, "ChannelA-DIMM0", "BANK 0"),
                           Module("CMK16GX4M2B3000C15", 8, MemoryType.Ddr4, 2133, "ChannelB-DIMM0", "BANK 2")],
            },
        };

        var a = Assess(profile);

        Assert.Equal(XmpState.NotSupportedByPlatform, a.Memory.Xmp);
        Assert.Contains("H110", a.Memory.XmpExplanation);
        Assert.DoesNotContain("ram-xmp-disabled", Ids(a));
        Assert.Equal(FindingSeverity.Info, a.Findings.Single(f => f.Id == "ram-xmp-platform").Severity);
    }

    [Fact]
    public void Unknown_part_number_above_jedec_speed_means_xmp_enabled()
    {
        var profile = MidPc() with
        {
            Memory = new MemoryInfo
            {
                Modules = [Module("99U5584-005.A00LF", 8, MemoryType.Ddr4, 3600, "DIMM_A2", null!),
                           Module("99U5584-005.A00LF", 8, MemoryType.Ddr4, 3600, "DIMM_B2", null!)],
            },
        };

        Assert.Equal(XmpState.LikelyEnabled, Assess(profile).Memory.Xmp);
    }

    [Fact]
    public void Single_module_is_single_channel()
    {
        var profile = MidPc() with
        {
            Memory = new MemoryInfo { Modules = [Module("F4-3200C16-16GIS", 16, MemoryType.Ddr4, 3200, "DIMM 1", "P0 CHANNEL A")] },
        };

        var a = Assess(profile);

        Assert.Equal((1, false), (a.Memory.Channels, a.Memory.ChannelsAssumed));
        Assert.Contains("ram-single-channel", Ids(a));
    }

    [Fact]
    public void Two_modules_in_the_same_channel_are_flagged()
    {
        var profile = HighPc() with
        {
            Memory = new MemoryInfo
            {
                Modules = [Module("CMK32GX5M2B6000C30", 16, MemoryType.Ddr5, 6000, "DIMM_A1", "BANK 0"),
                           Module("CMK32GX5M2B6000C30", 16, MemoryType.Ddr5, 6000, "DIMM_A2", "BANK 1")],
            },
        };

        Assert.Contains("ram-wrong-slots", Ids(Assess(profile)));
    }

    [Fact]
    public void Unreadable_slot_names_assume_dual_channel_without_warnings()
    {
        var profile = MidPc() with
        {
            Memory = new MemoryInfo
            {
                Modules = [Module("F4-3200C16-8GIS", 8, MemoryType.Ddr4, 3200, "DIMM1", ""),
                           Module("F4-3200C16-8GIS", 8, MemoryType.Ddr4, 3200, "DIMM2", "")],
            },
        };

        var a = Assess(profile);

        Assert.Equal((2, true), (a.Memory.Channels, a.Memory.ChannelsAssumed));
        Assert.DoesNotContain(Ids(a), id => id.StartsWith("ram-", StringComparison.Ordinal));
    }

    // --- CPU: avvertenze note ---

    [Fact]
    public void Raptor_lake_with_old_microcode_is_critical()
    {
        var profile = HighPc() with { Cpu = HighPc().Cpu with { MicrocodeRevision = 0x129 } };

        var finding = Assess(profile).Findings.Single(f => f.Id == "intel-raptorlake-vmin-shift");

        Assert.Equal(FindingSeverity.Critical, finding.Severity);
        Assert.Contains("0x129", finding.Detail);
    }

    [Fact]
    public void Raptor_lake_with_updated_microcode_has_no_advisory() =>
        Assert.DoesNotContain(Ids(Assess(HighPc())), id => id.StartsWith("intel-raptorlake", StringComparison.Ordinal));

    [Fact]
    public void Unreadable_microcode_on_raptor_lake_is_a_warning()
    {
        var profile = HighPc() with { Cpu = HighPc().Cpu with { MicrocodeRevision = null } };

        Assert.Equal(FindingSeverity.Warning,
            Assess(profile).Findings.Single(f => f.Id == "intel-raptorlake-vmin-shift-unknown").Severity);
    }

    [Fact]
    public void Early_am5_bios_is_flagged()
    {
        var zen4 = MidPc() with
        {
            Cpu = new CpuInfo { Vendor = CpuVendor.Amd, BrandString = "AMD Ryzen 7 7800X3D 8-Core Processor", Family = 0x19, Model = 0x61, Stepping = 2,
                                PhysicalCores = 8, LogicalProcessors = 16, PerformanceCores = 8, HasSmt = true },
            Board = MidPc().Board with { BoardProduct = "B650 AORUS ELITE AX", BiosReleaseDate = new DateTime(2023, 2, 10) },
        };

        Assert.Contains("amd-am5-early-bios-soc-voltage", Ids(Assess(zen4)));
        Assert.DoesNotContain("amd-am5-early-bios-soc-voltage",
            Ids(Assess(zen4 with { Board = zen4.Board with { BiosReleaseDate = new DateTime(2024, 1, 1) } })));
    }

    [Fact]
    public void Hybrid_cpu_on_windows_10_is_flagged() =>
        Assert.Contains("cpu-hybrid-windows10", Ids(Assess(HighPc() with { Os = WindowsOs(19045, "Windows 10 Pro") })));

    [Fact]
    public void Unknown_cpu_uses_prudent_values_and_low_confidence()
    {
        var profile = HighPc() with { Cpu = HighPc().Cpu with { Model = 0xFE } };

        var a = Assess(profile);

        Assert.Equal(Confidence.Low, a.Cpu.Confidence);
        Assert.Contains("cpu-unknown", Ids(a));
    }

    // --- Monitor ---

    [Fact]
    public void Monitor_below_max_refresh_is_flagged()
    {
        var profile = MidPc() with { Displays = [Display("AOC 24G2", 1920, 1080, 60, 144, "10DE", "21C4")] };

        var finding = Assess(profile).Findings.Single(f => f.Id == "display-refresh-1");

        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Contains("144 Hz", finding.Title);
    }

    [Fact]
    public void Fractional_refresh_rate_is_not_flagged() =>
        Assert.DoesNotContain("display-refresh-1",
            Ids(Assess(MidPc() with { Displays = [Display("TV", 1920, 1080, 59.94, 60, "10DE", "21C4")] })));

    [Fact]
    public void Monitor_plugged_into_integrated_graphics_is_critical_on_desktop()
    {
        var profile = HighPc() with
        {
            Gpus =
            [
                HighPc().Gpus[0],
                new GpuInfo { Name = "Intel(R) UHD Graphics 770", Vendor = GpuVendor.Intel, PciVendorId = "8086", PciDeviceId = "A780",
                              IsPhysicalPciDevice = true, DedicatedMemoryBytes = 128L * 1024 * 1024 },
            ],
            Displays = [Display("ROG XG27AQM", 2560, 1440, 240, 240, "8086", "A780")],
        };

        var a = Assess(profile);

        Assert.Equal("GeForce RTX 4080 SUPER", a.PrimaryGpu?.ModelName);
        Assert.Equal("display-on-igpu", a.Findings[0].Id);

        var laptop = profile with { Board = profile.Board with { IsLaptop = true } };
        Assert.DoesNotContain("display-on-igpu", Ids(Assess(laptop)));
    }

    // --- Rete, alimentazione, disco ---

    [Fact]
    public void Wifi_and_slow_ethernet_are_flagged()
    {
        var wifi = MidPc() with { NetworkAdapters = [Ethernet() with { Medium = NetworkMedium.WiFi, Description = "Intel(R) Wi-Fi 6 AX201" }] };
        var slow = MidPc() with { NetworkAdapters = [Ethernet(100_000_000)] };

        Assert.Contains("net-wifi", Ids(Assess(wifi)));
        Assert.Contains("net-link-speed", Ids(Assess(slow)));
        Assert.DoesNotContain(Ids(Assess(MidPc())), id => id.StartsWith("net-", StringComparison.Ordinal));
    }

    [Fact]
    public void Laptop_on_battery_is_flagged() =>
        Assert.Contains("power-on-battery",
            Ids(Assess(MidPc() with { Board = MidPc().Board with { IsLaptop = true, OnBattery = true } })));

    [Fact]
    public void Low_disk_space_and_bad_health_are_flagged()
    {
        var profile = MidPc() with
        {
            Os = MidPc().Os with { SystemDriveFreeBytes = 8 * GiB },
            Disks = [SystemDisk("Samsung SSD 860 EVO 500GB", DiskMediaType.Ssd, DiskBusType.Sata) with { Health = DiskHealth.Warning }],
        };

        var a = Assess(profile);

        Assert.Contains("disk-low-space", Ids(a));
        Assert.Equal(FindingSeverity.Critical, a.Findings.Single(f => f.Id == "disk-health-0").Severity);
    }

    [Fact]
    public void Findings_are_sorted_by_severity()
    {
        var profile = OldPc() with
        {
            Displays = [Display("HP 22w", 1920, 1080, 60, 75, "10DE", "1287")],
            Disks = [SystemDisk("ST1000DM010-2EP102", DiskMediaType.Hdd, DiskBusType.Sata) with { Health = DiskHealth.Unhealthy }],
        };

        var severities = Assess(profile).Findings.Select(f => (int)f.Severity).ToList();

        Assert.Equal(severities.OrderByDescending(s => s), severities);
    }

    [Fact]
    public void Tweak_context_carries_tier_and_facets()
    {
        var profile = HighPc();
        var assessment = Assess(profile);

        var context = HardwareContext.CreateTweakContext(profile, assessment);

        Assert.Equal(HardwareTier.High, context.Tier);
        Assert.True(context.IsWindows11);
        Assert.Same(assessment, context.Get<HardwareAssessment>());
        Assert.Same(profile, context.Get<HardwareProfile>());
    }
}
