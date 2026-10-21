using System.Globalization;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Tweaks;

namespace PcOptimizer.Tweaks.Maintenance;

/// <summary>
/// Pulizia dei file temporanei più vecchi di un giorno nelle cartelle temporanee dell'utente e di Windows.
/// Non reversibile, quindi sempre con anteprima. I file in uso vengono saltati, non forzati.
/// </summary>
public sealed class TempCleanup : IMaintenanceAction
{
    private readonly IReadOnlyList<string> _folders;
    private readonly TimeSpan _minAge;
    private readonly Func<DateTime> _now;

    public TempCleanup(IReadOnlyList<string>? folders = null, TimeSpan? minAge = null, Func<DateTime>? now = null)
    {
        _folders = folders ?? DefaultFolders();
        _minAge = minAge ?? TimeSpan.FromDays(1);
        _now = now ?? (() => DateTime.UtcNow);
    }

    public string Id => "maintenance.temp-cleanup";

    public string Title => "Pulizia dei file temporanei";

    public string Description =>
        "Elimina i file temporanei più vecchi di 24 ore (cartella TEMP dell'utente e di Windows). Libera spazio; " +
        "non migliora gli FPS. Operazione non annullabile: controlla l'anteprima.";

    public Risk Risk => Risk.Low;

    public static IReadOnlyList<string> DefaultFolders() =>
        new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") }
            .Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f))
            .Select(f => Path.GetFullPath(f).TrimEnd(Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public Task<string> PreviewAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        var files = Candidates(cancellationToken).ToList();
        var bytes = files.Sum(f => f.Length);
        return $"{files.Count} file ({Mb(bytes)}) più vecchi di {_minAge.TotalHours:0} ore in: {string.Join(", ", _folders)}.";
    }, cancellationToken);

    public Task<string> RunAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        long freed = 0;
        int deleted = 0, skipped = 0;
        foreach (var file in Candidates(cancellationToken).ToList())
        {
            try
            {
                var length = file.Length;
                file.Delete();
                freed += length;
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++; // file in uso o protetto: si lascia dov'è
            }
        }

        foreach (var folder in _folders)
        {
            RemoveEmptyDirectories(folder);
        }

        return $"Eliminati {deleted} file ({Mb(freed)}); {skipped} in uso o protetti lasciati al loro posto.";
    }, cancellationToken);

    private IEnumerable<FileInfo> Candidates(CancellationToken cancellationToken)
    {
        var limit = _now() - _minAge;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        foreach (var folder in _folders.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                    if (info.LastWriteTimeUtc > limit || info.LastAccessTimeUtc > limit)
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                yield return info;
            }
        }
    }

    private static void RemoveEmptyDirectories(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var dir in Directory.EnumerateDirectories(root, "*", options).OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',') + " MB";
}
