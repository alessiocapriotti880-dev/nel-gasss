using System.Text.Json;
using System.Text.Json.Serialization;
using PcOptimizer.Analysis.Model;

namespace PcOptimizer.Analysis.Catalog;

public sealed record KnownSoftwareEntry
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public SoftwareCategory Category { get; init; }

    public SoftwareRisk Risk { get; init; }

    public bool Overlay { get; init; }

    public bool NeverTouch { get; init; }

    public IReadOnlyList<string> Processes { get; init; } = [];

    public IReadOnlyList<string> Services { get; init; } = [];

    public IReadOnlyList<string> Drivers { get; init; } = [];

    public IReadOnlyList<string> Startup { get; init; } = [];

    public IReadOnlyList<string> Tasks { get; init; } = [];

    public string? Note { get; init; }
}

public sealed record KnownSoftwareFile
{
    public IReadOnlyList<KnownSoftwareEntry> Entries { get; init; } = [];
}

/// <summary>
/// Riconoscimento del software noto (Data/known-software.json) solo tramite nomi:
/// processi, servizi, driver, voci di avvio e attività pianificate.
/// </summary>
public sealed class KnownSoftwareCatalog
{
    public const string FileName = "known-software.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private KnownSoftwareCatalog(IReadOnlyList<KnownSoftwareEntry> entries)
    {
        Entries = entries;
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Name))
            {
                throw new InvalidDataException($"{FileName}: voce senza id o nome.");
            }

            if (entry.Processes.Count + entry.Services.Count + entry.Drivers.Count + entry.Startup.Count + entry.Tasks.Count == 0)
            {
                throw new InvalidDataException($"{FileName}: la voce {entry.Id} non ha criteri di riconoscimento.");
            }
        }

        var duplicate = entries.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"{FileName}: id duplicato \"{duplicate.Key}\".");
        }
    }

    public IReadOnlyList<KnownSoftwareEntry> Entries { get; }

    /// <summary>Carica il catalogo: prima dalle cartelle indicate, altrimenti quello incorporato.</summary>
    public static KnownSoftwareCatalog Load(params string[] overrideDirectories)
    {
        foreach (var dir in overrideDirectories)
        {
            var path = Path.Combine(dir, FileName);
            if (File.Exists(path))
            {
                using var file = File.OpenRead(path);
                return new KnownSoftwareCatalog(Deserialize(file, path));
            }
        }

        using var stream = typeof(KnownSoftwareCatalog).Assembly.GetManifestResourceStream("Data." + FileName)
            ?? throw new InvalidOperationException($"Risorsa incorporata mancante: {FileName}.");
        return new KnownSoftwareCatalog(Deserialize(stream, FileName));
    }

    public KnownSoftwareEntry? MatchProcess(string processName)
    {
        // Process.ProcessName non include ".exe": lo aggiungiamo per confrontare con il catalogo.
        var exe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName : processName + ".exe";
        return Entries.FirstOrDefault(e => e.Processes.Any(p => NameMatches(p, exe)));
    }

    public KnownSoftwareEntry? MatchService(string serviceName) =>
        Entries.FirstOrDefault(e => e.Services.Any(p => NameMatches(p, serviceName)));

    public KnownSoftwareEntry? MatchDriver(string serviceName, string? fileName) =>
        Entries.FirstOrDefault(e =>
            e.Drivers.Any(p => NameMatches(p, fileName ?? "") || NameMatches(p, serviceName)) ||
            e.Services.Any(p => NameMatches(p, serviceName)));

    /// <summary>Voce di avvio: vince il criterio più lungo ("Razer Cortex" prima di "Razer").</summary>
    public KnownSoftwareEntry? MatchStartup(string name, string? command) =>
        BestContains(e => e.Startup, name, command);

    public KnownSoftwareEntry? MatchTask(string path, string? executable) =>
        BestContains(e => e.Tasks, path, executable);

    private KnownSoftwareEntry? BestContains(Func<KnownSoftwareEntry, IReadOnlyList<string>> patterns, string text, string? extra) =>
        Entries
            .SelectMany(e => patterns(e).Select(p => (Entry: e, Pattern: p)))
            .Where(x => text.Contains(x.Pattern, StringComparison.OrdinalIgnoreCase) ||
                        (extra?.Contains(x.Pattern, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(x => x.Pattern.Length)
            .Select(x => x.Entry)
            .FirstOrDefault();

    /// <summary>Confronto esatto senza distinzione maiuscole; un '*' finale indica un prefisso.</summary>
    internal static bool NameMatches(string pattern, string name) =>
        pattern.EndsWith('*')
            ? name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(pattern, name, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<KnownSoftwareEntry> Deserialize(Stream stream, string source)
    {
        try
        {
            return (JsonSerializer.Deserialize<KnownSoftwareFile>(stream, JsonOptions)
                    ?? throw new InvalidDataException($"{source}: file vuoto.")).Entries;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source}: JSON non valido ({ex.Message}).", ex);
        }
    }
}
