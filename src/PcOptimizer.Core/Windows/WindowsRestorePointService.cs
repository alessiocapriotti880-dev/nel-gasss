using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using PcOptimizer.Core.Safety;

namespace PcOptimizer.Core.Windows;

/// <summary>
/// Punti di ripristino tramite WMI (root\default:SystemRestore), la stessa API usata da
/// Checkpoint-Computer in PowerShell. Non modifica la frequenza minima di Windows (24 ore):
/// se un punto recente esiste già, lo riusa e lo dichiara.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRestorePointService(ILogger<WindowsRestorePointService> log) : IRestorePointService
{
    private const uint ModifySettings = 12;       // RestorePointType: MODIFY_SETTINGS
    private const uint BeginSystemChange = 100;   // EventType: BEGIN_SYSTEM_CHANGE
    private const uint ErrorServiceDisabled = 1058;

    private static readonly TimeSpan WindowsMinimumInterval = TimeSpan.FromHours(24);

    public RestorePointResult EnsureRestorePoint(string description)
    {
        try
        {
            var before = LatestPoint();

            using var cls = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null);
            using var args = cls.GetMethodParameters("CreateRestorePoint");
            args["Description"] = description;
            args["RestorePointType"] = ModifySettings;
            args["EventType"] = BeginSystemChange;
            using var output = cls.InvokeMethod("CreateRestorePoint", args, null);
            var code = Convert.ToUInt32(output["ReturnValue"]);

            var after = LatestPoint();
            if (after is not null && (before is null || after.Value.Sequence != before.Value.Sequence))
            {
                log.LogInformation("Punto di ripristino creato: {Time}", after.Value.Time);
                return new RestorePointResult(RestorePointOutcome.Created, after.Value.Time, "Punto di ripristino creato.");
            }

            if (after is not null && DateTime.Now - after.Value.Time < WindowsMinimumInterval)
            {
                log.LogInformation("Riuso punto di ripristino recente: {Time}", after.Value.Time);
                return new RestorePointResult(RestorePointOutcome.ReusedRecent, after.Value.Time,
                    $"Windows consente un punto di ripristino ogni 24 ore: verrà usato quello del {after.Value.Time:g}.");
            }

            if (code == ErrorServiceDisabled || after is null)
            {
                return new RestorePointResult(RestorePointOutcome.Disabled, null,
                    "Protezione sistema disattivata sull'unità di Windows. Attivala da " +
                    "Pannello di controllo > Sistema > Protezione sistema, oppure prosegui solo con i backup dell'app.");
            }

            return new RestorePointResult(RestorePointOutcome.Failed, null, $"Creazione non riuscita (codice {code}).");
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            log.LogError(ex, "Errore nella creazione del punto di ripristino.");
            return new RestorePointResult(RestorePointOutcome.Failed, null, $"Errore: {ex.Message}");
        }
    }

    private static (uint Sequence, DateTime Time)? LatestPoint()
    {
        using var searcher = new ManagementObjectSearcher(@"root\default", "SELECT SequenceNumber, CreationTime FROM SystemRestore");
        (uint, DateTime)? latest = null;
        foreach (var obj in searcher.Get())
        {
            using (obj)
            {
                var seq = Convert.ToUInt32(obj["SequenceNumber"]);
                var time = ManagementDateTimeConverter.ToDateTime((string)obj["CreationTime"]);
                if (latest is null || seq > latest.Value.Item1)
                {
                    latest = (seq, time);
                }
            }
        }

        return latest;
    }
}
