using System.Text;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Backup;

/// <summary>
/// Genera un file .reg (formato "Windows Registry Editor Version 5.00") con i valori originali.
/// È un backup indipendente dall'app: anche se l'app non si avvia più, l'utente può
/// ripristinare con un doppio clic sul file.
/// </summary>
public static class RegFileWriter
{
    public static string Build(IEnumerable<(RegistryValueTarget Target, SettingValue Value)> entries)
    {
        var sb = new StringBuilder();
        sb.Append("Windows Registry Editor Version 5.00\r\n");

        // Raggruppa per chiave mantenendo l'ordine di prima apparizione.
        foreach (var group in entries.GroupBy(e => $@"{e.Target.HiveName}\{e.Target.KeyPath}", StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("\r\n[").Append(group.Key).Append("]\r\n");
            foreach (var (target, value) in group)
            {
                sb.Append(FormatName(target.ValueName)).Append('=').Append(FormatValue(target.Kind, value)).Append("\r\n");
            }
        }

        return sb.ToString();
    }

    /// <summary>Scrive il file in UTF-16 LE con BOM, la codifica che regedit si aspetta.</summary>
    public static void WriteFile(string path, IEnumerable<(RegistryValueTarget Target, SettingValue Value)> entries) =>
        File.WriteAllText(path, Build(entries), Encoding.Unicode);

    private static string FormatName(string name) => name.Length == 0 ? "@" : $"\"{Escape(name)}\"";

    internal static string FormatValue(RegValueKind kind, SettingValue value)
    {
        // "-" cancella il valore: era assente prima della modifica.
        if (!value.Exists)
        {
            return "-";
        }

        return kind switch
        {
            RegValueKind.DWord => $"dword:{value.AsUInt32():x8}",
            RegValueKind.QWord => "hex(b):" + Hex(BitConverter.GetBytes(value.AsUInt64())),
            RegValueKind.String => $"\"{Escape(value.Data!)}\"",
            RegValueKind.ExpandString => "hex(2):" + Hex(Encoding.Unicode.GetBytes(value.Data! + "\0")),
            RegValueKind.MultiString => "hex(7):" + Hex(Encoding.Unicode.GetBytes(MultiPayload(value))),
            RegValueKind.Binary => "hex:" + Hex(value.AsBinary()),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    // REG_MULTI_SZ: ogni stringa terminata da NUL, più un NUL finale.
    private static string MultiPayload(SettingValue value) =>
        string.Concat(value.AsMulti().Select(s => s + "\0")) + "\0";

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // BitConverter è little-endian su x64, come richiede il formato .reg.
    private static string Hex(byte[] bytes) => string.Join(',', bytes.Select(b => b.ToString("x2")));
}
