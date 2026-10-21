using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using PcOptimizer.Benchmark.Boot;
using PcOptimizer.Benchmark.Model;

namespace PcOptimizer.Benchmark.Windows;

/// <summary>Tempi di avvio registrati da Windows (evento 100 di Diagnostics-Performance). Richiede amministratore.</summary>
[SupportedOSPlatform("windows")]
public static class BootTimeReader
{
    public const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";

    public static BootResult Read(int max = 10)
    {
        var boots = new List<BootRecord>();
        var query = new EventLogQuery(LogName, PathType.LogName, "*[System[(EventID=100)]]") { ReverseDirection = true };
        using var reader = new EventLogReader(query);
        while (boots.Count < max && reader.ReadEvent() is { } record)
        {
            using (record)
            {
                if (BootEventParser.Parse(record.ToXml()) is { } boot)
                {
                    boots.Add(boot);
                }
            }
        }

        return new BootResult { Boots = boots.OrderByDescending(b => b.Time).ToList() };
    }
}
