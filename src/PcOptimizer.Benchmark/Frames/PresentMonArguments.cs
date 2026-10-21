using System.Globalization;
using System.Text.RegularExpressions;

namespace PcOptimizer.Benchmark.Frames;

/// <summary>Riga di comando di PresentMon: le opzioni hanno un trattino in 1.x e due in 2.x.</summary>
public static partial class PresentMonArguments
{
    public const string SessionName = "PcOptimizerPro-PresentMon";

    /// <summary>Versione principale dal nome del file scaricato da GitHub (es. "PresentMon-2.3.1-x64.exe").</summary>
    public static int? MajorVersionFromFileName(string path) =>
        VersionPattern().Match(Path.GetFileName(path)) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;

    /// <summary>Nome dell'eseguibile come lo vuole PresentMon ("cs2" diventa "cs2.exe").</summary>
    public static string ProcessName(string name)
    {
        var file = Path.GetFileName(name.Trim());
        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file : file + ".exe";
    }

    public static IReadOnlyList<string> Build(int majorVersion, string processName, string outputFile, int seconds, int delaySeconds = 0)
    {
        var dash = majorVersion >= 2 ? "--" : "-";
        var args = new List<string>
        {
            dash + "process_name", ProcessName(processName),
            dash + "output_file", outputFile,
            dash + "timed", seconds.ToString(CultureInfo.InvariantCulture),
            dash + "terminate_after_timed",
            dash + "session_name", SessionName,
            dash + "stop_existing_session",
        };
        if (delaySeconds > 0)
        {
            args.Add(dash + "delay");
            args.Add(delaySeconds.ToString(CultureInfo.InvariantCulture));
        }

        return args;
    }

    [GeneratedRegex(@"PresentMon[-_ ]?v?(\d+)\.", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();
}
