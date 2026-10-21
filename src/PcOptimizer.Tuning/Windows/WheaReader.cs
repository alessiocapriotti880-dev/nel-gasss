using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.Versioning;

namespace PcOptimizer.Tuning.Windows;

/// <summary>
/// Errori hardware registrati da Windows (sorgente WHEA-Logger nel registro Sistema): errori corretti della CPU (19),
/// della cache/memoria (47), del bus PCIe (17) e fatali (18). Con undervolt e overclock compaiono prima dei crash.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WheaReader
{
    public static int CountSince(DateTimeOffset since)
    {
        var time = since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var query = new EventLogQuery("System", PathType.LogName,
            $"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[@SystemTime>='{time}']]]");
        try
        {
            using var reader = new EventLogReader(query);
            var count = 0;
            while (reader.ReadEvent() is { } record)
            {
                record.Dispose();
                count++;
            }

            return count;
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
