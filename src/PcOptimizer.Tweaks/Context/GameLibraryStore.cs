using System.Text.Json;
using PcOptimizer.Core;

namespace PcOptimizer.Tweaks.Context;

/// <summary>Elenco dei giochi indicati dall'utente, salvato in %ProgramData%\PcOptimizerPro\profiles\games.json.</summary>
public sealed class GameLibraryStore(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private string FilePath => Path.Combine(paths.Profiles, "games.json");

    public GameLibrary Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<GameLibrary>(File.ReadAllText(FilePath), JsonOptions) ?? GameLibrary.Empty
                : GameLibrary.Empty;
        }
        catch (JsonException)
        {
            return GameLibrary.Empty;
        }
    }

    public GameLibrary Add(string executablePath, string? name = null)
    {
        var full = Path.GetFullPath(executablePath);
        if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Indica il file .exe del gioco.", nameof(executablePath));
        }

        var current = Load().Games.Where(g => !g.ExecutablePath.Equals(full, StringComparison.OrdinalIgnoreCase));
        var library = new GameLibrary([.. current, new GameEntry(name ?? Path.GetFileNameWithoutExtension(full), full)]);
        Save(library);
        return library;
    }

    public GameLibrary Remove(string executableOrName)
    {
        var library = new GameLibrary(Load().Games.Where(g =>
            !g.ExecutablePath.Equals(executableOrName, StringComparison.OrdinalIgnoreCase) &&
            !g.Name.Equals(executableOrName, StringComparison.OrdinalIgnoreCase) &&
            !g.ExecutableName.Equals(executableOrName, StringComparison.OrdinalIgnoreCase)).ToList());
        Save(library);
        return library;
    }

    private void Save(GameLibrary library)
    {
        Directory.CreateDirectory(paths.Profiles);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(library, JsonOptions));
    }
}
