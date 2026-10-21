using System.Globalization;
using System.Xml.Linq;
using PcOptimizer.Benchmark.Model;

namespace PcOptimizer.Benchmark.Boot;

/// <summary>
/// Lettura dell'evento 100 del registro "Microsoft-Windows-Diagnostics-Performance/Operational", che Windows scrive
/// dopo ogni avvio misurato: BootTime (fino al desktop pronto), MainPathBootTime (fino al desktop) e
/// BootPostBootTime (attività di avvio dopo il desktop: programmi in esecuzione automatica, servizi ritardati).
/// </summary>
public static class BootEventParser
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    public static BootRecord? Parse(string eventXml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(eventXml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var data = doc.Descendants(Ns + "Data")
            .Where(d => d.Attribute("Name") is not null)
            .GroupBy(d => d.Attribute("Name")!.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
        var created = doc.Descendants(Ns + "TimeCreated").FirstOrDefault()?.Attribute("SystemTime")?.Value;

        if (!TryNumber(data, "BootTime", out var boot) || boot <= 0)
        {
            return null;
        }

        TryNumber(data, "MainPathBootTime", out var main);
        TryNumber(data, "BootPostBootTime", out var post);
        var time = DateTimeOffset.TryParse(
            data.GetValueOrDefault("BootStartTime") ?? created, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var t) ? t : DateTimeOffset.MinValue;
        var afterUpdate = data.TryGetValue("BootIsRebootAfterInstall", out var flag) &&
                          (flag.Equals("true", StringComparison.OrdinalIgnoreCase) || flag == "1");
        return new BootRecord(time, boot, main, post, afterUpdate);
    }

    private static bool TryNumber(Dictionary<string, string> data, string name, out double value)
    {
        value = 0;
        return data.TryGetValue(name, out var text) &&
               double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
