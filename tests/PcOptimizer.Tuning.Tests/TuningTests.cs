using PcOptimizer.Benchmark.Model;
using PcOptimizer.Core;
using PcOptimizer.Hardware.Assessment;
using PcOptimizer.Hardware.Database;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Tuning.Bios;
using PcOptimizer.Tuning.Guides;
using PcOptimizer.Tuning.Safety;
using PcOptimizer.Tuning.Stability;

namespace PcOptimizer.Tuning.Tests;

internal static class Systems
{
    private static readonly HardwareAnalyzer Analyzer = new(HardwareDatabase.Load());

    public static (HardwareProfile, HardwareAssessment) Pc(
        CpuVendor vendor, int family, int model, string brand, uint? microcode, string boardMaker = "ASUSTeK COMPUTER INC.",
        bool laptop = false, MemoryType memory = MemoryType.Ddr5, int configured = 4800, string partNumber = "CMK32GX5M2B6000C36")
    {
        var profile = new HardwareProfile
        {
            Cpu = new CpuInfo
            {
                Vendor = vendor, VendorId = vendor == CpuVendor.Intel ? "GenuineIntel" : "AuthenticAMD", Family = family, Model = model,
                BrandString = brand, PhysicalCores = 8, LogicalProcessors = 16, PerformanceCores = 8, HasSmt = true, MicrocodeRevision = microcode,
            },
            Gpus = [new GpuInfo { Name = "NVIDIA GeForce RTX 4070", Vendor = GpuVendor.Nvidia, PciVendorId = "10DE", PciDeviceId = "2786", IsPhysicalPciDevice = true, DedicatedMemoryBytes = 12L << 30 }],
            Memory = new MemoryInfo
            {
                VisibleBytes = 32L << 30,
                Modules =
                [
                    new MemoryModule { Manufacturer = "Corsair", PartNumber = partNumber, CapacityBytes = 16L << 30, SpeedMTs = configured, ConfiguredSpeedMTs = configured, Type = memory, DeviceLocator = "DIMM_A2" },
                    new MemoryModule { Manufacturer = "Corsair", PartNumber = partNumber, CapacityBytes = 16L << 30, SpeedMTs = configured, ConfiguredSpeedMTs = configured, Type = memory, DeviceLocator = "DIMM_B2" },
                ],
            },
            Disks = [new DiskInfo { FriendlyName = "NVMe", MediaType = DiskMediaType.Ssd, BusType = DiskBusType.Nvme, IsSystemDisk = true, SizeBytes = 1L << 40 }],
            Board = new BoardInfo
            {
                BoardManufacturer = boardMaker, BoardProduct = "ROG STRIX Z790-F", SystemManufacturer = laptop ? "Lenovo" : boardMaker,
                BiosVendor = "American Megatrends", BiosVersion = "1662", BiosReleaseDate = new DateTime(2024, 1, 10), IsLaptop = laptop,
                Firmware = FirmwareType.Uefi,
            },
            Os = new OsInfo { Build = 22631 },
        };
        return (profile, Analyzer.Assess(profile));
    }

    public static (HardwareProfile, HardwareAssessment) RaptorLake(uint? microcode) =>
        Pc(CpuVendor.Intel, 6, 0xB7, "13th Gen Intel(R) Core(TM) i9-13900K", microcode);

    public static (HardwareProfile, HardwareAssessment) Ryzen7000(bool laptop = false) =>
        Pc(CpuVendor.Amd, 0x19, 0x61, "AMD Ryzen 7 7800X3D 8-Core Processor", 0x0A601206, "Micro-Star International Co., Ltd.", laptop);
}

public class BiosTests
{
    private static readonly BiosGuide Guide = BiosGuide.Load();

    [Fact]
    public void RaptorLakeWithOldMicrocodeRequiresBiosUpdateFirst()
    {
        var (profile, assessment) = Systems.RaptorLake(0x11F);
        Assert.Equal(BiosGuide.RaptorLakeArchitecture, assessment.Cpu.ArchitectureId);
        Assert.True(BiosGuide.RaptorLakeNeedsUpdate(profile, assessment));

        var items = Guide.Build(profile, assessment);
        var update = items.Single(i => i.Id == "bios.update");
        Assert.Equal(ChecklistState.Action, update.State);
        Assert.Contains("0x11F", update.Why);
        Assert.Contains("EZ Flash", update.MenuPath);
        Assert.Equal(ChecklistState.Action, items.Single(i => i.Id == "bios.intel-defaults").State);

        var methods = TuningMethods.For(profile, assessment, Guide);
        Assert.Contains("microcode", methods.Single(m => m.Id == "cpu.intel-offset").Blocked);
        Assert.Null(methods.Single(m => m.Id == "gpu.undervolt").Blocked);
    }

    [Fact]
    public void RaptorLakeWithFixedMicrocodeAllowsUndervolt()
    {
        var (profile, assessment) = Systems.RaptorLake(0x12F);
        Assert.False(BiosGuide.RaptorLakeNeedsUpdate(profile, assessment));
        Assert.Null(TuningMethods.For(profile, assessment, Guide).Single(m => m.Id == "cpu.intel-offset").Blocked);
        Assert.Equal(ChecklistState.Check, Guide.Build(profile, assessment).Single(i => i.Id == "bios.update").State);
        Assert.False(BiosGuide.RaptorLakeNeedsUpdate(profile with { Cpu = profile.Cpu with { MicrocodeRevision = 0x12B } }, assessment));
        Assert.True(BiosGuide.RaptorLakeNeedsUpdate(profile with { Cpu = profile.Cpu with { MicrocodeRevision = null } }, assessment));
    }

    [Fact]
    public void AmdMsiBoardUsesVendorMenusAndExpo()
    {
        var (profile, assessment) = Systems.Ryzen7000();
        var items = Guide.Build(profile, assessment);

        Assert.Contains("M-Flash", items.Single(i => i.Id == "bios.update").MenuPath);
        Assert.Contains("fTPM", items.Single(i => i.Id == "bios.tpm-secureboot").MenuPath);
        var methods = TuningMethods.For(profile, assessment, Guide);
        var co = methods.Single(m => m.Id == "cpu.amd-curve-optimizer");
        Assert.Null(co.Blocked);
        Assert.Contains("Curve Optimizer", co.Where);
        Assert.DoesNotContain(methods, m => m.Id == "cpu.intel-offset");
        if (assessment.Memory.Xmp == XmpState.LikelyDisabled)
        {
            var memory = items.Single(i => i.Id == "bios.memory-profile");
            Assert.Contains("EXPO", memory.MenuPath);
        }
    }

    [Fact]
    public void LaptopsGetOemChecklistAndNoCpuTuning()
    {
        var (profile, assessment) = Systems.Ryzen7000(laptop: true);
        var methods = TuningMethods.For(profile, assessment, Guide);
        Assert.NotNull(methods.Single(m => m.Id == "cpu.amd-curve-optimizer").Blocked);
        Assert.NotNull(methods.Single(m => m.Id == "gpu.core-overclock").Blocked);
        Assert.NotNull(methods.Single(m => m.Id == "memory.profile").Blocked);
    }

    [Fact]
    public void EveryVendorHasTheEssentialMenus()
    {
        foreach (var vendor in new[] { BoardVendor.Asus, BoardVendor.Msi, BoardVendor.Gigabyte, BoardVendor.AsRock })
        {
            var menus = Guide.Menus(vendor)!;
            foreach (var key in new[] { "biosUpdate", "xmp", "expo", "rebar", "intelUndervolt", "curveOptimizer", "secureBoot" })
            {
                Assert.False(string.IsNullOrWhiteSpace(menus.Settings.GetValueOrDefault(key)), $"{vendor}: {key}");
            }
        }
    }
}

public class PlannerTests
{
    private static readonly TuningMethod Offset = new()
    {
        Id = "cpu.intel-offset", Title = "t", Component = TuningComponent.Cpu, Where = "w", Steps = ["s"],
        Limits = new StepLimits(-30, -10, -60, "mV", PcOptimizer.Core.Model.Risk.Medium), Tests = [StabilityTestKind.Cpu],
    };

    private static TuningStep Step(double value, StepOutcome outcome) => new() { Value = value, Outcome = outcome, StartedAt = DateTimeOffset.Now };

    [Fact]
    public void WalksUntilInstabilityAndBacksOffOneStep()
    {
        var profile = new TuningProfile { MethodId = Offset.Id };
        Assert.Equal(-30, TuningPlanner.NextValue(profile, Offset));

        profile = TuningPlanner.Record(profile, Step(-30, StepOutcome.Stable));
        Assert.Equal(-40, TuningPlanner.NextValue(profile, Offset));
        profile = TuningPlanner.Record(profile, Step(-40, StepOutcome.Stable));
        profile = TuningPlanner.Record(profile, Step(-50, StepOutcome.Unstable));

        Assert.Null(TuningPlanner.NextValue(profile, Offset));
        Assert.Equal(-40, profile.LastStable);
        Assert.Equal(-30, TuningPlanner.Recommended(profile, Offset));
    }

    [Fact]
    public void StopsAtTheSafetyLimit()
    {
        var profile = new TuningProfile { MethodId = Offset.Id };
        foreach (var v in new[] { -30.0, -40, -50, -60 })
        {
            profile = TuningPlanner.Record(profile, Step(v, StepOutcome.Stable));
        }

        Assert.Null(TuningPlanner.NextValue(profile, Offset)); // -70 sarebbe oltre il limite di -60
        Assert.Equal(-60, TuningPlanner.Recommended(profile, Offset)); // limite raggiunto senza instabilità
    }

    [Fact]
    public void PendingStepsBecomeCrashesAndAbortsStop()
    {
        var pending = new TuningProfile { MethodId = Offset.Id, Steps = [new TuningStep { Value = -30, Outcome = StepOutcome.Pending, StartedAt = DateTimeOffset.Now.AddHours(-1) }] };
        var crashed = TuningPlanner.MarkInterruptedAsCrashed(pending);
        Assert.Equal(StepOutcome.Crashed, crashed.Steps.Single().Outcome);
        Assert.Null(TuningPlanner.Recommended(crashed, Offset));

        var fresh = pending with { Steps = [pending.Steps[0] with { StartedAt = DateTimeOffset.Now }] };
        Assert.Equal(StepOutcome.Pending, TuningPlanner.MarkInterruptedAsCrashed(fresh).Steps.Single().Outcome);
        Assert.Equal(-30, TuningPlanner.NextValue(fresh, Offset));

        var aborted = TuningPlanner.Record(new TuningProfile { MethodId = Offset.Id }, Step(-30, StepOutcome.Aborted));
        Assert.Null(TuningPlanner.NextValue(aborted, Offset));
    }

    [Fact]
    public void BackingOffNeverCrossesFactorySettings()
    {
        var gpuOc = Offset with { Limits = new StepLimits(15, 15, 200, "MHz", PcOptimizer.Core.Model.Risk.Medium) };
        var profile = TuningPlanner.Record(TuningPlanner.Record(new TuningProfile { MethodId = "x" }, Step(15, StepOutcome.Stable)), Step(30, StepOutcome.Unstable));
        Assert.Equal(0, TuningPlanner.Recommended(profile, gpuOc));
    }

    [Fact]
    public void StoreKeepsProfilesAcrossRestarts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcopt-tuning-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TuningProfileStore(new AppPaths(root));
            var profile = store.Open("gpu.undervolt");
            store.Save(TuningPlanner.Record(profile, Step(-50, StepOutcome.Stable)));
            var reopened = store.Open("gpu.undervolt");
            Assert.Equal(profile.Id, reopened.Id);
            Assert.Equal(-50, reopened.LastStable);
            store.Save(reopened with { Completed = true });
            Assert.NotEqual(profile.Id, store.Open("gpu.undervolt").Id);
            Assert.Equal(2, store.Load().Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class SafetyAndStressTests
{
    private static SensorSample S(double? cpu = 60, double? gpu = 60, double? hotspot = null) => new() { CpuTempC = cpu, GpuTempC = gpu, GpuHotspotC = hotspot };

    [Fact]
    public void SafetyLimitsStopTheTest()
    {
        Assert.Null(SafetyLimits.Check([]));
        Assert.Null(SafetyLimits.Check([S(), S(94), S(96)]));
        Assert.NotNull(SafetyLimits.Check([S(100)]));
        Assert.NotNull(SafetyLimits.Check(Enumerable.Repeat(S(96), SafetyLimits.SustainedSeconds).ToList()));
        Assert.Null(SafetyLimits.Check(Enumerable.Repeat(S(96), SafetyLimits.SustainedSeconds - 1).ToList()));
        Assert.NotNull(SafetyLimits.Check([S(gpu: 91)]));
        Assert.NotNull(SafetyLimits.Check([S(hotspot: 106)]));
        Assert.Null(SafetyLimits.Check([S(cpu: null, gpu: null)]));
    }

    [Fact]
    public async Task CpuStressFindsNoErrorsOnAHealthyCpu()
    {
        Assert.Equal(CpuStressTest.Workload(5, 1000), CpuStressTest.Workload(5, 1000));
        Assert.NotEqual(CpuStressTest.Workload(5, 1000), CpuStressTest.Workload(6, 1000));

        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var (units, errors, notes) = await new CpuStressTest(threads: 2, rounds: 2000).RunAsync(cancel.Token);
        Assert.True(units > 10);
        Assert.Equal(0, errors);
        Assert.Empty(notes);
    }

    [Fact]
    public async Task MemoryTestPassesAndDetectsCorruption()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var (passes, errors, _) = await new MemoryTest(MemoryTest.BlockBytes, threads: 1).RunAsync(cancel.Token);
        Assert.True(passes >= 1);
        Assert.Equal(0, errors);

        var block = new ulong[1024];
        for (var pattern = 0; pattern < 4; pattern++)
        {
            MemoryTest.Fill(block, 3, pattern, 42);
            Assert.Equal(0, MemoryTest.Verify(block, 3, pattern, 42));
            block[100] ^= 1UL << 7; // bit invertito, come un errore della RAM
            Assert.Equal(1, MemoryTest.Verify(block, 3, pattern, 42));
        }
    }

    [Fact]
    public void GpuMathDetectsWrongResults()
    {
        const uint seed = 7, iterations = 16;
        var hashes = Enumerable.Range(0, 3000).Select(i => GpuStressMath.Hash((uint)i, seed, iterations)).ToArray();
        var floats = Enumerable.Range(0, 3000).Select(i => (uint)i).ToArray();
        Assert.Equal(0, GpuStressMath.Check(hashes, floats, floats, seed, iterations));

        var corrupted = (uint[])hashes.Clone();
        corrupted[997] ^= 0x10;
        Assert.Equal(1, GpuStressMath.Check(corrupted, floats, floats, seed, iterations));

        var otherFloats = (uint[])floats.Clone();
        otherFloats[5]++;
        Assert.Equal(1, GpuStressMath.Check(hashes, otherFloats, floats, seed, iterations));
        Assert.Equal(0, GpuStressMath.Check(hashes, otherFloats, [], seed, iterations));
        Assert.NotEqual(GpuStressMath.Pattern(1, 1), GpuStressMath.Pattern(2, 1));
    }

    [Fact]
    public void ReportOutcomes()
    {
        Assert.Equal(StepOutcome.Stable, new StabilityReport { WorkUnits = 10 }.Outcome);
        Assert.Equal(StepOutcome.Unstable, new StabilityReport { Errors = 1 }.Outcome);
        Assert.Equal(StepOutcome.Unstable, new StabilityReport { WheaErrors = 2 }.Outcome);
        Assert.Equal(StepOutcome.Aborted, new StabilityReport { AbortReason = "caldo" }.Outcome);
        Assert.Equal(StepOutcome.Pending, new StabilityReport { Cancelled = true, WorkUnits = 5 }.Outcome);
        Assert.Equal(StepOutcome.Pending, new StabilityReport { WorkUnits = 0 }.Outcome);
        Assert.False(new StabilityReport { Cancelled = true }.Passed);
    }
}
