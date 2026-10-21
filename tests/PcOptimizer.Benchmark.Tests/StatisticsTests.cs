using PcOptimizer.Benchmark.Statistics;

namespace PcOptimizer.Benchmark.Tests;

public class StatisticsTests
{
    [Fact]
    public void Summarize_ComputesPercentilesWithLinearInterpolation()
    {
        var s = Descriptive.Summarize([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        Assert.Equal(10, s.Count);
        Assert.Equal(5.5, s.Mean, 9);
        Assert.Equal(5.5, s.Median, 9);
        Assert.Equal(9.55, s.P95, 9);
        Assert.Equal(9.91, s.P99, 9);
        Assert.Equal(1, s.Min);
        Assert.Equal(10, s.Max);
        Assert.Equal(3.0276503540974917, s.StdDev, 9);
    }

    [Fact]
    public void Summarize_IgnoresNonFiniteValuesAndHandlesEmpty()
    {
        Assert.Equal(SampleSummary.Empty, Descriptive.Summarize([]));
        Assert.Equal(2, Descriptive.Summarize([1, double.NaN, 3, double.PositiveInfinity]).Count);
    }

    [Fact]
    public void DecimateMax_KeepsSpikes()
    {
        var values = Enumerable.Repeat(16.6, 1000).ToArray();
        values[537] = 80;

        var reduced = Descriptive.DecimateMax(values, 100);

        Assert.Equal(100, reduced.Length);
        Assert.Contains(80, reduced);
        Assert.Equal(99, reduced.Count(v => v == 16.6));
    }

    [Fact]
    public void MannWhitney_ExactPValueForSmallSamples()
    {
        var r = MannWhitney.Test([1, 2, 3], [4, 5, 6]);
        Assert.True(r.Exact);
        Assert.Equal(0, r.U);
        Assert.Equal(0.1, r.PValue, 9);

        var r2 = MannWhitney.Test([1, 2, 3, 5], [4, 6, 7, 8, 9]);
        Assert.Equal(1, r2.U);
        Assert.Equal(4.0 / 126, r2.PValue, 9);
    }

    [Fact]
    public void MannWhitney_NormalApproximationWithTies_MatchesReference()
    {
        double[] a = [1.1, 2.2, 3.3, 4.4, 5.5, 6.6, 7.7, 8.8, 9.9, 10, 11, 12, 13, 14, 15, 16];
        double[] b = [5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20];

        var r = MannWhitney.Test(a, b);

        Assert.False(r.Exact);
        Assert.Equal(74.5, r.U, 9);
        // Riferimento calcolato con la formula standard (varianza corretta per i pari merito, correzione di continuità).
        Assert.Equal(0.0456305078663863, r.PValue, 5);
    }

    [Fact]
    public void MannWhitney_IdenticalSamplesAreNotSignificant()
    {
        var same = Enumerable.Range(0, 40).Select(i => (double)(i % 7)).ToArray();
        Assert.True(MannWhitney.Test(same, same).PValue > 0.9);
        Assert.Equal(1, MannWhitney.Test([5, 5, 5], [5, 5, 5]).PValue);
    }

    [Fact]
    public void MannWhitney_LargeShiftIsHighlySignificant()
    {
        var random = new Random(7);
        var a = Enumerable.Range(0, 60).Select(_ => 100 + random.NextDouble() * 20).ToArray();
        var b = Enumerable.Range(0, 60).Select(_ => 80 + random.NextDouble() * 20).ToArray();
        Assert.True(MannWhitney.Test(a, b).PValue < 1e-6);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.5, 0.4795001221869535)]
    [InlineData(1.5, 0.033894853524689274)]
    [InlineData(-1, 1.8427007929497148)]
    public void Erfc_IsAccurate(double x, double expected) =>
        Assert.Equal(expected, MannWhitney.Erfc(x), 6);

    [Fact]
    public void Reservoir_KeepsCapacityAndCountsAll()
    {
        var r = new Reservoir(100);
        for (var i = 0; i < 10_000; i++)
        {
            r.Add(i);
        }

        Assert.Equal(10_000, r.Seen);
        Assert.Equal(100, r.Items.Count);
        // Campione uniforme: la media deve stare vicino a quella della popolazione (4999,5).
        Assert.InRange(r.Items.Average(), 3500, 6500);
    }
}
