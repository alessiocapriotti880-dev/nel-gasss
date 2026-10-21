using PcOptimizer.Benchmark.Comparison;
using PcOptimizer.Benchmark.Model;

namespace PcOptimizer.Benchmark.Tests;

public class ComparisonTests
{
    [Fact]
    public void LargeReductionIsImproved()
    {
        var before = new[] { Runs.Latency(400, 300, 1) };
        var after = new[] { Runs.Latency(200, 300, 2) };

        var result = BenchmarkComparer.Compare(before, after, [MetricCatalog.DpcMax, MetricCatalog.TimerP99]);

        var dpc = result.Single(r => r.MetricId == MetricCatalog.DpcMax);
        Assert.Equal(ComparisonVerdict.Improved, dpc.Verdict);
        Assert.InRange(dpc.RelativeChange, -0.55, -0.45);
        Assert.True(dpc.PValue < 0.001);
        Assert.Equal(ComparisonVerdict.Unchanged, result.Single(r => r.MetricId == MetricCatalog.TimerP99).Verdict);
    }

    [Fact]
    public void LargeIncreaseIsWorse()
    {
        var r = BenchmarkComparer.Compare([Runs.Latency(200, 100, 1)], [Runs.Latency(200, 180, 2)], [MetricCatalog.TimerP99]).Single();
        Assert.Equal(ComparisonVerdict.Worse, r.Verdict);
    }

    [Fact]
    public void SignificantButTinyChangeIsUnchanged()
    {
        // 3% in meno con rumore bassissimo: statisticamente significativo ma sotto la soglia del 10%.
        var r = BenchmarkComparer.Compare([Runs.Latency(400, 300, 1, spread: 0.005)], [Runs.Latency(388, 300, 2, spread: 0.005)], [MetricCatalog.DpcMax]).Single();
        Assert.True(r.PValue < 0.05);
        Assert.Equal(ComparisonVerdict.Unchanged, r.Verdict);
    }

    [Fact]
    public void NoiseBetweenBaselineRunsRaisesTheBar()
    {
        // Le due sessioni "prima" differiscono del 40% fra loro: un -25% non basta.
        var before = new[] { Runs.Latency(300, 300, 1, spread: 0.02), Runs.Latency(420, 300, 2, spread: 0.02) };
        var after = new[] { Runs.Latency(270, 300, 3, spread: 0.02) };

        var r = BenchmarkComparer.Compare(before, after, [MetricCatalog.DpcMax]).Single();

        Assert.True(r.NoiseFloor > 0.3);
        Assert.Equal(ComparisonVerdict.Unchanged, r.Verdict);
    }

    [Fact]
    public void MissingDataIsInsufficient()
    {
        var withoutEtw = Runs.Latency(400, 300, 1) with { DpcIsr = null };
        var r = BenchmarkComparer.Compare([withoutEtw], [Runs.Latency(200, 300, 2)], [MetricCatalog.DpcMax]).Single();
        Assert.Equal(ComparisonVerdict.Insufficient, r.Verdict);

        var few = BenchmarkComparer.Compare([Runs.Latency(400, 300, 1, windows: 5)], [Runs.Latency(100, 300, 2, windows: 5)], [MetricCatalog.DpcMax]).Single();
        Assert.Equal(ComparisonVerdict.Insufficient, few.Verdict);
    }

    [Fact]
    public void UnknownMetricThrows() =>
        Assert.Throws<ArgumentException>(() => BenchmarkComparer.Compare([], [], ["inesistente"]));

    [Fact]
    public void AllMetricsHaveUniqueIdsAndExtractors()
    {
        Assert.Equal(MetricCatalog.All.Count, MetricCatalog.All.Select(m => m.Id).Distinct().Count());
        var empty = new BenchmarkRun { Environment = Runs.Env };
        Assert.All(MetricCatalog.All, m => Assert.True(m.Samples(empty) is null or { Length: 0 }));
    }

    [Fact]
    public void BootMetricUsesLatestNonUpdateBootPerSession()
    {
        BenchmarkRun Boot(params double[] seconds) => new()
        {
            Environment = Runs.Env,
            Boot = new BootResult { Boots = seconds.Select(s => new BootRecord(DateTimeOffset.Now, s * 1000, 0, 0, false)).ToList() },
        };

        var metric = MetricCatalog.Find(MetricCatalog.BootTime)!;
        Assert.Equal([30.0], metric.Samples(Boot(30, 45)));

        var r = BenchmarkComparer.Compare(metric, [Boot(60), Boot(62), Boot(61)], [Boot(40), Boot(41), Boot(39)]);
        Assert.Equal(ComparisonVerdict.Unchanged, r.Verdict);   // 3 contro 3: p esatto minimo 0,1
        Assert.InRange(r.RelativeChange, -0.35, -0.3);
    }
}
