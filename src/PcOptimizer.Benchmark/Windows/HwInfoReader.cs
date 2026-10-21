using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using PcOptimizer.Benchmark.Sensors;

namespace PcOptimizer.Benchmark.Windows;

/// <summary>Lettura della memoria condivisa di HWiNFO, se HWiNFO è aperto con "Shared Memory Support" attivo.</summary>
[SupportedOSPlatform("windows")]
public static class HwInfoReader
{
    public static IReadOnlyList<HwInfoReading>? TryRead()
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(HwInfoSharedMemory.MapName, MemoryMappedFileRights.Read);
            var header = new byte[HwInfoSharedMemory.HeaderSize];
            using (var view = map.CreateViewAccessor(0, header.Length, MemoryMappedFileAccess.Read))
            {
                view.ReadArray(0, header, 0, header.Length);
            }

            if (HwInfoSharedMemory.TotalSize(header) is not { } total)
            {
                return null;
            }

            var data = new byte[total];
            using (var view = map.CreateViewAccessor(0, total, MemoryMappedFileAccess.Read))
            {
                view.ReadArray(0, data, 0, data.Length);
            }

            return HwInfoSharedMemory.Parse(data);
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
