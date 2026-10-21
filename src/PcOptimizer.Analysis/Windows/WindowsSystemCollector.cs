using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Analysis.Windows;

/// <summary>
/// Raccolta completa della configurazione su Windows, in sola lettura. Ogni area è isolata:
/// se una fallisce (es. WMI di Defender non disponibile) le altre proseguono e l'errore finisce
/// in <see cref="SystemSnapshot.Errors"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemCollector(ILogger<WindowsSystemCollector> log) : ISystemCollector
{
    public Task<SystemSnapshot> CollectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Collect(cancellationToken), cancellationToken);

    private SystemSnapshot Collect(CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        T Run<T>(string area, Func<T> collect, T fallback)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = DateTime.UtcNow;
            try
            {
                var value = collect();
                log.LogDebug("Analisi {Area} completata in {Ms} ms", area, (DateTime.UtcNow - started).TotalMilliseconds);
                return value;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Analisi {Area} non riuscita", area);
                errors.Add($"{area}: {ex.Message}");
                return fallback;
            }
        }

        void ReportPartial(string area, Exception ex)
        {
            log.LogInformation(ex, "Dato non disponibile: {Area}", area);
            errors.Add($"{area}: {ex.Message}");
        }

        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var files = new FileInfoCache();

        var snapshot = new SystemSnapshot
        {
            CollectedAt = DateTimeOffset.Now,
            IsElevated = IsElevated(),
            Services = Run("Servizi", () => ServiceCollector.Collect(systemRoot, files), []),
            Startup = Run("Avvio automatico", () => StartupCollector.Collect(systemRoot, files), []),
            ScheduledTasks = Run("Attività pianificate", TaskCollector.Collect, []),
            KernelDrivers = Run("Driver", () => DriverCollector.CollectKernel(systemRoot, files), []),
            DeviceDrivers = Run("Driver dei dispositivi", DriverCollector.CollectDevices, []),
            Processes = Run("Processi", ProcessCollector.Collect, []),
            Power = Run("Alimentazione", PowerCollector.Collect, new PowerSnapshot()),
            Timer = Run("Timer", TimerCollector.Collect, new TimerSnapshot()),
            PciDevices = Run("Interrupt", InterruptCollector.Collect, []),
            GpuScheduling = Run("Pianificazione GPU", GraphicsCollector.Collect, []),
            Registry = Run("Registro", RegistryCollector.ReadProbes, new Dictionary<string, SettingValue>()),
            FsoDisabledApps = Run("Compatibilità app", RegistryCollector.FsoDisabledApps, []),
            GpuPreferences = Run("Preferenze GPU", RegistryCollector.GpuPreferences, []),
            TcpInterfaces = Run("Rete (TCP)", RegistryCollector.TcpInterfaces, []),
            Security = Run("Sicurezza", () => SecurityCollector.Collect(ReportPartial), new SecuritySnapshot()),
            PageFile = Run("File di paging", PageFileCollector.Collect, new PageFileSnapshot()),
        };

        log.LogInformation("Analisi del sistema completata: {Services} servizi, {Startup} voci di avvio, {Tasks} attività, {Errors} errori",
            snapshot.Services.Count, snapshot.Startup.Count, snapshot.ScheduledTasks.Count, errors.Count);
        return snapshot with { Errors = errors };
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
