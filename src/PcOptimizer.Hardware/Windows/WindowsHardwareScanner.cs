using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using PcOptimizer.Hardware.Model;

namespace PcOptimizer.Hardware.Windows;

/// <summary>
/// Scansione completa dell'hardware su Windows. Solo lettura: WMI, registro, CPUID e API di sistema.
/// Ogni parte è isolata: se una fallisce (es. servizio WMI danneggiato) le altre proseguono e
/// l'errore finisce in <see cref="HardwareProfile.ScanErrors"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHardwareScanner(ILogger<WindowsHardwareScanner> log) : IHardwareScanner
{
    public Task<HardwareProfile> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(cancellationToken), cancellationToken);

    private HardwareProfile Scan(CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        T Run<T>(string area, Func<T> detect, T fallback)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = DateTime.UtcNow;
            try
            {
                var value = detect();
                log.LogDebug("Rilevamento {Area} completato in {Ms} ms", area, (DateTime.UtcNow - started).TotalMilliseconds);
                return value;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Rilevamento {Area} non riuscito", area);
                errors.Add($"{area}: {ex.Message}");
                return fallback;
            }
        }

        var profile = new HardwareProfile
        {
            ScannedAt = DateTimeOffset.Now,
            Os = Run("Sistema operativo", OsDetector.Detect, new OsInfo()),
            Cpu = Run("CPU", CpuDetector.Detect, new CpuInfo()),
            Gpus = Run("GPU", GpuDetector.Detect, []),
            Memory = Run("RAM", MemoryDetector.Detect, new MemoryInfo()),
            Disks = Run("Dischi", StorageDetector.Detect, []),
            Board = Run("Scheda madre e BIOS", BoardDetector.Detect, new BoardInfo()),
            NetworkAdapters = Run("Rete", NetworkDetector.Detect, []),
            Displays = Run("Monitor", DisplayDetector.Detect, []),
        };

        log.LogInformation("Scansione hardware completata: {Cpu}, {Gpus} GPU, {Errors} errori",
            profile.Cpu.BrandString, profile.Gpus.Count, errors.Count);
        return profile with { ScanErrors = errors };
    }
}
