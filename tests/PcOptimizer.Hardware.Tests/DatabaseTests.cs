using PcOptimizer.Hardware.Database;
using PcOptimizer.Hardware.Model;

namespace PcOptimizer.Hardware.Tests;

public sealed class DatabaseTests
{
    private static readonly HardwareDatabase Db = HardwareDatabase.Load();

    [Theory]
    [InlineData(CpuVendor.Intel, 0x6, 0xB7, 1, "intel-raptorlake-s")]
    [InlineData(CpuVendor.Intel, 0x6, 0x5E, 3, "intel-skylake")]
    [InlineData(CpuVendor.Intel, 0x6, 0x9E, 9, "intel-kabylake-s")]
    [InlineData(CpuVendor.Intel, 0x6, 0x9E, 12, "intel-coffeelake-s")]
    [InlineData(CpuVendor.Intel, 0x6, 0x9E, 5, "intel-kabylake-coffeelake")]
    [InlineData(CpuVendor.Intel, 0x6, 0xC6, 2, "intel-arrowlake-s")]
    [InlineData(CpuVendor.Amd, 0x17, 0x01, 1, "amd-zen")]
    [InlineData(CpuVendor.Amd, 0x17, 0x08, 2, "amd-zenplus")]
    [InlineData(CpuVendor.Amd, 0x17, 0x71, 0, "amd-zen2")]
    [InlineData(CpuVendor.Amd, 0x19, 0x21, 0, "amd-zen3")]
    [InlineData(CpuVendor.Amd, 0x19, 0x61, 2, "amd-zen4")]
    [InlineData(CpuVendor.Amd, 0x1A, 0x44, 0, "amd-zen5")]
    public void Cpu_architecture_is_identified_with_most_specific_entry(
        CpuVendor vendor, int family, int model, int stepping, string expectedId) =>
        Assert.Equal(expectedId, Db.FindCpuArchitecture(vendor, family, model, stepping)?.Id);

    [Fact]
    public void Unknown_cpu_returns_null() =>
        Assert.Null(Db.FindCpuArchitecture(CpuVendor.Intel, 6, 0xFE, 0));

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070 Ti SUPER", GpuVendor.Nvidia, "GeForce RTX 4070 Ti SUPER")]
    [InlineData("NVIDIA GeForce RTX 4070 Ti", GpuVendor.Nvidia, "GeForce RTX 4070 Ti")]
    [InlineData("NVIDIA GeForce RTX 4070 SUPER", GpuVendor.Nvidia, "GeForce RTX 4070 SUPER")]
    [InlineData("NVIDIA GeForce RTX 4070", GpuVendor.Nvidia, "GeForce RTX 4070")]
    [InlineData("NVIDIA GeForce GTX 1060 3GB", GpuVendor.Nvidia, "GeForce GTX 1060 3GB")]
    [InlineData("NVIDIA GeForce GTX 1060 6GB", GpuVendor.Nvidia, "GeForce GTX 1060 6GB")]
    [InlineData("NVIDIA GeForce GT 730", GpuVendor.Nvidia, "GeForce GT 730")]
    [InlineData("AMD Radeon RX 7900 XT", GpuVendor.Amd, "Radeon RX 7900 XT")]
    [InlineData("AMD Radeon RX 7900 XTX", GpuVendor.Amd, "Radeon RX 7900 XTX")]
    [InlineData("Radeon RX 580 Series", GpuVendor.Amd, "Radeon RX 580")]
    [InlineData("AMD Radeon(TM) Graphics", GpuVendor.Amd, "Radeon Graphics (integrata)")]
    [InlineData("AMD Radeon 780M Graphics", GpuVendor.Amd, "Radeon 780M (integrata)")]
    [InlineData("Intel(R) UHD Graphics 630", GpuVendor.Intel, "Intel UHD Graphics 630 (integrata)")]
    [InlineData("Intel(R) UHD Graphics", GpuVendor.Intel, "Intel UHD Graphics (integrata)")]
    [InlineData("Intel(R) Iris(R) Xe Graphics", GpuVendor.Intel, "Intel Iris Xe (integrata)")]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", GpuVendor.Intel, "Intel Arc A770")]
    public void Gpu_model_is_matched_by_longest_word_sequence(string name, GpuVendor vendor, string expected) =>
        Assert.Equal(expected, Db.FindGpu(vendor, name)?.Entry.Name);

    [Fact]
    public void Laptop_gpu_gets_reduction_modifier()
    {
        var match = Db.FindGpu(GpuVendor.Nvidia, "NVIDIA GeForce RTX 3060 Laptop GPU");

        Assert.Equal("GeForce RTX 3060", match?.Entry.Name);
        Assert.Equal("LAPTOP", match?.Modifier?.Token);
    }

    [Fact]
    public void Gpu_match_respects_vendor() =>
        Assert.Null(Db.FindGpu(GpuVendor.Intel, "NVIDIA GeForce RTX 4090"));

    [Theory]
    [InlineData(16.0, 330)]
    [InlineData(8.0, 180)]
    [InlineData(0.5, 10)]
    public void Unknown_gpu_index_is_estimated_from_vram(double gb, double expected) =>
        Assert.Equal(expected, Db.EstimateGpuIndexFromVram((long)(gb * TestProfiles.GiB)));

    [Theory]
    [InlineData("CMK16GX4M2B3200C16", 3200, 16, false)]
    [InlineData("CMK32GX5M2B6000C30    ", 6000, 30, false)]
    [InlineData("F4-3600C16D-32GTZNC", 3600, 16, false)]
    [InlineData("F5-6000J3038F16GX2-TZ5RK", 6000, 30, false)]
    [InlineData("F3-1600C9D-8GXM", 1600, 9, false)]
    [InlineData("KF436C17BBK2/16", 3600, 17, false)]
    [InlineData("KF560C36BBEK2-32", 6000, 36, false)]
    [InlineData("HX432C16FB3/8", 3200, 16, false)]
    [InlineData("KVR21N15S8/4", 2133, null, true)]
    [InlineData("BL8G32C16U4B.M8FE1", 3200, 16, false)]
    [InlineData("CT8G4DFS8266.C8FD1", 2666, null, true)]
    [InlineData("CT16G4SFRA32A.C16FP", 3200, null, true)]
    [InlineData("CT16G48C40U5.M8A1", 4800, 40, true)]
    [InlineData("TLZGD416G3200HC16CBK", 3200, 16, false)]
    [InlineData("PVS416G320C6K", 3200, null, false)]
    [InlineData("M378A1K43CB2-CTD", 2666, null, true)]
    [InlineData("HMA81GU6CJR8N-VK", 2666, null, true)]
    [InlineData("MTA8ATF1G64AZ-2G6E1", 2666, null, true)]
    public void Memory_part_numbers_are_decoded(string partNumber, int speed, int? cl, bool jedec)
    {
        var info = Db.ParseMemoryPartNumber(partNumber);

        Assert.NotNull(info);
        Assert.Equal((speed, cl, jedec), (info.RatedSpeedMTs, info.CasLatency, info.IsJedec));
    }

    [Fact]
    public void Unknown_part_number_returns_null() =>
        Assert.Null(Db.ParseMemoryPartNumber("99U5584-005.A00LF"));

    [Theory]
    [InlineData(CpuVendor.Intel, "Z790", OcSupport.Yes, OcSupport.Yes)]
    [InlineData(CpuVendor.Intel, "B760", OcSupport.No, OcSupport.Yes)]
    [InlineData(CpuVendor.Intel, "H110", OcSupport.No, OcSupport.No)]
    [InlineData(CpuVendor.Amd, "B450", OcSupport.Yes, OcSupport.Yes)]
    [InlineData(CpuVendor.Amd, "A520", OcSupport.No, OcSupport.Yes)]
    [InlineData(CpuVendor.Amd, "A320", OcSupport.No, OcSupport.Limited)]
    public void Chipset_overclock_support_is_known(CpuVendor vendor, string chipset, OcSupport cpu, OcSupport memory)
    {
        var entry = Db.FindChipset(vendor, chipset);

        Assert.NotNull(entry);
        Assert.Equal((cpu, memory), (entry.CpuOverclock, entry.MemoryOverclock));
    }

    [Fact]
    public void Chipset_lookup_respects_cpu_vendor() =>
        Assert.Null(Db.FindChipset(CpuVendor.Intel, "B450"));

    [Fact]
    public void Tables_are_consistent()
    {
        Assert.Equal(Db.Cpu.Architectures.Count, Db.Cpu.Architectures.Select(a => a.Id).Distinct().Count());
        Assert.All(Db.Gpu.Gpus, g => Assert.True(g.Index > 0, g.Name));
        var duplicates = Db.Gpu.Gpus
            .GroupBy(g => (g.Vendor, Match: string.Join(' ', HardwareDatabase.NormalizeGpuName(g.Match))))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Match);
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Override_directory_takes_precedence_and_invalid_files_are_rejected()
    {
        var dir = Directory.CreateTempSubdirectory("pco-db-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, HardwareDatabase.ScoringFileName), """{ "thresholds": { "mid": 30, "high": 60 } }""");
            Assert.Equal(60, HardwareDatabase.Load(dir).Scoring.Thresholds.High);

            File.WriteAllText(Path.Combine(dir, HardwareDatabase.MemoryFileName), """{ "patterns": [ { "brand": "x", "regex": "(" } ] }""");
            Assert.Throws<InvalidDataException>(() => HardwareDatabase.Load(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
