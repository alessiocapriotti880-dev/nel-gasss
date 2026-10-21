using System.Globalization;
using System.Management;
using System.Runtime.Versioning;

namespace PcOptimizer.Hardware.Windows;

/// <summary>Query WMI in sola lettura; ogni riga diventa un dizionario per non trascinare oggetti COM.</summary>
[SupportedOSPlatform("windows")]
internal static class Wmi
{
    public const string Cimv2 = @"root\cimv2";
    public const string Storage = @"root\Microsoft\Windows\Storage";
    public const string StandardCimv2 = @"root\StandardCimv2";

    public static List<Dictionary<string, object?>> Query(string scope, string query)
    {
        var rows = new List<Dictionary<string, object?>>();
        using var searcher = new ManagementObjectSearcher(scope, query);
        using var results = searcher.Get();
        foreach (var item in results)
        {
            using (item)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in item.Properties)
                {
                    row[property.Name] = property.Value;
                }

                rows.Add(row);
            }
        }

        return rows;
    }

    public static string? Str(this IReadOnlyDictionary<string, object?> row, string name) =>
        row.TryGetValue(name, out var value) && value is not null
            ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim()
            : null;

    public static long? Long(this IReadOnlyDictionary<string, object?> row, string name)
    {
        if (!row.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is OverflowException or FormatException or InvalidCastException)
        {
            return null;
        }
    }

    public static int? Int(this IReadOnlyDictionary<string, object?> row, string name) =>
        row.Long(name) is { } v and >= int.MinValue and <= int.MaxValue ? (int)v : null;

    /// <summary>Data CIM ("20240509000000.000000-000") convertita in DateTime.</summary>
    public static DateTime? CimDate(this IReadOnlyDictionary<string, object?> row, string name)
    {
        var text = row.Str(name);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(text);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }
    }
}
