using System.Text.RegularExpressions;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Analysis.Parsing;

/// <summary>Percorso del file eseguibile da un comando, un ImagePath di servizio o di driver.</summary>
public static class ImagePathParser
{
    private static readonly string[] Extensions = [".exe", ".sys", ".dll"];

    /// <param name="systemRoot">Cartella di Windows (es. "C:\Windows"), per i percorsi relativi dei driver.</param>
    public static string? ExecutablePath(string? command, string systemRoot)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = command.Trim();
        string path;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            path = end > 1 ? text[1..end] : text.Trim('"');
        }
        else
        {
            // Senza virgolette il percorso può contenere spazi: si taglia dopo la prima estensione eseguibile.
            var end = Extensions
                .Select(ext => (Index: text.IndexOf(ext, StringComparison.OrdinalIgnoreCase), ext.Length))
                .Where(x => x.Index > 0)
                .Select(x => x.Index + x.Length)
                .DefaultIfEmpty(-1)
                .Min();
            path = end > 0 ? text[..end] : text.Split(' ', 2)[0];
        }

        return ExpandSystemPaths(path, systemRoot);
    }

    internal static string ExpandSystemPaths(string path, string systemRoot)
    {
        if (path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            path = path[4..];
        }

        foreach (var prefix in new[] { @"\SystemRoot", "%SystemRoot%", "%windir%" })
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return systemRoot + path[prefix.Length..];
            }
        }

        // I driver spesso hanno percorsi relativi alla cartella di Windows: "System32\drivers\x.sys".
        return path.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(@"SysWOW64\", StringComparison.OrdinalIgnoreCase)
            ? systemRoot + @"\" + path
            : path;
    }
}

/// <summary>Stato delle voci di avvio come in Gestione attività.</summary>
public static class StartupStateParser
{
    /// <summary>
    /// Valore binario in Explorer\StartupApproved: primo byte pari (02, 06) = abilitato,
    /// dispari (03, 07) = disabilitato dall'utente. Valore assente = abilitato.
    /// </summary>
    public static bool IsApprovedEnabled(byte[]? data) => data is not { Length: > 0 } || (data[0] & 1) == 0;

    /// <summary>StartupTaskState delle app dello Store: 2 = attiva, 4 = attiva per criterio di gruppo.</summary>
    public static bool IsStoreTaskEnabled(int? state) => state is 2 or 4;
}

/// <summary>Risorse IRQ da Win32_PnPAllocatedResource.</summary>
public static partial class IrqParser
{
    /// <summary>Gli IRQ dal 2^31 in su (valori negativi a 32 bit) sono interrupt MSI.</summary>
    public const long MsiThreshold = 0x80000000L;

    /// <summary>
    /// Interpreta un'associazione risorsa-dispositivo, es.
    /// Antecedent = "\\PC\root\cimv2:Win32_IRQResource.IRQNumber=4294967286",
    /// Dependent = "\\PC\root\cimv2:Win32_PnPEntity.DeviceID=\"PCI\\VEN_10DE&amp;...\\4&amp;2B6B...\"".
    /// </summary>
    public static (long Irq, string DeviceId)? ParseAllocation(string? antecedent, string? dependent)
    {
        if (antecedent is null || dependent is null)
        {
            return null;
        }

        var irq = IrqRegex().Match(antecedent);
        var device = DeviceRegex().Match(dependent);
        if (!irq.Success || !device.Success)
        {
            return null;
        }

        // Nei riferimenti WMI le barre rovesciate sono raddoppiate.
        return (long.Parse(irq.Groups[1].Value), device.Groups[1].Value.Replace(@"\\", @"\"));
    }

    public static InterruptMode Mode(IReadOnlyList<long> irqs) =>
        irqs.Count == 0 ? InterruptMode.Unknown
        : irqs.Any(i => i >= MsiThreshold) ? InterruptMode.Msi
        : InterruptMode.LineBased;

    [GeneratedRegex(@"Win32_IRQResource\.IRQNumber=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex IrqRegex();

    [GeneratedRegex(@"Win32_PnPEntity\.DeviceID=""(.+)""", RegexOptions.IgnoreCase)]
    private static partial Regex DeviceRegex();
}

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

    [GeneratedRegex(@"^\s*([A-Za-z]+)\s+(\S.*)$")]
    private static partial Regex ElementRegex();
}

/// <summary>Stringhe "Chiave=valore;Chiave=valore;" usate da DirectX per le preferenze grafiche.</summary>
public static class DirectXSettingsParser
{
    public static IReadOnlyDictionary<string, string> Parse(string? value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Length > 0)
            {
                result[parts[0]] = parts[1];
            }
        }

        return result;
    }

    public static bool? Flag(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var v) ? v == "1" : null;
}

public static class DeviceClassParser
{
    private static readonly Guid Display = new("4d36e968-e325-11ce-bfc1-08002be10318");
    private static readonly Guid Net = new("4d36e972-e325-11ce-bfc1-08002be10318");
    private static readonly Guid Media = new("4d36e96c-e325-11ce-bfc1-08002be10318");
    private static readonly Guid Usb = new("36fc9e60-c465-11cf-8056-444553540000");
    private static readonly Guid ScsiAdapter = new("4d36e97b-e325-11ce-bfc1-08002be10318");
    private static readonly Guid Hdc = new("4d36e96a-e325-11ce-bfc1-08002be10318");

    public static DeviceCategory Category(Guid? classGuid) => classGuid switch
    {
        null => DeviceCategory.Other,
        var g when g == Display => DeviceCategory.Gpu,
        var g when g == Net => DeviceCategory.Network,
        var g when g == Media => DeviceCategory.Audio,
        var g when g == Usb => DeviceCategory.Usb,
        var g when g == ScsiAdapter || g == Hdc => DeviceCategory.Storage,
        _ => DeviceCategory.Other,
    };
}

public static class PowerPlanParser
{
    /// <summary>
    /// Tipo di piano dal GUID; per i piani duplicati (es. "Prestazioni eccellenti" creato con
    /// powercfg -duplicatescheme ha un GUID nuovo) si ripiega sul nome, in italiano o inglese.
    /// </summary>
    public static PowerPlanKind Kind(Guid? scheme, string? name)
    {
        var byGuid = scheme switch
        {
            null => PowerPlanKind.Unknown,
            var g when g == PowerGuids.PowerSaver => PowerPlanKind.PowerSaver,
            var g when g == PowerGuids.Balanced => PowerPlanKind.Balanced,
            var g when g == PowerGuids.HighPerformance => PowerPlanKind.HighPerformance,
            var g when g == PowerGuids.UltimatePerformance => PowerPlanKind.UltimatePerformance,
            _ => PowerPlanKind.Custom,
        };
        if (byGuid != PowerPlanKind.Custom)
        {
            return byGuid;
        }

        var n = name ?? "";
        bool Has(params string[] words) => words.Any(w => n.Contains(w, StringComparison.OrdinalIgnoreCase));
        return Has("Ultimate", "eccellenti") ? PowerPlanKind.UltimatePerformance
            : Has("High performance", "Prestazioni elevate") ? PowerPlanKind.HighPerformance
            : Has("Power saver", "Risparmio") ? PowerPlanKind.PowerSaver
            : Has("Balanced", "Bilanciato") ? PowerPlanKind.Balanced
            : PowerPlanKind.Custom;
    }

    /// <summary>Modalità di alimentazione (cursore di Windows) dal GUID di sovrapposizione.</summary>
    public static string? OverlayName(string? overlayGuid) =>
        Guid.TryParse(overlayGuid, out var g)
            ? g == Guid.Empty ? "Bilanciata"
            : g == PowerGuids.OverlayBestPerformance ? "Prestazioni massime"
            : g == PowerGuids.OverlayBetterPerformance ? "Prestazioni migliori"
            : g == PowerGuids.OverlayBestEfficiency ? "Massima efficienza energetica"
            : null
            : null;
}

public static class MiscParsers
{
    /// <summary>Risoluzione del timer da unità di 100 ns a millisecondi.</summary>
    public static double TimerMs(uint hundredNanoseconds) => hundredNanoseconds / 10_000.0;

    /// <summary>"Disattiva ottimizzazioni a schermo intero" nei flag di compatibilità di un eseguibile.</summary>
    public static bool DisablesFullscreenOptimizations(string? layers) =>
        layers?.Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Maschera di affinità (KAFFINITY) da un valore binario little-endian.</summary>
    public static ulong? AffinityMask(byte[]? data)
    {
        if (data is not { Length: > 0 })
        {
            return null;
        }

        var buffer = new byte[8];
        Array.Copy(data, buffer, Math.Min(data.Length, 8));
        return BitConverter.ToUInt64(buffer, 0);
    }

    /// <summary>"Precisione puntatore avanzata": MouseSpeed 1 o 2 = attiva.</summary>
    public static bool? MouseAcceleration(string? mouseSpeed) => mouseSpeed switch
    {
        null => null,
        "0" => false,
        _ => true,
    };
}
