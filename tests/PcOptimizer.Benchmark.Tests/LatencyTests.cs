using PcOptimizer.Benchmark.Catalog;
using PcOptimizer.Benchmark.Latency;

namespace PcOptimizer.Benchmark.Tests;

public class LatencyTests
{
    private const ulong NdisBase = 0xFFFFF80000100000;
    private const ulong NvidiaBase = 0xFFFFF80000500000;

    [Fact]
    public void KernelModuleMap_ResolvesAddressesInsideModules()
    {
        var map = new KernelModuleMap();
        map.Add(NvidiaBase, 0x100000, @"\SystemRoot\System32\DriverStore\FileRepository\nv_dispi.inf_amd64\nvlddmkm.sys");
        map.Add(NdisBase, 0x10000, @"\SystemRoot\system32\drivers\ndis.sys");
        map.Add(0, 100, "zero.sys");

        Assert.Equal(2, map.Count);
        Assert.Equal("ndis.sys", map.Resolve(NdisBase + 0x20));
        Assert.Equal("nvlddmkm.sys", map.Resolve(NvidiaBase + 0xFFFFF));
        Assert.Null(map.Resolve(NvidiaBase + 0x100000));
        Assert.Null(map.Resolve(1234));
    }

    [Fact]
    public void DpcIsrAggregator_BuildsWindowsAndDriverStats()
    {
        var agg = new DpcIsrAggregator(startMs: 1000, logicalProcessors: 4);
        agg.Modules.Add(NdisBase, 0x10000, @"\SystemRoot\system32\drivers\ndis.sys");
        agg.Modules.Add(NvidiaBase, 0x100000, "nvlddmkm.sys");

        agg.AddDpc(500, 9999, NdisBase + 1);          // durante l'avvio: ignorato
        agg.AddDpc(1100, 50, NdisBase + 1);           // finestra 0
        agg.AddDpc(1200, 700, NvidiaBase + 8);        // finestra 0
        agg.AddIsr(1300, 20, NdisBase + 2);           // finestra 0
        agg.AddDpc(3500, 120, NdisBase + 1);          // finestra 2
        agg.AddDpc(3600, 5_000_000, NdisBase + 1);    // implausibile: ignorato
        agg.AddDpc(3700, 30, 0x1234);                 // modulo sconosciuto
        agg.AddLost(3);

        var r = agg.Build(durationSeconds: 3, new KnownDriverCatalog([new KnownDriverEntry { Patterns = ["ndis.sys"], Name = "Rete (NDIS)", Hint = "x" }]));

        Assert.Equal(4, r.DpcCount);
        Assert.Equal(1, r.IsrCount);
        Assert.Equal(700, r.MaxDpcUs);
        Assert.Equal(20, r.MaxIsrUs);
        Assert.Equal([700, 0, 120], r.WindowMaxDpcUs);
        Assert.Equal([20, 0, 0], r.WindowMaxIsrUs);
        // 770 µs su 4 processori per 1 s = 0,01925%.
        Assert.Equal(770 / 4e6 * 100, r.WindowLoadPercent[0], 9);
        Assert.Equal(3, r.LostEvents);

        Assert.Equal("nvlddmkm.sys", r.Drivers[0].Module);
        Assert.Equal(1, r.Drivers[0].Over500UsCount);
        var ndis = r.Drivers.Single(d => d.Module == "ndis.sys");
        Assert.Equal("Rete (NDIS)", ndis.Description);
        Assert.Equal(2, ndis.DpcCount);
        Assert.Equal(1, ndis.IsrCount);
        Assert.Equal((50 + 20 + 120) / 1000.0, ndis.TotalMs, 9);
        Assert.Contains(r.Drivers, d => d.Module == "sconosciuto");
    }

    [Fact]
    public void TimerAggregator_DropsIncompleteLastWindow()
    {
        var agg = new TimerLatencyAggregator();
        for (var i = 0; i < 1000; i++)
        {
            agg.Add(i / 1000.0, 100);
        }

        for (var i = 0; i < 1000; i++)
        {
            agg.Add(1 + i / 1000.0, i >= 980 ? 2000 : 50);
        }

        agg.Add(2.01, 10);    // terza finestra quasi vuota
        agg.Add(2.02, -3);    // anticipo: vale 0

        var r = agg.Build(2.02, 1);

        Assert.Equal(2002, r.Samples);
        Assert.Equal(2, r.WindowMeanUs.Length);
        Assert.Equal(100, r.WindowMeanUs[0], 9);
        Assert.Equal(2000, r.WindowP99Us[1], 9);
        Assert.Equal(50, r.WindowP99Us[0] / 2, 9);
        Assert.Equal(2000, r.LatenessUs.Max);
        Assert.Equal(0, r.LatenessUs.Min);
    }

    [Theory]
    [InlineData(50, 100)]
    [InlineData(100, 100)]
    [InlineData(2000, 0)]
    [InlineData(5000, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void Scale_MapsGoodAndBadBounds(double value, double expected) =>
        Assert.Equal(expected, LatencyScorer.Scale(value, 100, 2000), 9);

    [Fact]
    public void Scale_IsLogarithmicBetweenBounds()
    {
        // Media geometrica fra 100 e 10000 = 1000: metà punteggio.
        Assert.Equal(50, LatencyScorer.Scale(1000, 100, 10000), 9);
    }

    [Fact]
    public void Score_IsPartialWithoutEtw()
    {
        var timer = new Model.TimerLatencyResult
        {
            Samples = 100,
            LatenessUs = new Statistics.SampleSummary(100, 30, 25, 40, 50, 0, 60, 5),
        };

        var score = LatencyScorer.Compute(null, timer);

        Assert.NotNull(score);
        Assert.True(score.Partial);
        Assert.Equal(100, score.Score);
        Assert.Null(LatencyScorer.Compute(null, null));
    }

    [Fact]
    public void Score_CombinesAllComponents()
    {
        var dpc = new Model.DpcIsrResult
        {
            WindowMaxDpcUs = Enumerable.Repeat(2000.0, 20).ToArray(),
            WindowMaxIsrUs = Enumerable.Repeat(10.0, 20).ToArray(),
            WindowLoadPercent = Enumerable.Repeat(0.1, 20).ToArray(),
        };
        var timer = new Model.TimerLatencyResult { Samples = 100, LatenessUs = new Statistics.SampleSummary(100, 0, 0, 0, 40, 0, 0, 0) };

        var score = LatencyScorer.Compute(dpc, timer)!;

        Assert.False(score.Partial);
        Assert.Equal(4, score.Components.Count);
        // DPC a 0 (peso 0,35), gli altri a 100: 65.
        Assert.Equal(65, score.Score);
    }
}
