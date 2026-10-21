using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Sensors;

namespace PcOptimizer.Benchmark.Windows;

/// <summary>
/// Letture periodiche dei sensori da tutte le fonti disponibili: NVML (GPU NVIDIA), D3DKMT (GPU di ogni produttore)
/// e HWiNFO (CPU: temperatura, potenza, Vcore, throttling). Usato anche dai test di stabilità del modulo Tuning.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SensorSampler : IDisposable
{
    private readonly ILogger _logger;
    private readonly Nvml? _nvml;
    private readonly GpuPerfReader? _gpu;

    public SensorSampler(ILogger logger)
    {
        _logger = logger;
        try
        {
            _nvml = Nvml.TryOpen();
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            _logger.LogInformation(ex, "NVML non disponibile");
        }

        try
        {
            _gpu = new GpuPerfReader();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _logger.LogInformation(ex, "D3DKMT non disponibile");
        }
    }

    public IReadOnlyList<string> GpuNames =>
        (_nvml?.Devices.Select(d => d.Name) ?? []).Concat(_gpu?.AdapterNames ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public bool HasNvidia => _nvml is { Devices.Count: > 0 };

    /// <summary>Una lettura completa. HWiNFO viene riletto ogni volta: può essere aperto o chiuso durante la misura.</summary>
    public (SensorSample Sample, IReadOnlyList<string> Sources, IReadOnlyList<HwInfoReading>? HwInfo) Read(double timeSeconds)
    {
        var sources = new List<string>();
        var sample = new SensorSample { TimeSeconds = timeSeconds };

        var perf = _gpu?.Read().FirstOrDefault();
        if (perf is not null && (perf.TemperatureC ?? perf.FanRpm ?? perf.MemoryClockMHz) is not null)
        {
            sources.Add("D3DKMT");
            sample = sample with { GpuTempC = perf.TemperatureC, GpuFanRpm = perf.FanRpm, GpuMemoryClockMHz = perf.MemoryClockMHz };
        }

        if (_nvml is { Devices.Count: > 0 } nvml)
        {
            var n = nvml.Read(nvml.Devices[0].Handle);
            sources.Add("NVML");
            sample = sample with
            {
                GpuTempC = n.TemperatureC ?? sample.GpuTempC,
                GpuPowerW = n.PowerW,
                GpuClockMHz = n.GraphicsClockMHz,
                GpuMemoryClockMHz = n.MemoryClockMHz ?? sample.GpuMemoryClockMHz,
                GpuUsagePercent = n.UsagePercent,
                GpuLimits = n.Limits,
            };
        }

        var hw = HwInfoReader.TryRead();
        if (hw is { Count: > 0 })
        {
            sources.Add("HWiNFO");
            sample = sample with
            {
                CpuTempC = HwInfoSelectors.CpuTemperature(hw),
                CpuPowerW = HwInfoSelectors.CpuPackagePower(hw),
                CpuCoreVoltage = HwInfoSelectors.CpuCoreVoltage(hw),
                CpuThermalThrottling = HwInfoSelectors.CpuThermalThrottling(hw),
                GpuHotspotC = hw.FirstOrDefault(r => r.Type == HwInfoReadingType.Temperature &&
                                                     r.Label.Equals("GPU Hot Spot Temperature", StringComparison.OrdinalIgnoreCase))?.Value,
            };
        }

        return (sample, sources, hw);
    }

    public async Task<SensorSeries> RunAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var samples = new List<SensorSample>();
        var sources = new HashSet<string>();
        var clock = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (clock.Elapsed < duration && await timer.WaitForNextTickAsync(cancellationToken))
        {
            var (sample, used, _) = Read(clock.Elapsed.TotalSeconds);
            samples.Add(sample);
            sources.UnionWith(used);
        }

        return new SensorSeries { Sources = sources.Order().ToList(), Samples = samples };
    }

    public void Dispose()
    {
        _nvml?.Dispose();
        _gpu?.Dispose();
    }
}
