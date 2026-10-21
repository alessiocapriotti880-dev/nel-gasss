using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PcOptimizer.Core;
using PcOptimizer.Core.Logging;
using PcOptimizer.Core.Model;
using PcOptimizer.Hardware.Assessment;
using PcOptimizer.Hardware.Database;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Hardware.Windows;

Console.OutputEncoding = Encoding.UTF8;

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "scan";
switch (command)
{
    case "scan":
        return await ScanAsync(args.Skip(1).ToArray());
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
        PC Optimizer Pro - strumento a riga di comando

        pcopt scan [--json <file>] [--data <cartella>]
            Rileva l'hardware, calcola la fascia e mostra le osservazioni. Non modifica nulla.
            --json   salva profilo e valutazione completi in un file JSON (utile per segnalazioni)
            --data   cartella con tabelle aggiornate (cpu-architectures.json, gpus.json, ...)
        """);
}

static async Task<int> ScanAsync(string[] options)
{
    string? Option(string name)
    {
        var i = Array.FindIndex(options, o => o.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < options.Length ? options[i + 1] : null;
    }

    using var loggerFactory = CreateLoggerFactory();
    var dataDirs = new[] { Option("--data"), Path.Combine(AppContext.BaseDirectory, "data") }
        .Where(d => d is not null)
        .Select(d => d!)
        .ToArray();

    Console.WriteLine("Scansione dell'hardware in corso...");
    var profile = await new WindowsHardwareScanner(loggerFactory.CreateLogger<WindowsHardwareScanner>()).ScanAsync();
    var assessment = new HardwareAnalyzer(HardwareDatabase.Load(dataDirs)).Assess(profile);

    Report.Print(profile, assessment);

    if (Option("--json") is { } jsonPath)
    {
        var json = JsonSerializer.Serialize(new { profile, assessment }, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        });
        await File.WriteAllTextAsync(jsonPath, json);
        Console.WriteLine($"\nReport completo salvato in {Path.GetFullPath(jsonPath)}");
    }

    return 0;
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

/// <summary>Stampa leggibile del profilo e della valutazione.</summary>
internal static class Report
{
    private const double GiB = 1024.0 * 1024 * 1024;

    public static void Print(HardwareProfile p, HardwareAssessment a)
    {
        var t = a.Tier;
        Console.WriteLine();
        Heading($"Fascia: {TierClassifier.TierName(t.Tier).ToUpperInvariant()}  " +
                $"(punteggio {Num(t.Score)}/100, affidabilità {ConfidenceName(t.Confidence)})");
        foreach (var c in t.Components)
        {
            Console.WriteLine($"  {c.Component,-6} {c.Score,5:0}  peso {Num(c.Weight, "0.00")}  {c.Explanation}");
        }

        foreach (var cap in t.AppliedCaps)
        {
            Console.WriteLine($"  Limite: {cap}");
        }

        Console.WriteLine();
        Heading("Componenti");
        var cpu = p.Cpu;
        Line("CPU", cpu.BrandString);
        Line("", $"{a.Cpu.ArchitectureName} ({a.Cpu.Generation}), famiglia 0x{cpu.Family:X} modello 0x{cpu.Model:X} " +
                 $"stepping {cpu.Stepping}, microcode {(cpu.MicrocodeRevision is { } mc ? $"0x{mc:X}" : "n/d")}");

        foreach (var g in a.Gpus)
        {
            var vram = g.Gpu.DedicatedMemoryBytes is { } bytes ? $"{Num(bytes / GiB)} GB, " : "";
            var driver = g.Gpu.VendorDriverVersion ?? g.Gpu.DriverVersion ?? "n/d";
            var date = g.Gpu.DriverDate is { } d ? $" del {d:dd/MM/yyyy}" : "";
            var role = g == a.PrimaryGpu ? " [principale]" : g.IsVirtual ? " [virtuale]" : g.IsIntegrated ? " [integrata]" : "";
            Line("GPU", $"{g.Gpu.Name}{role}: {vram}driver {driver}{date}");
        }

        var m = a.Memory;
        var channels = m.Channels is { } ch ? $"{ch} {(ch == 1 ? "canale" : "canali")}{(m.ChannelsAssumed ? " (stimato)" : "")}" : "canali n/d";
        Line("RAM", $"{Num(m.InstalledGB)} GB {(m.Type == MemoryType.Unknown ? "" : m.Type.ToString().ToUpperInvariant())} " +
                    $"{m.ConfiguredMTs?.ToString() ?? "n/d"} MT/s, {m.ModuleCount} moduli, {channels}");
        Line("", $"XMP/EXPO: {m.XmpExplanation}");

        foreach (var disk in p.Disks)
        {
            Line("Disco", $"{disk.FriendlyName}: {Num(disk.SizeBytes / GiB, "0")} GB, {disk.MediaType}/{disk.BusType}, " +
                          $"salute {disk.Health}{(disk.IsSystemDisk ? " [sistema]" : "")}");
        }

        var b = p.Board;
        var chipset = a.Board.Chipset is { } cs
            ? $", chipset {cs} (OC CPU: {OcName(a.Board.CpuOverclock)}, OC RAM: {OcName(a.Board.MemoryOverclock)})"
            : "";
        Line("Scheda", $"{b.BoardManufacturer} {b.BoardProduct}{chipset}{(b.IsLaptop ? " [portatile]" : "")}");
        Line("BIOS", $"{b.BiosVendor} {b.BiosVersion}{(b.BiosReleaseDate is { } bd ? $" del {bd:dd/MM/yyyy}" : "")}, " +
                     $"{b.Firmware}, Secure Boot {(b.SecureBootEnabled switch { true => "attivo", false => "disattivo", _ => "n/d" })}");

        foreach (var n in p.NetworkAdapters.Where(n => n.IsConnected))
        {
            var speed = n.LinkSpeedBps is { } bps ? $" {bps / 1_000_000} Mbps" : "";
            Line("Rete", $"{n.Description}: {n.Medium}{speed}{(n.IsPrimary ? " [principale]" : "")}");
        }

        foreach (var d in p.Displays)
        {
            var max = d.MaxRefreshHz is { } mx ? $" (max {mx} Hz)" : "";
            Line("Monitor", $"{d.Name}: {d.Width}x{d.Height} a {Num(d.RefreshHz, "0.##")} Hz{max}, {d.Connection}" +
                            $"{(d.IsPrimary ? " [principale]" : "")}");
        }

        Line("Sistema", $"{p.Os.FriendlyName} {p.Os.DisplayVersion} (build {p.Os.Build}.{p.Os.Ubr})");

        Console.WriteLine();
        Heading(a.Findings.Count == 0 ? "Nessuna osservazione" : $"Osservazioni ({a.Findings.Count})");
        foreach (var f in a.Findings)
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

    private static void Heading(string text)
    {
        Console.WriteLine(text);
        Console.WriteLine(new string('-', Math.Min(text.Length, 100)));
    }

    private static void Line(string label, string text) => Console.WriteLine($"  {label,-8} {text}");

    private static string Num(double value, string format = "0.#") =>
        value.ToString(format, System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

    private static string SeverityName(FindingSeverity s) => s switch
    {
        FindingSeverity.Critical => "CRITICO",
        FindingSeverity.Warning => "ATTENZIONE",
        FindingSeverity.Suggestion => "CONSIGLIO",
        _ => "INFO",
    };

    private static string ConfidenceName(Confidence c) => c switch
    {
        Confidence.High => "alta",
        Confidence.Medium => "media",
        _ => "bassa",
    };

    private static string OcName(OcSupport s) => s switch
    {
        OcSupport.Yes => "sì",
        OcSupport.No => "no",
        OcSupport.Limited => "limitato",
        _ => "n/d",
    };
}
