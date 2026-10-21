using System.Text.Json;
using System.Text.Json.Serialization;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Backup;

/// <summary>Scrive su disco i valori originali prima di ogni applicazione.</summary>
public interface IBackupWriter
{
    /// <summary>Crea il backup e restituisce la cartella in cui è stato salvato.</summary>
    string Write(Guid transactionId, string tweakId, IReadOnlyList<CapturedSetting> settings);
}

/// <summary>
/// Per ogni transazione crea una cartella con:
/// <list type="bullet">
/// <item><c>original.reg</c>: i valori di registro originali, ripristinabili con regedit;</item>
/// <item><c>snapshot.json</c>: tutte le impostazioni (anche servizi e piano energetico) con valore originale e nuovo.</item>
/// </list>
/// </summary>
public sealed class BackupService(string backupRoot) : IBackupWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Write(Guid transactionId, string tweakId, IReadOnlyList<CapturedSetting> settings)
    {
        var folder = Path.Combine(backupRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}_{Sanitize(tweakId)}_{transactionId:N}");
        Directory.CreateDirectory(folder);

        var registry = settings
            .Where(s => s.Target is RegistryValueTarget)
            .Select(s => ((RegistryValueTarget)s.Target, s.Original))
            .ToList();
        if (registry.Count > 0)
        {
            RegFileWriter.WriteFile(Path.Combine(folder, "original.reg"), registry);
        }

        File.WriteAllText(Path.Combine(folder, "snapshot.json"), JsonSerializer.Serialize(settings, JsonOptions));
        return folder;
    }

    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
