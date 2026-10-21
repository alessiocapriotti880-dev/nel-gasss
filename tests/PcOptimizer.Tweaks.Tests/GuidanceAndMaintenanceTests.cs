using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Model;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Tweaks.Guidance;
using PcOptimizer.Tweaks.Maintenance;

namespace PcOptimizer.Tweaks.Tests;

public class GuidanceAndMaintenanceTests
{
    [Fact]
    public void Guidance_DependsOnHardwareAndSoftware()
    {
        var snapshot = Fixtures.Snapshot() with
        {
            Processes = [new ProcessEntry { Name = "Discord", Id = 10 }],
        };
        var profile = Fixtures.Profile(systemDisk: DiskMediaType.Hdd) with
        {
            NetworkAdapters = [new NetworkAdapterInfo { Name = "Wi-Fi", Medium = NetworkMedium.WiFi, IsPrimary = true, IsConnected = true }],
        };
        var items = GuidanceCatalog.Build(Fixtures.Context(profile: profile, snapshot: snapshot));
        var ids = items.Select(i => i.Id).ToList();

        Assert.Contains("gpu.nvidia.panel", ids);
        Assert.DoesNotContain("gpu.amd.panel", ids);
        Assert.Contains("gpu.ddu", ids);
        Assert.Contains("storage.ssd", ids);
        Assert.Contains("network.cable", ids);
        Assert.Contains("overlay.discord", ids);
        Assert.Contains("system.notifications", ids);
        Assert.All(items, i => Assert.NotEmpty(i.Steps));
        Assert.Contains("Quando giochi", string.Join(' ', items.Single(i => i.Id == "system.notifications").Steps));
    }

    [Fact]
    public void Guidance_AmdWarnsAboutAntiLagPlus()
    {
        var profile = Fixtures.Profile(gpus: [new GpuInfo { Name = "Radeon RX 7800 XT", Vendor = GpuVendor.Amd, IsPhysicalPciDevice = true }]);
        var amd = GuidanceCatalog.Build(Fixtures.Context(profile: profile)).Single(i => i.Id == "gpu.amd.panel");
        Assert.Contains("Anti-Lag+", amd.Warning);
    }

    [Fact]
    public async Task TempCleanup_DeletesOnlyOldFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcopt-temp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        try
        {
            var old = Path.Combine(root, "sub", "vecchio.tmp");
            var recent = Path.Combine(root, "recente.tmp");
            await File.WriteAllBytesAsync(old, new byte[2048]);
            await File.WriteAllBytesAsync(recent, new byte[1024]);
            var past = DateTime.UtcNow.AddDays(-3);
            File.SetLastWriteTimeUtc(old, past);
            File.SetLastAccessTimeUtc(old, past);

            var cleanup = new TempCleanup([root]);
            Assert.Equal(Risk.Low, cleanup.Risk);
            Assert.StartsWith("1 file", await cleanup.PreviewAsync(CancellationToken.None));

            var result = await cleanup.RunAsync(CancellationToken.None);

            Assert.StartsWith("Eliminati 1 file", result);
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
            Assert.False(Directory.Exists(Path.Combine(root, "sub")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TierProfilesAreDescribed()
    {
        foreach (var tier in Enum.GetValues<HardwareTier>())
        {
            Assert.False(string.IsNullOrWhiteSpace(TierProfiles.Describe(tier)));
        }
    }
}
