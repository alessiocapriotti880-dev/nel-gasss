using System.Diagnostics;
using System.Runtime.Versioning;
using PcOptimizer.Benchmark.Latency;
using PcOptimizer.Benchmark.Model;

namespace PcOptimizer.Benchmark.Windows;

/// <summary>
/// Misura il ritardo di risveglio con un timer ad alta risoluzione di Windows 10 1803+ (non serve cambiare la
/// risoluzione globale del timer, che altererebbe proprio ciò che si misura). Usa solo un thread di questo processo.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TimerLatencyProbe
{
    public static Task<TimerLatencyResult> RunAsync(TimeSpan duration, double intervalMs = 1, CancellationToken cancellationToken = default) =>
        Task.Factory.StartNew(() => Run(duration, intervalMs, cancellationToken), cancellationToken,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static TimerLatencyResult Run(TimeSpan duration, double intervalMs, CancellationToken cancellationToken)
    {
        var timer = BenchmarkNative.CreateWaitableTimerExW(
            IntPtr.Zero, null, BenchmarkNative.CreateWaitableTimerHighResolution, BenchmarkNative.TimerAllAccess);
        if (timer == IntPtr.Zero)
        {
            throw new NotSupportedException("Timer ad alta risoluzione non disponibile (serve Windows 10 versione 1803 o successiva).");
        }

        var previousPriority = Thread.CurrentThread.Priority;
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
        try
        {
            var aggregator = new TimerLatencyAggregator();
            var intervalTicks = intervalMs * Stopwatch.Frequency / 1000;
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < duration && !cancellationToken.IsCancellationRequested)
            {
                var due = -(long)(intervalMs * 10_000);
                var start = Stopwatch.GetTimestamp();
                if (!BenchmarkNative.SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false) ||
                    BenchmarkNative.WaitForSingleObject(timer, 1000) != BenchmarkNative.WaitObject0)
                {
                    continue;
                }

                var late = (Stopwatch.GetTimestamp() - start - intervalTicks) * 1_000_000.0 / Stopwatch.Frequency;
                aggregator.Add(clock.Elapsed.TotalSeconds, late);
            }

            return aggregator.Build(clock.Elapsed.TotalSeconds, intervalMs);
        }
        finally
        {
            Thread.CurrentThread.Priority = previousPriority;
            BenchmarkNative.CloseHandle(timer);
        }
    }
}
