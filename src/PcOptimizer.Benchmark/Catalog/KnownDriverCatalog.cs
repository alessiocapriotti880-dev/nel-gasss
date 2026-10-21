using System.Text.Json;

namespace PcOptimizer.Benchmark.Catalog;

public sealed record KnownDriverEntry
{
    public IReadOnlyList<string> Patterns { get; init; } = [];

    public string Name { get; init; } = "";

    /// <summary>Gpu, Audio, Network, Storage, Usb, Platform, Security, Software.</summary>
    public string Category { get; init; } = "";

    public string Hint { get; init; } = "";
}

public sealed record KnownDriverFile
{
    public IReadOnlyList<KnownDriverEntry> Entries { get; init; } = [];
}

/// <summary>
/// Nomi leggibili e consigli per i driver che compaiono fra i DPC/ISR lunghi (Data/known-drivers.json).
/// Una copia aggiornata nella cartella dati ha la precedenza su quella incorporata.
/// </summary>
public sealed class KnownDriverCatalog
{
    public const string FileName = "known-drivers.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal KnownDriverCatalog(IReadOnlyList<KnownDriverEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || entry.Patterns.Count == 0)
            {
                throw new InvalidDataException($"{FileName}: voce senza nome o senza criteri.");
            }
        }

        Entries = entries;
    }

    public IReadOnlyList<KnownDriverEntry> Entries { get; }

    public static KnownDriverCatalog Load(params string[] overrideDirectories)
    {
        foreach (var dir in overrideDirectories)
        {
            var path = Path.Combine(dir, FileName);
            if (File.Exists(path))
            {
                using var file = File.OpenRead(path);
                return new KnownDriverCatalog(Deserialize(file, path));
            }
        }

        using var stream = typeof(KnownDriverCatalog).Assembly.GetManifestResourceStream("Data." + FileName)
            ?? throw new InvalidOperationException($"Risorsa incorporata mancante: {FileName}.");
        return new KnownDriverCatalog(Deserialize(stream, FileName));
    }

    /// <summary>Voce per il file del driver; a parità vince il criterio più lungo (più specifico).</summary>
    public KnownDriverEntry? Find(string module) =>
        Entries
            .SelectMany(e => e.Patterns.Select(p => (Entry: e, Pattern: p)))
            .Where(x => Matches(x.Pattern, module))
            .OrderByDescending(x => x.Pattern.Length)
            .Select(x => x.Entry)
            .FirstOrDefault();

    /// <summary>Confronto senza maiuscole; '*' (anche in mezzo, es. "netwtw*.sys") vale qualsiasi sequenza.</summary>
    internal static bool Matches(string pattern, string name)
    {
        var star = pattern.IndexOf('*');
        if (star < 0)
        {
            return string.Equals(pattern, name, StringComparison.OrdinalIgnoreCase);
        }

        var prefix = pattern[..star];
        var suffix = pattern[(star + 1)..];
        return name.Length >= prefix.Length + suffix.Length &&
               name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<KnownDriverEntry> Deserialize(Stream stream, string source)
    {
        try
        {
            return (JsonSerializer.Deserialize<KnownDriverFile>(stream, JsonOptions)
                    ?? throw new InvalidDataException($"{source}: file vuoto.")).Entries;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source}: JSON non valido ({ex.Message}).", ex);
        }
    }
}
