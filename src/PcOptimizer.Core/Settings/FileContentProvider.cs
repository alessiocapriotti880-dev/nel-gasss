using System.Text;

namespace PcOptimizer.Core.Settings;

/// <summary>
/// File di testo creati dall'app. Per sicurezza gestisce solo script .cmd: non può sovrascrivere né cancellare
/// file di sistema o di altri programmi, anche se un intervento lo chiedesse per errore.
/// </summary>
public sealed class FileContentProvider : ISettingProvider
{
    public bool CanHandle(SettingTarget target) => target is FileContentTarget;

    public SettingValue Read(SettingTarget target)
    {
        var path = Check(target);
        return File.Exists(path) ? SettingValue.Of(File.ReadAllText(path, Encoding.UTF8)) : SettingValue.Absent;
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        var path = Check(target);
        if (!value.Exists)
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Senza BOM: cmd.exe interpreta il BOM come caratteri del primo comando.
        File.WriteAllText(path, value.Data ?? "", new UTF8Encoding(false));
    }

    private static string Check(SettingTarget target)
    {
        var path = ((FileContentTarget)target).FilePath;
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Percorso non consentito: {path} (solo script .cmd con percorso completo).");
        }

        return path;
    }
}
