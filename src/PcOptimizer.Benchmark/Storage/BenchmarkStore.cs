using System.Text.Json;
using System.Text.Json.Serialization;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Core;

namespace PcOptimizer.Benchmark.Storage;

/// <summary>Riepilogo di una sessione salvata, per gli elenchi senza caricare tutti i dati.</summary>
public sealed record BenchmarkEntry(Guid Id, DateTimeOffset StartedAt, string Label, BenchmarkScenario Scenario, int? Score, string Path);

/// <summary>Archivio delle sessioni di misura: un file JSON per sessione in %ProgramData%\PcOptimizerPro\benchmarks.</summary>
public sealed class BenchmarkStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly string _directory;

    public BenchmarkStore(AppPaths paths) => _directory = paths.Benchmarks;

    public string Save(BenchmarkRun run)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{run.StartedAt.UtcDateTime:yyyyMMdd-HHmmss}_{Slug(run.Label)}_{run.Id.ToString("N")[..8]}.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(run, JsonOptions));
        File.Move(temp, path, overwrite: true);
        return path;
    }

    public static BenchmarkRun Load(string path) =>
        JsonSerializer.Deserialize<BenchmarkRun>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"{path}: file vuoto.");

    public BenchmarkRun? Find(Guid id) =>
        List().FirstOrDefault(e => e.Id == id) is { } entry ? Load(entry.Path) : null;

    /// <summary>Sessioni salvate, dalla più recente. I file illeggibili vengono saltati.</summary>
    public IReadOnlyList<BenchmarkEntry> List()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var entries = new List<BenchmarkEntry>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var run = Load(file);
                entries.Add(new BenchmarkEntry(run.Id, run.StartedAt, run.Label, run.Scenario, run.Score?.Score, file));
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // File danneggiato o di una versione futura: non blocca l'elenco.
            }
        }

        return entries.OrderByDescending(e => e.StartedAt).ToList();
    }

    internal static string Slug(string label)
    {
        var chars = label.Trim().ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "misura" : slug[..Math.Min(slug.Length, 40)];
    }
}
