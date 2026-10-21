using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using PcOptimizer.Benchmark.Frames;

namespace PcOptimizer.Benchmark.Windows;

/// <summary>
/// Avvia PresentMon (strumento open source di Intel) per registrare i frame di un gioco. PresentMon legge gli eventi
/// ETW di DirectX/Vulkan del sistema: non si inietta nel gioco e non apre il suo processo, quindi è compatibile con
/// gli anti-cheat. L'eseguibile non è incluso: va scaricato da github.com/GameTechDev/PresentMon/releases.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PresentMonRunner
{
    private readonly ILogger _logger;

    public PresentMonRunner(ILogger logger) => _logger = logger;

    /// <summary>Cartelle dove cercare PresentMon*.exe se non indicato esplicitamente.</summary>
    public static IEnumerable<string> SearchDirectories(string dataRoot) =>
    [
        Path.Combine(AppContext.BaseDirectory, "tools"),
        AppContext.BaseDirectory,
        Path.Combine(dataRoot, "tools"),
    ];

    public static string? Locate(string? configured, string dataRoot)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        }

        return SearchDirectories(dataRoot)
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "PresentMon*.exe"))
            .OrderByDescending(f => Version(f))
            .FirstOrDefault();
    }

    public static int Version(string exe)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            if (info.FileMajorPart > 0)
            {
                return info.FileMajorPart;
            }
        }
        catch (FileNotFoundException)
        {
        }

        return PresentMonArguments.MajorVersionFromFileName(exe) ?? 2;
    }

    public async Task<(PresentMonCapture Capture, string Source)> CaptureAsync(
        string exe, string processName, int seconds, int delaySeconds, CancellationToken cancellationToken = default)
    {
        var version = Version(exe);
        var output = Path.Combine(Path.GetTempPath(), $"pcopt-presentmon-{Guid.NewGuid():N}.csv");
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in PresentMonArguments.Build(version, processName, output, seconds, delaySeconds))
        {
            start.ArgumentList.Add(arg);
        }

        _logger.LogInformation("Avvio PresentMon {Version}.x: {Exe} {Args}", version, exe, string.Join(' ', start.ArgumentList));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Impossibile avviare PresentMon.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds + delaySeconds + 60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        var errors = (await stderr).Trim();
        await stdout;
        try
        {
            if (!File.Exists(output))
            {
                throw new InvalidOperationException(
                    $"PresentMon non ha prodotto dati (codice {process.ExitCode}). Verifica che {PresentMonArguments.ProcessName(processName)} sia in esecuzione e stia disegnando frame." +
                    (errors.Length > 0 ? $" Dettagli: {errors[..Math.Min(errors.Length, 300)]}" : ""));
            }

            return (PresentMonCsvParser.ParseFile(output), $"PresentMon {version}.x");
        }
        finally
        {
            File.Delete(output);
        }
    }
}
