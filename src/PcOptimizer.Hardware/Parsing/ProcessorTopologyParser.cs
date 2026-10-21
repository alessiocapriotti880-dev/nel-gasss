using System.Buffers.Binary;
using System.Numerics;

namespace PcOptimizer.Hardware.Parsing;

/// <summary>Topologia dei core ricavata da GetLogicalProcessorInformationEx.</summary>
public sealed record ProcessorTopology(
    int Packages,
    int PhysicalCores,
    int LogicalProcessors,
    int PerformanceCores,
    int EfficiencyCores,
    bool HasSmt,
    long? L3CacheBytes);

/// <summary>
/// Interpreta il buffer restituito da GetLogicalProcessorInformationEx(RelationAll).
/// <para>
/// Ogni voce SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX ha: Relationship (DWORD, offset 0),
/// Size (DWORD, offset 4) e i dati dall'offset 8. Per i core (Relationship = 0) i dati sono
/// PROCESSOR_RELATIONSHIP: Flags (byte, bit 0 = SMT), EfficiencyClass (byte),
/// Reserved[20], GroupCount (WORD, offset +22) e GroupMask[] (GROUP_AFFINITY da 16 byte,
/// offset +24) con le maschere dei processori logici.
/// </para>
/// <para>
/// Sulle CPU ibride Windows assegna EfficiencyClass più alta ai core P: è lo stesso dato
/// usato dallo scheduler, quindi più affidabile di qualsiasi tabella per modello.
/// </para>
/// </summary>
public static class ProcessorTopologyParser
{
    private const int RelationProcessorCore = 0;
    private const int RelationCache = 2;
    private const int RelationProcessorPackage = 3;
    private const int HeaderSize = 8;
    private const int GroupAffinitySize = 16;

    public static ProcessorTopology Parse(ReadOnlySpan<byte> buffer)
    {
        var cores = new List<(byte EfficiencyClass, bool Smt, int Threads)>();
        var packages = 0;
        long l3 = 0;

        var offset = 0;
        while (offset + HeaderSize <= buffer.Length)
        {
            var relationship = BinaryPrimitives.ReadInt32LittleEndian(buffer[offset..]);
            var size = BinaryPrimitives.ReadInt32LittleEndian(buffer[(offset + 4)..]);
            if (size < HeaderSize || offset + size > buffer.Length)
            {
                break; // buffer troncato o corrotto: ci fermiamo a quanto letto finora
            }

            var data = buffer.Slice(offset + HeaderSize, size - HeaderSize);
            switch (relationship)
            {
                case RelationProcessorCore when data.Length >= 24:
                    var flags = data[0];
                    var efficiencyClass = data[1];
                    var groupCount = BinaryPrimitives.ReadUInt16LittleEndian(data[22..]);
                    var threads = 0;
                    for (var g = 0; g < groupCount && 24 + (g + 1) * GroupAffinitySize <= data.Length; g++)
                    {
                        var mask = BinaryPrimitives.ReadUInt64LittleEndian(data[(24 + g * GroupAffinitySize)..]);
                        threads += BitOperations.PopCount(mask);
                    }

                    cores.Add((efficiencyClass, (flags & 1) != 0, Math.Max(threads, 1)));
                    break;

                // CACHE_RELATIONSHIP: Level (byte, +0), CacheSize (DWORD, +4), Type (DWORD, +8; 0 = unificata).
                case RelationCache when data.Length >= 12:
                    if (data[0] == 3 && BinaryPrimitives.ReadInt32LittleEndian(data[8..]) == 0)
                    {
                        l3 += BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
                    }

                    break;

                case RelationProcessorPackage:
                    packages++;
                    break;
            }

            offset += size;
        }

        var isHybrid = cores.Select(c => c.EfficiencyClass).Distinct().Count() > 1;
        var maxClass = cores.Count > 0 ? cores.Max(c => c.EfficiencyClass) : (byte)0;
        var performance = isHybrid ? cores.Count(c => c.EfficiencyClass == maxClass) : cores.Count;

        return new ProcessorTopology(
            Packages: Math.Max(packages, 1),
            PhysicalCores: cores.Count,
            LogicalProcessors: cores.Sum(c => c.Threads),
            PerformanceCores: performance,
            EfficiencyCores: cores.Count - performance,
            HasSmt: cores.Any(c => c.Smt),
            L3CacheBytes: l3 > 0 ? l3 : null);
    }
}
