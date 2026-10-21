using System.Buffers.Binary;
using System.Text;
using PcOptimizer.Benchmark.Model;

namespace PcOptimizer.Benchmark.Cpu;

/// <summary>Tempi di sistema (GetSystemTimes) in unità da 100 ns. Il tempo kernel include quello idle.</summary>
public readonly record struct SystemTimes(long Idle, long Kernel, long User);

/// <summary>Tempo CPU cumulativo di un processo letto dall'elenco di sistema.</summary>
/// <param name="CreateTime">Distingue un processo nuovo che riusa lo stesso PID.</param>
public readonly record struct ProcessCpuTimes(int ProcessId, long CreateTime, string Name, long CpuTime100ns);

public static class CpuMath
{
    /// <summary>Percentuale di CPU occupata fra due letture di GetSystemTimes.</summary>
    public static double BusyPercent(SystemTimes before, SystemTimes after)
    {
        var idle = after.Idle - before.Idle;
        var total = after.Kernel - before.Kernel + (after.User - before.User);
        return total <= 0 ? 0 : Math.Clamp(100.0 * (total - idle) / total, 0, 100);
    }

    /// <summary>
    /// Processi che hanno usato più CPU fra due istantanee, in % della capacità totale.
    /// Esclude il processo inattivo (PID 0).
    /// </summary>
    public static IReadOnlyList<ProcessActivity> TopProcesses(
        IReadOnlyList<ProcessCpuTimes> before, IReadOnlyList<ProcessCpuTimes> after,
        double elapsedSeconds, int logicalProcessors, int count = 10)
    {
        var capacity = elapsedSeconds * 10_000_000 * Math.Max(1, logicalProcessors);
        if (capacity <= 0)
        {
            return [];
        }

        var start = before.GroupBy(p => (p.ProcessId, p.CreateTime)).ToDictionary(g => g.Key, g => g.First().CpuTime100ns);
        return after
            .Where(p => p.ProcessId != 0)
            .Select(p => new ProcessActivity(
                p.Name, p.ProcessId,
                // Processo nato durante la misura: tutto il suo tempo CPU è nell'intervallo.
                100.0 * (p.CpuTime100ns - start.GetValueOrDefault((p.ProcessId, p.CreateTime), 0)) / capacity))
            .Where(p => p.CpuPercent > 0)
            .OrderByDescending(p => p.CpuPercent)
            .Take(count)
            .ToList();
    }
}

/// <summary>
/// Decodifica del buffer di NtQuerySystemInformation(SystemProcessInformation) su x64: lo stesso elenco usato da
/// Gestione attività, ottenuto senza aprire handle sui processi (quindi senza toccare giochi o anti-cheat).
/// </summary>
public static class SystemProcessInfoParser
{
    // Offset di SYSTEM_PROCESS_INFORMATION (x64).
    private const int NextEntryOffset = 0;
    private const int CreateTimeOffset = 32;
    private const int UserTimeOffset = 40;
    private const int KernelTimeOffset = 48;
    private const int ImageNameLengthOffset = 56;
    private const int ImageNameBufferOffset = 64;
    private const int ProcessIdOffset = 80;
    internal const int MinEntrySize = 88;

    /// <param name="baseAddress">Indirizzo in memoria del buffer originale: i nomi sono puntatori assoluti al suo interno.</param>
    public static IReadOnlyList<ProcessCpuTimes> Parse(ReadOnlySpan<byte> buffer, long baseAddress)
    {
        var result = new List<ProcessCpuTimes>();
        var offset = 0;
        while (offset >= 0 && offset + MinEntrySize <= buffer.Length)
        {
            var entry = buffer[offset..];
            var pid = (int)BinaryPrimitives.ReadInt64LittleEndian(entry[ProcessIdOffset..]);
            var cpu = BinaryPrimitives.ReadInt64LittleEndian(entry[UserTimeOffset..]) +
                      BinaryPrimitives.ReadInt64LittleEndian(entry[KernelTimeOffset..]);
            var name = pid == 0 ? "Idle" : ReadName(buffer, entry, baseAddress) ?? (pid == 4 ? "System" : $"PID {pid}");
            result.Add(new ProcessCpuTimes(pid, BinaryPrimitives.ReadInt64LittleEndian(entry[CreateTimeOffset..]), name, cpu));

            var next = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[NextEntryOffset..]);
            if (next == 0)
            {
                break;
            }

            offset += next;
        }

        return result;
    }

    private static string? ReadName(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> entry, long baseAddress)
    {
        var length = BinaryPrimitives.ReadUInt16LittleEndian(entry[ImageNameLengthOffset..]);
        var pointer = BinaryPrimitives.ReadInt64LittleEndian(entry[ImageNameBufferOffset..]);
        var start = pointer - baseAddress;
        if (length == 0 || pointer == 0 || start < 0 || start + length > buffer.Length)
        {
            return null;
        }

        return Encoding.Unicode.GetString(buffer.Slice((int)start, length));
    }
}
