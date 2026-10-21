using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PcOptimizer.Benchmark.Cpu;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Statistics;

namespace PcOptimizer.Benchmark.Windows;

/// <summary>Uso CPU al secondo (GetSystemTimes) e processi più attivi (elenco di sistema, senza handle).</summary>
[SupportedOSPlatform("windows")]
public static class CpuSampler
{
    public static async Task<CpuActivityResult> RunAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var before = ProcessTimes();
        var clock = Stopwatch.StartNew();
        var previous = Now();
        var windows = new List<double>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (clock.Elapsed < duration && await timer.WaitForNextTickAsync(cancellationToken))
        {
            var now = Now();
            windows.Add(CpuMath.BusyPercent(previous, now));
            previous = now;
        }

        var after = ProcessTimes();
        return new CpuActivityResult
        {
            WindowBusyPercent = windows.ToArray(),
            Busy = Descriptive.Summarize(windows),
            TopProcesses = CpuMath.TopProcesses(before, after, clock.Elapsed.TotalSeconds, Environment.ProcessorCount),
        };
    }

    private static SystemTimes Now() =>
        BenchmarkNative.GetSystemTimes(out var idle, out var kernel, out var user) ? new SystemTimes(idle, kernel, user) : default;

    /// <summary>Istantanea dei tempi CPU di tutti i processi tramite NtQuerySystemInformation.</summary>
    public static IReadOnlyList<ProcessCpuTimes> ProcessTimes()
    {
        var size = 512 * 1024;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = BenchmarkNative.NtQuerySystemInformation(BenchmarkNative.SystemProcessInformation, buffer, size, out var needed);
                if (status == BenchmarkNative.StatusInfoLengthMismatch)
                {
                    size = Math.Max(size * 2, needed + 64 * 1024);
                    continue;
                }

                if (status != 0)
                {
                    return [];
                }

                var copy = new byte[needed > 0 ? needed : size];
                Marshal.Copy(buffer, copy, 0, copy.Length);
                return SystemProcessInfoParser.Parse(copy, buffer.ToInt64());
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return [];
    }
}
