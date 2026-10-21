namespace PcOptimizer.Core;

/// <summary>
/// Cartelle dati dell'app. Di default in %ProgramData%\PcOptimizerPro, perché le modifiche
/// riguardano tutto il sistema e non il singolo utente. La radice è configurabile per i test.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PcOptimizerPro");
    }

    public string Root { get; }

    public string Logs => Path.Combine(Root, "logs");

    public string JournalFile => Path.Combine(Root, "journal", "changes.jsonl");

    public string Backups => Path.Combine(Root, "backups");

    public string Benchmarks => Path.Combine(Root, "benchmarks");

    public string Profiles => Path.Combine(Root, "profiles");

    public void EnsureCreated()
    {
        foreach (var dir in new[] { Logs, Path.GetDirectoryName(JournalFile)!, Backups, Benchmarks, Profiles })
        {
            Directory.CreateDirectory(dir);
        }
    }
}
