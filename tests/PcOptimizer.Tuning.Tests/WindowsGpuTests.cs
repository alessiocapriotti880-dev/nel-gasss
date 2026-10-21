using PcOptimizer.Tuning.Windows;

namespace PcOptimizer.Tuning.Tests;

/// <summary>Test della GPU con il rasterizzatore software WARP di Windows: gira anche senza scheda video (CI).</summary>
public class WindowsGpuTests
{
    [Fact]
    public async Task ComputeAndVramTestsRunOnWarp()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using (var compute = new GpuStressTest(useWarp: true, iterations: 32))
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var (units, errors, notes) = await compute.RunAsync(cancel.Token);
            Assert.True(units >= 2, string.Join(" ", notes));
            Assert.Equal(0, errors);
        }

        using (var vram = new GpuStressTest(vramBytes: 128L * 1024 * 1024, useWarp: true))
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var (passes, errors, _) = await vram.RunAsync(cancel.Token);
            Assert.True(passes >= 1);
            Assert.Equal(0, errors);
        }
    }
}
