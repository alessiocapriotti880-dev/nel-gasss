using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Windows;

/// <summary>
/// Attiva o disattiva attività pianificate tramite l'API dell'Utilità di pianificazione (COM "Schedule.Service"),
/// come la console di Windows. Non crea né cancella attività. Quelle protette rifiutano la modifica con
/// "accesso negato": l'errore annulla l'intervento.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScheduledTaskProvider : ISettingProvider
{
    private const int FileNotFound = unchecked((int)0x80070002);
    private const int PathNotFound = unchecked((int)0x80070003);

    public bool CanHandle(SettingTarget target) => target is ScheduledTaskTarget;

    public SettingValue Read(SettingTarget target)
    {
        var task = Open(((ScheduledTaskTarget)target).TaskPath);
        if (task is null)
        {
            return SettingValue.Absent;
        }

        try
        {
            return SettingValue.Of((bool)task.Enabled ? ScheduledTaskValues.Enabled : ScheduledTaskValues.Disabled);
        }
        finally
        {
            Marshal.FinalReleaseComObject(task);
        }
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        if (!value.Exists)
        {
            return; // un'attività che non esisteva non va creata
        }

        var path = ((ScheduledTaskTarget)target).TaskPath;
        var task = Open(path) ?? throw new InvalidOperationException($"Attività pianificata non trovata: {path}");
        try
        {
            task.Enabled = value.Data == ScheduledTaskValues.Enabled;
        }
        finally
        {
            Marshal.FinalReleaseComObject(task);
        }
    }

    private static dynamic? Open(string path)
    {
        var split = path.LastIndexOf('\\');
        var folderPath = split <= 0 ? "\\" : path[..split];
        var name = path[(split + 1)..];
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new NotSupportedException("Utilità di pianificazione non disponibile.");
        dynamic service = Activator.CreateInstance(type)!;
        try
        {
            service.Connect();
            dynamic folder = service.GetFolder(folderPath);
            return folder.GetTask(name);
        }
        catch (COMException ex) when (ex.HResult is FileNotFound or PathNotFound)
        {
            return null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(service);
        }
    }
}
