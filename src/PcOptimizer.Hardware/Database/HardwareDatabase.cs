using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PcOptimizer.Hardware.Model;

namespace PcOptimizer.Hardware.Database;

/// <summary>
/// Tabelle di riferimento per identificare i componenti (Data/*.json).
/// <para>
/// I file sono incorporati nell'assembly; una copia aggiornata posta in una delle cartelle
/// passate a <see cref="Load"/> ha la precedenza, così le tabelle si aggiornano senza
/// ricompilare. Al caricamento ogni voce viene validata (regex, esadecimali): un file
/// sbagliato genera subito un errore chiaro invece di classificazioni silenziosamente errate.
/// </para>
/// </summary>
public sealed class HardwareDatabase
{
    public const string CpuFileName = "cpu-architectures.json";
    public const string GpuFileName = "gpus.json";
    public const string MemoryFileName = "memory-part-numbers.json";
    public const string ChipsetFileName = "chipsets.json";
    public const string ScoringFileName = "tier-scoring.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // Parole che non aiutano a distinguere i modelli di GPU.
    private static readonly HashSet<string> GpuNoiseWords = ["NVIDIA", "GEFORCE", "AMD", "ATI", "INTEL", "CORPORATION"];

    private readonly List<CompiledCpuArchitecture> _cpuArchitectures;
    private readonly List<(GpuEntry Entry, string[] Tokens)> _gpus;
    private readonly List<(MemoryPartPattern Pattern, Regex Regex)> _memoryPatterns;

    private HardwareDatabase(
        CpuDatabaseFile cpu, GpuDatabaseFile gpu, MemoryDatabaseFile memory,
        ChipsetDatabaseFile chipsets, TierScoringConfig scoring)
    {
        Cpu = cpu;
        Gpu = gpu;
        Memory = memory;
        Chipsets = chipsets;
        Scoring = scoring;

        _cpuArchitectures = cpu.Architectures.Select(Compile).ToList();
        _gpus = gpu.Gpus.Select(g => (g, NormalizeGpuName(g.Match))).ToList();
        _memoryPatterns = memory.Patterns.Select(p => (p, CompileRegex(MemoryFileName, p.Regex))).ToList();

        foreach (var (entry, tokens) in _gpus.Where(g => g.Tokens.Length == 0))
        {
            throw new InvalidDataException($"{GpuFileName}: voce \"{entry.Name}\" senza campo match valido.");
        }

        foreach (var advisory in cpu.Architectures.SelectMany(a => a.Advisories).Where(a => a.MinimumMicrocode is not null))
        {
            ParseHex(CpuFileName, advisory.MinimumMicrocode!);
        }
    }

    public CpuDatabaseFile Cpu { get; }

    public GpuDatabaseFile Gpu { get; }

    public MemoryDatabaseFile Memory { get; }

    public ChipsetDatabaseFile Chipsets { get; }

    public TierScoringConfig Scoring { get; }

    /// <summary>Carica le tabelle: prima dalle cartelle indicate (se il file esiste), altrimenti quelle incorporate.</summary>
    public static HardwareDatabase Load(params string[] overrideDirectories) => new(
        Read<CpuDatabaseFile>(CpuFileName, overrideDirectories),
        Read<GpuDatabaseFile>(GpuFileName, overrideDirectories),
        Read<MemoryDatabaseFile>(MemoryFileName, overrideDirectories),
        Read<ChipsetDatabaseFile>(ChipsetFileName, overrideDirectories),
        Read<TierScoringConfig>(ScoringFileName, overrideDirectories));

    /// <summary>
    /// Architettura CPU per famiglia/modello/stepping. Se più voci corrispondono vince la più
    /// specifica: con filtro di stepping, poi modello esatto, poi intervallo di modelli.
    /// </summary>
    public CpuArchitectureEntry? FindCpuArchitecture(CpuVendor vendor, int family, int model, int stepping)
    {
        CpuArchitectureEntry? best = null;
        var bestScore = 0;
        foreach (var arch in _cpuArchitectures.Where(a => a.Entry.Vendor == vendor && a.Family == family))
        {
            if (arch.Entry.Steppings is { } steppings && !steppings.Contains(stepping))
            {
                continue;
            }

            foreach (var (from, to) in arch.Models.Where(r => model >= r.From && model <= r.To))
            {
                var score = (from == to ? 2 : 1) + (arch.Entry.Steppings is null ? 0 : 2);
                if (score > bestScore)
                {
                    (best, bestScore) = (arch.Entry, score);
                }
            }
        }

        return best;
    }

    public CpuBrandModifier? FindBrandModifier(string? brand) =>
        brand is null ? null : Cpu.BrandModifiers.FirstOrDefault(m => brand.Contains(m.Contains, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Modello di GPU dal nome. Il nome viene normalizzato in parole (senza marchi e simboli)
    /// e vince la voce con la sequenza di parole più lunga contenuta nel nome:
    /// "RTX 4070 Ti SUPER" sceglie "RTX 4070 TI SUPER" e non "RTX 4070 TI" o "RTX 4070".
    /// </summary>
    public (GpuEntry Entry, GpuModifier? Modifier)? FindGpu(GpuVendor vendor, string name)
    {
        var tokens = NormalizeGpuName(name);
        var match = _gpus
            .Where(g => vendor is GpuVendor.Unknown or GpuVendor.Other || g.Entry.Vendor == vendor)
            .Where(g => ContainsSequence(tokens, g.Tokens))
            .OrderByDescending(g => g.Tokens.Length)
            .Select(g => g.Entry)
            .FirstOrDefault();
        if (match is null)
        {
            return null;
        }

        var modifier = Gpu.Modifiers.FirstOrDefault(m => tokens.Contains(m.Token.ToUpperInvariant()));
        return (match, modifier);
    }

    /// <summary>Stima grossolana per GPU sconosciute in base alla VRAM dedicata.</summary>
    public double? EstimateGpuIndexFromVram(long? dedicatedBytes)
    {
        if (dedicatedBytes is null or <= 0)
        {
            return null;
        }

        var gb = dedicatedBytes.Value / (1024.0 * 1024 * 1024);
        return Gpu.VramFallback.OrderByDescending(f => f.MinGB).FirstOrDefault(f => gb >= f.MinGB)?.Index;
    }

    /// <summary>Velocità e latenza dichiarate dal codice prodotto del modulo RAM.</summary>
    public ModulePartInfo? ParseMemoryPartNumber(string? partNumber)
    {
        if (string.IsNullOrWhiteSpace(partNumber))
        {
            return null;
        }

        var normalized = partNumber.Trim().ToUpperInvariant();
        foreach (var (pattern, regex) in _memoryPatterns)
        {
            var m = regex.Match(normalized);
            if (!m.Success)
            {
                continue;
            }

            int? speed = null;
            if (m.Groups["code"].Success && pattern.Codes is not null)
            {
                speed = pattern.Codes.TryGetValue(m.Groups["code"].Value, out var coded) ? coded : null;
            }
            else if (m.Groups["speed"].Success)
            {
                var raw = (int)Math.Round(int.Parse(m.Groups["speed"].Value, CultureInfo.InvariantCulture) * pattern.SpeedScale);
                speed = pattern.SnapToStandard ? SnapToStandardSpeed(raw) : raw;
            }

            if (speed is null)
            {
                continue;
            }

            int? cl = m.Groups["cl"].Success ? int.Parse(m.Groups["cl"].Value, CultureInfo.InvariantCulture) : null;
            return new ModulePartInfo(pattern.Brand, speed.Value, cl, pattern.Jedec);
        }

        return null;
    }

    public ChipsetEntry? FindChipset(CpuVendor vendor, string? chipset) =>
        chipset is null
            ? null
            : Chipsets.Chipsets.FirstOrDefault(c =>
                (vendor == CpuVendor.Unknown || c.CpuVendor == vendor) &&
                c.Names.Contains(chipset, StringComparer.OrdinalIgnoreCase));

    internal static string[] NormalizeGpuName(string name)
    {
        var text = name.ToUpperInvariant();
        foreach (var symbol in new[] { "(R)", "(TM)", "(C)", "®", "™", "©" })
        {
            text = text.Replace(symbol, " ");
        }

        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !GpuNoiseWords.Contains(t))
            .ToArray();
    }

    internal static int ParseHex(string file, string text)
    {
        var digits = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        return int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException($"{file}: valore esadecimale non valido \"{text}\".");
    }

    private int SnapToStandardSpeed(int raw)
    {
        var nearest = Memory.StandardSpeeds.OrderBy(s => Math.Abs(s - raw)).FirstOrDefault();
        return nearest != 0 && Math.Abs(nearest - raw) <= raw * 0.03 ? nearest : raw;
    }

    private static bool ContainsSequence(string[] tokens, string[] sequence)
    {
        for (var i = 0; i + sequence.Length <= tokens.Length; i++)
        {
            if (tokens.AsSpan(i, sequence.Length).SequenceEqual(sequence))
            {
                return true;
            }
        }

        return false;
    }

    private static CompiledCpuArchitecture Compile(CpuArchitectureEntry entry)
    {
        var models = entry.Models.Select(m =>
        {
            var parts = m.Split('-', 2, StringSplitOptions.TrimEntries);
            var from = ParseHex(CpuFileName, parts[0]);
            var to = parts.Length == 2 ? ParseHex(CpuFileName, parts[1]) : from;
            return to >= from ? (from, to) : throw new InvalidDataException($"{CpuFileName}: intervallo non valido \"{m}\" in {entry.Id}.");
        }).ToList();

        if (models.Count == 0 || entry.Ipc <= 0 || entry.TypicalBoostMHz <= 0)
        {
            throw new InvalidDataException($"{CpuFileName}: voce {entry.Id} incompleta (models, ipc, typicalBoostMHz).");
        }

        return new CompiledCpuArchitecture(entry, ParseHex(CpuFileName, entry.Family), models);
    }

    private static Regex CompileRegex(string file, string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"{file}: espressione regolare non valida \"{pattern}\": {ex.Message}", ex);
        }
    }

    private static T Read<T>(string fileName, string[] overrideDirectories)
    {
        foreach (var dir in overrideDirectories)
        {
            var path = Path.Combine(dir, fileName);
            if (File.Exists(path))
            {
                using var file = File.OpenRead(path);
                return Deserialize<T>(file, path);
            }
        }

        using var stream = typeof(HardwareDatabase).Assembly.GetManifestResourceStream("Data." + fileName)
            ?? throw new InvalidOperationException($"Risorsa incorporata mancante: {fileName}.");
        return Deserialize<T>(stream, fileName);
    }

    private static T Deserialize<T>(Stream stream, string source)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(stream, JsonOptions)
                ?? throw new InvalidDataException($"{source}: file vuoto.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source}: JSON non valido ({ex.Message}).", ex);
        }
    }

    private sealed record CompiledCpuArchitecture(CpuArchitectureEntry Entry, int Family, List<(int From, int To)> Models);
}
