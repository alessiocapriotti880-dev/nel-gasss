using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PcOptimizer.Analysis;
using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Evaluation;
using PcOptimizer.Analysis.Windows;
using PcOptimizer.Cli;
using PcOptimizer.Core;
using PcOptimizer.Core.Logging;
using PcOptimizer.Hardware.Assessment;
using PcOptimizer.Hardware.Database;
using PcOptimizer.Hardware.Windows;

Console.OutputEncoding = Encoding.UTF8;

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "scan";
var options = args.Skip(1).ToArray();
switch (command)
{
    case "scan":
        return await ScanAsync(options);
    case "analyze":
        return await AnalyzeAsync(options);
    case "help" or "--help" or "-h" or "/?":
        PrintHelp();
        return 0;
    default:
        Console.Error.WriteLine($"Comando sconosciuto: {command}");
        PrintHelp();
        return 2;
}

static void PrintHelp()
{
    Console.WriteLine("""
        PC Optimizer Pro - strumento a riga di comando (sola lettura: non modifica nulla)

        pcopt scan [--json <file>] [--data <cartella>]
            Rileva l'hardware, calcola la fascia e mostra le osservazioni.

        pcopt analyze [--json <file>] [--data <cartella>]
            Analizza la configurazione di Windows: servizi, avvio, attività, driver, alimentazione,
            timer, interrupt, impostazioni di gioco, sicurezza e modifiche lasciate da altri ottimizzatori.
            Per il quadro completo va eseguito come amministratore.

            --json   salva tutti i dati raccolti e la valutazione in un file JSON
            --data   cartella con tabelle aggiornate (cpu-architectures.json, gpus.json, known-software.json, ...)
        """);
}

static async Task<int> ScanAsync(string[] options)
{
    using var loggerFactory = CreateLoggerFactory();
    Console.WriteLine("Scansione dell'hardware in corso...");
    var profile = await new WindowsHardwareScanner(loggerFactory.CreateLogger<WindowsHardwareScanner>()).ScanAsync();
    var assessment = new HardwareAnalyzer(HardwareDatabase.Load(DataDirectories(options))).Assess(profile);

    HardwareReportPrinter.Print(profile, assessment);
    await SaveJsonAsync(options, new { profile, assessment });
    return 0;
}

static async Task<int> AnalyzeAsync(string[] options)
{
    using var loggerFactory = CreateLoggerFactory();
    var dataDirs = DataDirectories(options);

    Console.WriteLine("Scansione dell'hardware in corso...");
    var hardware = await new WindowsHardwareScanner(loggerFactory.CreateLogger<WindowsHardwareScanner>()).ScanAsync();
    var hardwareAssessment = new HardwareAnalyzer(HardwareDatabase.Load(dataDirs)).Assess(hardware);

    Console.WriteLine("Analisi della configurazione di Windows in corso...");
    var snapshot = await new WindowsSystemCollector(loggerFactory.CreateLogger<WindowsSystemCollector>()).CollectAsync();
    var report = new SystemAnalyzer(KnownSoftwareCatalog.Load(dataDirs))
        .Analyze(snapshot, AnalysisContext.From(hardware, hardwareAssessment));

    ConsoleOutput.Heading("Analisi del sistema");
    ConsoleOutput.Line("Fascia", $"{TierClassifier.TierName(hardwareAssessment.Tier.Tier)} " +
                                   $"(punteggio {ConsoleOutput.Num(hardwareAssessment.Tier.Score)}/100: dettagli con \"pcopt scan\")");
    SystemReportPrinter.Print(snapshot, report);
    await SaveJsonAsync(options, new { hardware, hardwareAssessment, snapshot, report });
    return 0;
}

static string[] DataDirectories(string[] options) =>
    new[] { Option(options, "--data"), Path.Combine(AppContext.BaseDirectory, "data") }
        .Where(d => d is not null)
        .Select(d => d!)
        .ToArray();

static string? Option(string[] options, string name)
{
    var i = Array.FindIndex(options, o => o.Equals(name, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < options.Length ? options[i + 1] : null;
}

static async Task SaveJsonAsync(string[] options, object data)
{
    if (Option(options, "--json") is not { } path)
    {
        return;
    }

    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    });
    await File.WriteAllTextAsync(path, json);
    Console.WriteLine($"\nReport completo salvato in {Path.GetFullPath(path)}");
}

static ILoggerFactory CreateLoggerFactory()
{
    try
    {
        return AppLogging.CreateFactory(new AppPaths());
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Log su file non disponibile: {ex.Message}");
        return NullLoggerFactory.Instance;
    }
}
