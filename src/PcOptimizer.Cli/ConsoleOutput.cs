using System.Globalization;
using PcOptimizer.Core.Model;

namespace PcOptimizer.Cli;

/// <summary>Formattazione comune dei report su console.</summary>
internal static class ConsoleOutput
{
    public const double GiB = 1024.0 * 1024 * 1024;

    public static void Heading(string text)
    {
        Console.WriteLine();
        Console.WriteLine(text);
        Console.WriteLine(new string('-', Math.Min(text.Length, 100)));
    }

    public static void Line(string label, string text) => Console.WriteLine($"  {label,-13} {text}");

    /// <summary>Numero con virgola decimale all'italiana, indipendente dalla lingua di sistema.</summary>
    public static string Num(double value, string format = "0.#") =>
        value.ToString(format, CultureInfo.InvariantCulture).Replace('.', ',');

    public static string YesNo(bool? value, string unknown = "n/d") => value switch
    {
        true => "sì",
        false => "no",
        _ => unknown,
    };

    public static void PrintFindings(IReadOnlyList<Finding> findings)
    {
        Heading(findings.Count == 0 ? "Nessuna osservazione" : $"Osservazioni ({findings.Count})");
        foreach (var f in findings)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = f.Severity switch
            {
                FindingSeverity.Critical => ConsoleColor.Red,
                FindingSeverity.Warning => ConsoleColor.Yellow,
                FindingSeverity.Suggestion => ConsoleColor.Cyan,
                _ => ConsoleColor.Gray,
            };
            Console.WriteLine($"  [{SeverityName(f.Severity)}] {f.Title}");
            Console.ForegroundColor = previous;
            Console.WriteLine($"      {f.Detail}");
            if (f.Recommendation is not null)
            {
                Console.WriteLine($"      → {f.Recommendation}");
            }
        }
    }

    private static string SeverityName(FindingSeverity s) => s switch
    {
        FindingSeverity.Critical => "CRITICO",
        FindingSeverity.Warning => "ATTENZIONE",
        FindingSeverity.Suggestion => "CONSIGLIO",
        _ => "INFO",
    };
}
