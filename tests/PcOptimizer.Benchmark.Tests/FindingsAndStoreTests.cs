using PcOptimizer.Benchmark.Assessment;
using PcOptimizer.Benchmark.Catalog;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Storage;
using PcOptimizer.Core;
using PcOptimizer.Core.Model;

namespace PcOptimizer.Benchmark.Tests;

public class FindingsAndStoreTests
{
    private static readonly KnownDriverCatalog Drivers = KnownDriverCatalog.Load();

    [Fact]
    public void EmbeddedDriverCatalogLoadsAndMatchesWildcards()
    {
        Assert.True(Drivers.Entries.Count > 20);
        Assert.Equal("Wi-Fi Intel", Drivers.Find("Netwtw10.sys")?.Name);
        Assert.Equal("Driver video NVIDIA", Drivers.Find("NVLDDMKM.SYS")?.Name);
        Assert.Equal("Ethernet Intel", Drivers.Find("e1d68x64.sys")?.Name);
        Assert.Null(Drivers.Find("sconosciuto.sys"));
    }

    [Theory]
    [InlineData("rtwlan*.sys", "rtwlane601.sys", true)]
    [InlineData("rtwlan*.sys", "rtwlane601.dll", false)]
    [InlineData("e1d*.sys", "e1.sys", false)]
    [InlineData("ndis.sys", "NDIS.SYS", true)]
    public void WildcardMatching(string pattern, string name, bool expected) =>
        Assert.Equal(expected, KnownDriverCatalog.Matches(pattern, name));

    [Fact]
    public void RepeatedHighDpcProducesFindingWithDriverHint()
    {
        var windows = Enumerable.Repeat(150.0, 60).ToArray();
        windows[10] = windows[30] = windows[50] = 1500;
        var run = new BenchmarkRun
        {
            Environment = Runs.Env,
            DpcIsr = new DpcIsrResult
            {
                WindowMaxDpcUs = windows,
                WindowMaxIsrUs = new double[60],
                WindowLoadPercent = new double[60],
                MaxDpcUs = 1500,
                Drivers = [new DriverLatency("sconosciuto", null, 1, 0, 2000, 0, 1, 1), new DriverLatency("Netwtw10.sys", "Wi-Fi Intel", 100, 0, 1500, 0, 5, 3)],
            },
        };

        var findings = BenchmarkFindings.Evaluate(run, Drivers);

        var f = Assert.Single(findings, f => f.Id == "bench-dpc-high");
        Assert.Equal(FindingSeverity.Suggestion, f.Severity);
        Assert.Contains("Netwtw10.sys (Wi-Fi Intel)", f.Detail);
        Assert.Contains("cavo Ethernet", f.Recommendation);
    }

    [Fact]
    public void IsolatedSpikeIsNotAProblem()
    {
        var windows = Enumerable.Repeat(150.0, 200).ToArray();
        windows[3] = 1200;
        var run = new BenchmarkRun
        {
            Environment = Runs.Env,
            DpcIsr = new DpcIsrResult { WindowMaxDpcUs = windows, WindowMaxIsrUs = new double[200], MaxDpcUs = 1200 },
        };

        var findings = BenchmarkFindings.Evaluate(run, Drivers);

        Assert.DoesNotContain(findings, f => f.Id == "bench-dpc-high");
        Assert.Contains(findings, f => f.Id == "bench-dpc-ok");
    }

    [Fact]
    public void BackgroundLoadBatteryAndRecentBootAreFlagged()
    {
        var run = new BenchmarkRun
        {
            Environment = Runs.Env with { OnAcPower = false, UptimeMinutes = 2 },
            Cpu = new CpuActivityResult
            {
                Busy = new Statistics.SampleSummary(60, 25, 25, 30, 35, 20, 40, 3),
                TopProcesses = [new ProcessActivity("MsMpEng.exe", 4000, 18)],
            },
        };

        var ids = BenchmarkFindings.Evaluate(run, Drivers).Select(f => f.Id).ToList();

        Assert.Contains("bench-on-battery", ids);
        Assert.Contains("bench-recent-boot", ids);
        Assert.Contains("bench-background-load", ids);
    }

    [Fact]
    public void FrameAndNetworkFindings()
    {
        var run = new BenchmarkRun
        {
            Environment = Runs.Env,
            Scenario = BenchmarkScenario.Game,
            Frames = new FrameResult { Frames = 1000, StutterCount = 30, PresentMode = "Composed: Flip", PresentModeShare = 0.9, VSync = true, GpuBoundShare = 0.3, AverageFps = 140, OnePercentLowFps = 40 },
            Network = new NetworkResult
            {
                AdapterType = "Wi-Fi",
                Targets =
                [
                    new PingTargetResult { Kind = "gateway", Target = "192.168.1.1", Sent = 200, Received = 190, LossPercent = 5, JitterMs = 8, RttMs = new Statistics.SampleSummary(190, 5, 4, 20, 30, 1, 40, 5) },
                ],
            },
        };

        var ids = BenchmarkFindings.Evaluate(run, Drivers).Select(f => f.Id).ToList();

        Assert.Contains("bench-frames-composed", ids);
        Assert.Contains("bench-frames-vsync", ids);
        Assert.Contains("bench-frames-cpu-bound", ids);
        Assert.Contains("bench-frames-stutter", ids);
        Assert.Contains("bench-net-gateway-loss", ids);
        Assert.Contains("bench-net-gateway-jitter", ids);
        Assert.Contains("bench-net-wifi", ids);
    }

    [Fact]
    public void Store_SavesListsAndLoadsRuns()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcopt-bench-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BenchmarkStore(new AppPaths(root));
            var older = Runs.Latency(300, 200, 1) with { Label = "Prima: piano Bilanciato!" };
            var newer = Runs.Latency(250, 200, 2) with { Label = "dopo", Score = new LatencyScore(80, false, []) };
            var path = store.Save(older);
            store.Save(newer);
            File.WriteAllText(Path.Combine(root, "benchmarks", "rotto.json"), "{ non json");

            Assert.Contains("prima-piano-bilanciato", Path.GetFileName(path));
            var list = store.List();
            Assert.Equal(2, list.Count);
            Assert.Equal(newer.Id, list[0].Id);
            Assert.Equal(80, list[0].Score);

            var loaded = store.Find(older.Id)!;
            Assert.Equal(older.DpcIsr!.WindowMaxDpcUs, loaded.DpcIsr!.WindowMaxDpcUs);
            Assert.Equal(older.Timer!.LatenessUs, loaded.Timer!.LatenessUs);
            Assert.Equal("Prima: piano Bilanciato!", loaded.Label);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("", "misura")]
    [InlineData("  ", "misura")]
    [InlineData("Dopo MSI (GPU)", "dopo-msi-gpu")]
    public void Slug(string label, string expected) => Assert.Equal(expected, BenchmarkStore.Slug(label));

    [Fact]
    public void NativeStructSizes()
    {
        Assert.Equal(64, System.Runtime.InteropServices.Marshal.SizeOf<Windows.D3dkmtAdapterPerfData>());
        Assert.Equal(24, System.Runtime.InteropServices.Marshal.SizeOf<Windows.D3dkmtQueryAdapterInfo>());
    }
}
