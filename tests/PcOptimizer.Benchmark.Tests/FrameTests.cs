using System.Globalization;
using System.Text;
using PcOptimizer.Benchmark.Frames;

namespace PcOptimizer.Benchmark.Tests;

public class FrameTests
{
    private const string V1Header =
        "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents,msUntilRenderComplete,msUntilDisplayed,msBetweenDisplayChange";

    private const string V2Header =
        "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,CPUStartTime,FrameTime,CPUBusy,CPUWait,GPULatency,GPUTime,GPUBusy,GPUWait,DisplayLatency,DisplayedTime,ClickToPhotonLatency";

    private static string V1(IEnumerable<double> frameTimes, string app = "cs2.exe")
    {
        var sb = new StringBuilder(V1Header).AppendLine();
        double t = 0;
        var i = 0;
        foreach (var ft in frameTimes)
        {
            t += ft / 1000;
            var dropped = i++ % 50 == 49 ? 1 : 0;
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{app},1234,0x0000021A,DXGI,0,512,1,Hardware: Independent Flip,{dropped},{t:0.000000},0.05,{ft:0.0000},1.2,{(dropped == 1 ? 0 : 5.5)},{ft:0.0000}");
        }

        return sb.ToString();
    }

    private static string V2(IEnumerable<double> frameTimes, double gpuShare, string mode = "Composed: Flip", int sync = 1)
    {
        var sb = new StringBuilder(V2Header).AppendLine();
        double t = 0;
        foreach (var ft in frameTimes)
        {
            t += ft / 1000;
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"game.exe,99,0x1,DXGI,{sync},0,0,{mode},{t:0.000000},{ft:0.0000},{ft * 0.5:0.0000},0.1,0.2,{ft * gpuShare:0.0000},{ft * gpuShare:0.0000},0,12.5,{ft:0.0000},NA");
        }

        // Riga di un altro processo: va ignorata scegliendo il gioco.
        sb.AppendLine("explorer.exe,4,0x2,DXGI,1,0,0,Composed: Flip,0.5,16.6,1,1,1,1,1,1,1,NA,NA");
        return sb.ToString();
    }

    [Fact]
    public void ParsesPresentMon1Columns()
    {
        var capture = PresentMonCsvParser.Parse(new StringReader(V1(Enumerable.Repeat(4.0, 500))));

        Assert.Equal("PresentMon 1.x", capture.Format);
        Assert.Equal(500, capture.Frames.Count);
        var f = capture.Frames[0];
        Assert.Equal("cs2.exe", f.Application);
        Assert.Equal(4, f.FrameTimeMs);
        Assert.Equal(5.5, f.DisplayLatencyMs);
        Assert.Equal("Hardware: Independent Flip", f.PresentMode);
        Assert.Equal(0, f.SyncInterval);
        Assert.True(f.AllowsTearing);
        Assert.Equal(10, capture.Frames.Count(x => x.Dropped));
    }

    [Fact]
    public void ParsesPresentMon2ColumnsAndNotAvailableValues()
    {
        var capture = PresentMonCsvParser.Parse(new StringReader(V2(Enumerable.Repeat(10.0, 200), 0.95)));

        Assert.Equal("PresentMon 2.x", capture.Format);
        Assert.Equal(("game.exe", 200), capture.Applications[0]);
        var f = capture.Frames[0];
        Assert.Equal(9.5, f.GpuBusyMs!.Value, 6);
        Assert.Equal(5, f.CpuBusyMs!.Value, 6);
        Assert.Null(f.ClickToPhotonMs);
        Assert.False(f.Dropped);
        Assert.True(capture.Frames.Single(x => x.Application == "explorer.exe").Dropped);
    }

    [Fact]
    public void RejectsFilesWithoutFrameTime() =>
        Assert.Throws<InvalidDataException>(() => PresentMonCsvParser.Parse(new StringReader("a,b,c\n1,2,3")));

    [Fact]
    public void RebuildsTimeWhenColumnIsNotInSeconds()
    {
        var csv = "Application,CPUStartTime,FrameTime\n" + string.Join('\n',
            Enumerable.Range(0, 100).Select(i => $"g.exe,{1_000_000_000L + i * 166_000L},16.6"));

        var capture = PresentMonCsvParser.Parse(new StringReader(csv));

        Assert.Equal(1.66, capture.Frames[^1].TimeSeconds, 6);
    }

    [Fact]
    public void SplitCsv_HandlesQuotes() =>
        Assert.Equal(["a", "b,c", "d\"e", ""], PresentMonCsvParser.SplitCsv("a,\"b,c\",\"d\"\"e\","));

    [Fact]
    public void Analyze_ComputesFpsLowsAndStutters()
    {
        // 240 FPS costanti con 5 scatti da 30 ms.
        var times = Enumerable.Repeat(4.1666667, 1000).ToArray();
        foreach (var i in new[] { 100, 300, 500, 700, 900 })
        {
            times[i] = 30;
        }

        var r = FrameAnalyzer.Analyze(PresentMonCsvParser.Parse(new StringReader(V1(times))))!;

        Assert.Equal("cs2.exe", r.Application);
        Assert.Equal(1000, r.Frames);
        // Il CSV ha 4 decimali: la media differisce di qualche millesimo.
        Assert.Equal(1000 / times.Average(), r.AverageFps, 1);
        Assert.Equal(5, r.StutterCount);
        Assert.True(r.PointOnePercentLowFps < 40);
        Assert.Equal(2, r.DroppedPercent, 6);
        Assert.Equal("Hardware: Independent Flip", r.PresentMode);
        Assert.False(r.VSync);
        Assert.True(r.AllowsTearing);
        Assert.Equal(5.5, r.AverageDisplayLatencyMs!.Value, 6);
        Assert.Null(r.GpuBoundShare);
        Assert.True(r.WindowMeanFrameTimeMs.Length >= 4);
        Assert.Equal(r.WindowMeanFrameTimeMs.Length, r.WindowP99FrameTimeMs.Length);
    }

    [Fact]
    public void Analyze_DetectsGpuBoundAndComposedVsync()
    {
        var capture = PresentMonCsvParser.Parse(new StringReader(V2(Enumerable.Repeat(10.0, 300), 0.97)));

        var r = FrameAnalyzer.Analyze(capture, "game")!;

        Assert.Equal("game.exe", r.Application);
        Assert.Equal(1.0, r.GpuBoundShare!.Value, 6);
        Assert.Equal("Composed: Flip", r.PresentMode);
        Assert.True(r.VSync);
        Assert.Equal(100, r.AverageFps, 6);
        Assert.Equal(100, r.OnePercentLowFps, 6);
    }

    [Fact]
    public void Analyze_ReturnsNullForMissingApplication() =>
        Assert.Null(FrameAnalyzer.Analyze(PresentMonCsvParser.Parse(new StringReader(V1([4, 4, 4]))), "valorant.exe"));

    [Fact]
    public void StutterDetection_AdaptsToSceneChanges()
    {
        // Passare da 144 a 60 FPS non è uno scatto ripetuto: solo il primo frame lento supera il doppio della mediana.
        var times = Enumerable.Repeat(6.9, 200).Concat(Enumerable.Repeat(16.6, 200)).ToArray();
        Assert.True(FrameAnalyzer.CountStutters(times) <= 31);
        Assert.Equal(0, FrameAnalyzer.CountStutters(Enumerable.Repeat(6.9, 500).ToArray()));
    }

    [Theory]
    [InlineData(2, "--process_name")]
    [InlineData(1, "-process_name")]
    public void PresentMonArguments_UseVersionSpecificDashes(int version, string expected)
    {
        var args = PresentMonArguments.Build(version, "cs2", @"C:\temp\out.csv", 60, 5);

        Assert.Equal(expected, args[0]);
        Assert.Equal("cs2.exe", args[1]);
        Assert.Contains(@"C:\temp\out.csv", args);
        Assert.Equal("5", args[^1]);
        Assert.Contains(args, a => a.EndsWith("terminate_after_timed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(@"C:\tools\PresentMon-2.3.1-x64.exe", 2)]
    [InlineData("PresentMon-1.10.0-x64.exe", 1)]
    [InlineData("PresentMon64-1.6.0.exe", null)]
    [InlineData("presentmon.exe", null)]
    public void PresentMonArguments_VersionFromFileName(string file, int? expected) =>
        Assert.Equal(expected, PresentMonArguments.MajorVersionFromFileName(file));
}
