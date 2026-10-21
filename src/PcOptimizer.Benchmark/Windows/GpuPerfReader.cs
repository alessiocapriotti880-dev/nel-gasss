using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PcOptimizer.Benchmark.Windows;

/// <summary>Lettura D3DKMT di un adattatore (dati di Gestione attività): valida per GPU di ogni produttore.</summary>
public sealed record GpuPerfReading(string Adapter, double? TemperatureC, double? FanRpm, double? PowerPercent, double? MemoryClockMHz);

/// <summary>
/// Sensori della GPU tramite il kernel grafico di Windows (D3DKMT, KMTQAITYPE_ADAPTERPERFDATA), come Gestione attività.
/// Tiene aperti gli adattatori fino a Dispose.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GpuPerfReader : IDisposable
{
    private const int MaxPath = 260;
    private readonly List<(uint Handle, string Name)> _adapters = [];

    public GpuPerfReader()
    {
        var args = new D3dkmtEnumAdapters2();
        if (BenchmarkNative.D3DKMTEnumAdapters2(ref args) != 0 || args.NumAdapters == 0)
        {
            return;
        }

        var size = Marshal.SizeOf<D3dkmtAdapterInfo>();
        var buffer = Marshal.AllocHGlobal(size * (int)args.NumAdapters);
        try
        {
            args.Adapters = buffer;
            if (BenchmarkNative.D3DKMTEnumAdapters2(ref args) != 0)
            {
                return;
            }

            for (var i = 0; i < args.NumAdapters; i++)
            {
                var info = Marshal.PtrToStructure<D3dkmtAdapterInfo>(buffer + i * size);
                var name = QueryName(info.Adapter);
                if (name is null || name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) || name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
                {
                    Close(info.Adapter);
                    continue;
                }

                _adapters.Add((info.Adapter, name));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public IReadOnlyList<string> AdapterNames => _adapters.Select(a => a.Name).Distinct().ToList();

    /// <summary>
    /// Letture di tutti gli adattatori; la GPU dedicata è quella con la frequenza massima della memoria più alta
    /// (le integrate usano la RAM di sistema e di solito riportano 0).
    /// </summary>
    public IReadOnlyList<GpuPerfReading> Read() =>
        _adapters.Select(a => (a.Name, Data: QueryPerf(a.Handle)))
            .OrderByDescending(x => x.Data?.MaxMemoryFrequency ?? 0)
            .Select(x => new GpuPerfReading(
                x.Name,
                x.Data?.Temperature is > 0 and < 2000 ? x.Data.Value.Temperature / 10.0 : null,
                x.Data?.FanRpm is > 0 and < 20000 ? x.Data.Value.FanRpm : null,
                x.Data?.Power is > 0 and <= 2000 ? x.Data.Value.Power / 10.0 : null,
                x.Data?.MemoryFrequency is > 0 ? x.Data.Value.MemoryFrequency / 1_000_000.0 : null))
            .ToList();

    public void Dispose()
    {
        foreach (var (handle, _) in _adapters)
        {
            Close(handle);
        }

        _adapters.Clear();
    }

    private static D3dkmtAdapterPerfData? QueryPerf(uint adapter)
    {
        var size = Marshal.SizeOf<D3dkmtAdapterPerfData>();
        var data = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(new D3dkmtAdapterPerfData { PhysicalAdapterIndex = 0 }, data, false);
            var query = new D3dkmtQueryAdapterInfo
            {
                Adapter = adapter, Type = BenchmarkNative.KmtQueryAdapterPerfData, PrivateDriverData = data, PrivateDriverDataSize = (uint)size,
            };
            return BenchmarkNative.D3DKMTQueryAdapterInfo(ref query) == 0 ? Marshal.PtrToStructure<D3dkmtAdapterPerfData>(data) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    private static string? QueryName(uint adapter)
    {
        const int size = 4 * MaxPath * sizeof(char);
        var data = Marshal.AllocHGlobal(size);
        try
        {
            var query = new D3dkmtQueryAdapterInfo
            {
                Adapter = adapter, Type = BenchmarkNative.KmtQueryAdapterRegistryInfo, PrivateDriverData = data, PrivateDriverDataSize = size,
            };
            return BenchmarkNative.D3DKMTQueryAdapterInfo(ref query) == 0 ? Marshal.PtrToStringUni(data) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    private static void Close(uint adapter)
    {
        var close = new D3dkmtCloseAdapter { Adapter = adapter };
        BenchmarkNative.D3DKMTCloseAdapter(ref close);
    }
}
