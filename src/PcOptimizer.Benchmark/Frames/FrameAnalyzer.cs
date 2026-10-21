using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Statistics;

namespace PcOptimizer.Benchmark.Frames;

/// <summary>Calcolo di FPS medi, 1%/0,1% low, scatti e indicatori di limite CPU/GPU da una cattura PresentMon.</summary>
public static class FrameAnalyzer
{
    private const int StutterWindow = 60;
    private const int ChartPoints = 5000;

    /// <param name="application">Processo da analizzare; se null, quello con più frame.</param>
    public static FrameResult? Analyze(PresentMonCapture capture, string? application = null, string? source = null)
    {
        var app = application is null
            ? capture.Applications.FirstOrDefault().Application
            : capture.Applications.Select(a => a.Application)
                .FirstOrDefault(a => string.Equals(a, application, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(application), StringComparison.OrdinalIgnoreCase));
        if (app is null)
        {
            return null;
        }

        var frames = capture.Frames.Where(f => string.Equals(f.Application, app, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.TimeSeconds)
            .ToList();
        if (frames.Count < 2)
        {
            return null;
        }

        var times = frames.Select(f => f.FrameTimeMs).ToArray();
        var summary = Descriptive.Summarize(times);
        var p999 = Descriptive.Percentile(times, 99.9);
        var duration = frames[^1].TimeSeconds - frames[0].TimeSeconds + frames[0].FrameTimeMs / 1000;

        var gpuFrames = frames.Where(f => f.GpuBusyMs is not null).ToList();
        var modes = frames.Where(f => f.PresentMode is not null).GroupBy(f => f.PresentMode!).OrderByDescending(g => g.Count()).FirstOrDefault();
        var sync = frames.Where(f => f.SyncInterval is not null).ToList();
        var tearing = frames.Where(f => f.AllowsTearing is not null).ToList();
        var latency = frames.Where(f => f.DisplayLatencyMs is not null && !f.Dropped).Select(f => f.DisplayLatencyMs!.Value).ToArray();
        var click = frames.Where(f => f.ClickToPhotonMs is > 0).Select(f => f.ClickToPhotonMs!.Value).ToArray();

        var t0 = frames[0].TimeSeconds;
        var windows = frames.GroupBy(f => (int)Math.Floor(f.TimeSeconds - t0))
            .OrderBy(g => g.Key)
            .Select(g => g.Select(f => f.FrameTimeMs).ToArray())
            // L'ultimo secondo è quasi sempre parziale: le finestre con pochi frame falserebbero il P99.
            .Where(w => w.Length >= 5)
            .ToList();

        return new FrameResult
        {
            Application = app,
            Source = source ?? capture.Format,
            DurationSeconds = duration,
            Frames = frames.Count,
            AverageFps = 1000 / summary.Mean,
            OnePercentLowFps = 1000 / summary.P99,
            PointOnePercentLowFps = 1000 / p999,
            FrameTimeMs = summary,
            StutterCount = CountStutters(times),
            DroppedPercent = 100.0 * frames.Count(f => f.Dropped) / frames.Count,
            AverageDisplayLatencyMs = latency.Length > 0 ? latency.Average() : null,
            AverageClickToPhotonMs = click.Length > 0 ? click.Average() : null,
            GpuBoundShare = gpuFrames.Count > frames.Count / 2
                ? (double)gpuFrames.Count(f => f.GpuBusyMs >= 0.9 * f.FrameTimeMs) / gpuFrames.Count
                : null,
            PresentMode = modes?.Key,
            PresentModeShare = modes is null ? 0 : (double)modes.Count() / frames.Count,
            VSync = sync.Count > 0 ? sync.Count(f => f.SyncInterval > 0) > sync.Count / 2 : null,
            AllowsTearing = tearing.Count > 0 ? tearing.Count(f => f.AllowsTearing == true) > tearing.Count / 2 : null,
            WindowMeanFrameTimeMs = windows.Select(w => w.Average()).ToArray(),
            WindowP99FrameTimeMs = windows.Select(w => Descriptive.Percentile(w, 99)).ToArray(),
            FrameTimesMs = Descriptive.DecimateMax(times, ChartPoints),
        };
    }

    /// <summary>Scatto: frame più lungo del doppio della mediana dei 60 precedenti (si adatta ai cambi di scena).</summary>
    internal static int CountStutters(IReadOnlyList<double> times)
    {
        var count = 0;
        var window = new List<double>(StutterWindow);
        for (var i = 0; i < times.Count; i++)
        {
            if (window.Count >= 10)
            {
                var sorted = window.ToArray();
                Array.Sort(sorted);
                var median = Descriptive.PercentileSorted(sorted, 50);
                if (times[i] > 2 * median)
                {
                    count++;
                }
            }

            window.Add(times[i]);
            if (window.Count > StutterWindow)
            {
                window.RemoveAt(0);
            }
        }

        return count;
    }
}
