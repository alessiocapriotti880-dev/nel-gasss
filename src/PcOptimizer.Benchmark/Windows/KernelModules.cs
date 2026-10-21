using System.Runtime.Versioning;
using PcOptimizer.Benchmark.Latency;

namespace PcOptimizer.Benchmark.Windows;

/// <summary>
/// Elenco dei driver caricati da psapi, usato se la sessione ETW non ha fornito i caricamenti dei moduli.
/// Senza privilegi gli indirizzi possono essere nascosti (tutti zero): in quel caso non aggiunge nulla.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class KernelModules
{
    private const ulong LastModuleSize = 16 * 1024 * 1024;

    public static void AddTo(KernelModuleMap map)
    {
        BenchmarkNative.EnumDeviceDrivers(null, 0, out var needed);
        if (needed <= 0)
        {
            return;
        }

        var bases = new IntPtr[needed / IntPtr.Size];
        if (!BenchmarkNative.EnumDeviceDrivers(bases, bases.Length * IntPtr.Size, out _))
        {
            return;
        }

        var sorted = bases.Where(b => b != IntPtr.Zero).OrderBy(b => (ulong)b).ToArray();
        var name = new char[260];
        for (var i = 0; i < sorted.Length; i++)
        {
            var length = BenchmarkNative.GetDeviceDriverBaseNameW(sorted[i], name, name.Length);
            if (length <= 0)
            {
                continue;
            }

            // psapi non dà la dimensione del modulo: si arriva fino all'inizio del successivo.
            var start = (ulong)sorted[i];
            var size = i + 1 < sorted.Length ? (ulong)sorted[i + 1] - start : LastModuleSize;
            map.Add(start, size, new string(name, 0, length));
        }
    }
}
