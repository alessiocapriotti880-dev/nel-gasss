using System.Text.Json;
using System.Text.Json.Serialization;
using PcOptimizer.Core;

namespace PcOptimizer.Optimization.Session;

/// <summary>Sessione corrente e storico in %ProgramData%\PcOptimizerPro\sessions.</summary>
public sealed class SessionStore(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private string Folder => Path.Combine(paths.Root, "sessions");

    private string CurrentPath => Path.Combine(Folder, "current.json");

    /// <summary>Salvataggio atomico: un file scritto a metà non deve mai sostituire uno valido.</summary>
    public void Save(OptimizationSession session)
    {
        Directory.CreateDirectory(Folder);
        var json = JsonSerializer.Serialize(session, JsonOptions);
        var temp = CurrentPath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, CurrentPath, overwrite: true);
        if (session.Phase is SessionPhase.Completed or SessionPhase.Aborted)
        {
            File.WriteAllText(Path.Combine(Folder, $"{session.StartedAt.UtcDateTime:yyyyMMdd-HHmmss}_{session.Id.ToString("N")[..8]}.json"), json);
        }
    }

    public OptimizationSession? Current()
    {
        try
        {
            return File.Exists(CurrentPath) ? JsonSerializer.Deserialize<OptimizationSession>(File.ReadAllText(CurrentPath), JsonOptions) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public IReadOnlyList<OptimizationSession> History() =>
        Directory.Exists(Folder)
            ? Directory.EnumerateFiles(Folder, "*_*.json")
                .Select(f =>
                {
                    try
                    {
                        return JsonSerializer.Deserialize<OptimizationSession>(File.ReadAllText(f), JsonOptions);
                    }
                    catch (JsonException)
                    {
                        return null;
                    }
                })
                .Where(s => s is not null)
                .Select(s => s!)
                .OrderByDescending(s => s.StartedAt)
                .ToList()
            : [];
}
