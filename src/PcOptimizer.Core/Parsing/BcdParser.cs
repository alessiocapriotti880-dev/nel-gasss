using System.Text.RegularExpressions;

namespace PcOptimizer.Core.Parsing;

/// <summary>Output di "bcdedit /enum {current}".</summary>
public static partial class BcdParser
{
    // bcdedit stampa i nomi degli elementi in inglese ma i valori sì/no nella lingua di Windows.
    private static readonly HashSet<string> Yes = new(StringComparer.OrdinalIgnoreCase)
    {
        "yes", "true", "on", "sì", "si", "sí", "ja", "oui", "sim", "tak", "evet", "igen", "ano", "áno", "kyllä", "da", "да", "так", "ναι",
    };

    private static readonly HashSet<string> No = new(StringComparer.OrdinalIgnoreCase)
    {
        "no", "false", "off", "nein", "non", "não", "nao", "nie", "hayır", "nem", "ne", "nej", "nee", "ei", "nu", "нет", "ні", "όχι",
    };

    /// <summary>Elementi di interesse con il loro valore grezzo (chiavi in minuscolo).</summary>
    public static IReadOnlyDictionary<string, string> ParseElements(string output, IEnumerable<string> names)
    {
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n'))
        {
            var m = ElementRegex().Match(line.TrimEnd('\r'));
            if (m.Success && wanted.Contains(m.Groups[1].Value))
            {
                result[m.Groups[1].Value.ToLowerInvariant()] = m.Groups[2].Value.Trim();
            }
        }

        return result;
    }

    /// <summary>Valore booleano localizzato; null se non riconosciuto o assente.</summary>
    public static bool? ToBool(string? value) =>
        value is null ? null : Yes.Contains(value) ? true : No.Contains(value) ? false : null;

    /// <summary>Valore normalizzato per il confronto: "Yes"/"No" per i booleani localizzati, altrimenti il testo così com'è.</summary>
    public static string Normalize(string raw) => ToBool(raw) switch
    {
        true => "Yes",
        false => "No",
        _ => raw.Trim(),
    };

    [GeneratedRegex(@"^\s*([A-Za-z]+)\s+(\S.*)$")]
    private static partial Regex ElementRegex();
}
