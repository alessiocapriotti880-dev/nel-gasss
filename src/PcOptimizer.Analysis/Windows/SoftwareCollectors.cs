using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Win32;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Analysis.Parsing;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Windows;

namespace PcOptimizer.Analysis.Windows;

/// <summary>Produttore dei file eseguibili (informazioni di versione), con cache per percorso.</summary>
[SupportedOSPlatform("windows")]
internal sealed class FileInfoCache
{
    private readonly Dictionary<string, string?> _companies = new(StringComparer.OrdinalIgnoreCase);

    public string? Company(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (_companies.TryGetValue(path, out var cached))
        {
            return cached;
        }

        string? company = null;
        try
        {
            if (File.Exists(path))
            {
                company = FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // File non leggibile: produttore sconosciuto.
        }

        return _companies[path] = string.IsNullOrEmpty(company) ? null : company;
    }
}

/// <summary>Servizi Win32 con tipo di avvio, stato e produttore.</summary>
[SupportedOSPlatform("windows")]
internal static class ServiceCollector
{
    public static IReadOnlyList<ServiceEntry> Collect(string systemRoot, FileInfoCache files)
    {
        var starts = new ServiceStartProvider();
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

        return Wmi.Query(Wmi.Cimv2, "SELECT Name, DisplayName, StartMode, State, PathName FROM Win32_Service")
            .Select(row =>
            {
                var name = row.Str("Name") ?? "";
                var image = ImagePathParser.ExecutablePath(Expand(row.Str("PathName")), systemRoot);

                // I servizi ospitati da svchost sono DLL: il produttore si legge da quella.
                if (image?.EndsWith("svchost.exe", StringComparison.OrdinalIgnoreCase) == true)
                {
                    image = ServiceDll(hklm, name) ?? image;
                }

                return new ServiceEntry
                {
                    Name = name,
                    DisplayName = row.Str("DisplayName") ?? name,
                    StartMode = StartMode(starts, name, row.Str("StartMode")),
                    State = row.Str("State") ?? "",
                    ImagePath = image,
                    Company = files.Company(image),
                };
            })
            .ToList();
    }

    // Il registro distingue anche l'avvio ritardato, che WMI riporta come semplice "Auto".
    private static string StartMode(ServiceStartProvider starts, string name, string? wmiStartMode)
    {
        try
        {
            if (starts.Read(new ServiceStartTarget(name)) is { Exists: true, Data: { } mode })
            {
                return mode;
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // Si usa il valore di WMI.
        }

        return wmiStartMode switch
        {
            "Auto" => ServiceStartValues.Automatic,
            "Manual" => ServiceStartValues.Manual,
            "Disabled" => ServiceStartValues.Disabled,
            "Boot" => ServiceStartValues.Boot,
            "System" => ServiceStartValues.System,
            _ => wmiStartMode ?? "",
        };
    }

    private static string? ServiceDll(RegistryKey hklm, string serviceName)
    {
        try
        {
            using var parameters = hklm.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}\Parameters");
            using var service = hklm.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return (parameters?.GetValue("ServiceDll") ?? service?.GetValue("ServiceDll")) as string;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static string? Expand(string? path) => path is null ? null : Environment.ExpandEnvironmentVariables(path);
}

/// <summary>Voci di avvio automatico, con lo stato abilitato/disabilitato di Gestione attività.</summary>
[SupportedOSPlatform("windows")]
internal static class StartupCollector
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    private const string StoreTasksKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

    public static IReadOnlyList<StartupEntry> Collect(string systemRoot, FileInfoCache files)
    {
        var list = new List<StartupEntry>();
        ReadRunKey(list, RegistryHive.LocalMachine, RegistryView.Registry64, StartupSource.RegistryMachine, "Run", systemRoot, files);
        ReadRunKey(list, RegistryHive.LocalMachine, RegistryView.Registry32, StartupSource.RegistryMachine32, "Run32", systemRoot, files);
        ReadRunKey(list, RegistryHive.CurrentUser, RegistryView.Registry64, StartupSource.RegistryUser, "Run", systemRoot, files);
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.StartupFolderCommon, RegistryHive.LocalMachine, files);
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.StartupFolderUser, RegistryHive.CurrentUser, files);
        ReadStoreApps(list);
        return list;
    }

    private static void ReadRunKey(
        List<StartupEntry> list, RegistryHive hive, RegistryView view, StartupSource source, string approvedName,
        string systemRoot, FileInfoCache files)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var run = root.OpenSubKey(RunKey);
        if (run is null)
        {
            return;
        }

        // StartupApproved sta sempre nella vista a 64 bit, anche per le voci a 32 bit (Run32).
        using var approvedRoot = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var approved = approvedRoot.OpenSubKey(ApprovedKey + approvedName);

        foreach (var name in run.GetValueNames().Where(n => n.Length > 0))
        {
            var command = run.GetValue(name) as string;
            var exe = ImagePathParser.ExecutablePath(ServiceCollector.Expand(command), systemRoot);
            list.Add(new StartupEntry
            {
                Name = name,
                Source = source,
                Command = command,
                ExecutablePath = exe,
                Company = files.Company(exe),
                Enabled = StartupStateParser.IsApprovedEnabled(approved?.GetValue(name) as byte[]),
            });
        }
    }

    private static void ReadFolder(List<StartupEntry> list, string folder, StartupSource source, RegistryHive hive, FileInfoCache files)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var approved = root.OpenSubKey(ApprovedKey + "StartupFolder");

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (command, target) = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ResolveShortcut(file) : (file, file);
            list.Add(new StartupEntry
            {
                Name = Path.GetFileNameWithoutExtension(file),
                Source = source,
                Command = command,
                ExecutablePath = target,
                Company = files.Company(target),
                Enabled = StartupStateParser.IsApprovedEnabled(approved?.GetValue(name) as byte[]),
            });
        }
    }

    /// <summary>Destinazione di un collegamento tramite WScript.Shell (sola lettura).</summary>
    private static (string Command, string? Target) ResolveShortcut(string path)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null)
            {
                return (path, null);
            }

            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(path);
            string target = link.TargetPath;
            string arguments = link.Arguments;
            return (string.IsNullOrEmpty(arguments) ? target : $"{target} {arguments}", string.IsNullOrEmpty(target) ? null : target);
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException or InvalidCastException)
        {
            return (path, null);
        }
    }

    /// <summary>Attività di avvio delle app dello Store (StartupTaskState in SystemAppData).</summary>
    private static void ReadStoreApps(List<StartupEntry> list)
    {
        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var root = hkcu.OpenSubKey(StoreTasksKey);
        if (root is null)
        {
            return;
        }

        foreach (var package in root.GetSubKeyNames())
        {
            using var packageKey = root.OpenSubKey(package);
            foreach (var task in packageKey?.GetSubKeyNames() ?? [])
            {
                using var taskKey = packageKey!.OpenSubKey(task);
                if (taskKey?.GetValue("State") is int state)
                {
                    list.Add(new StartupEntry
                    {
                        Name = package.Split('_')[0],
                        Source = StartupSource.StoreApp,
                        Command = $"{package}!{task}",
                        Enabled = StartupStateParser.IsStoreTaskEnabled(state),
                    });
                }
            }
        }
    }
}

/// <summary>Attività pianificate tramite l'API COM dell'Utilità di pianificazione (sola lettura).</summary>
[SupportedOSPlatform("windows")]
internal static class TaskCollector
{
    private const int TaskEnumHidden = 1;
    private const int TaskActionExec = 0;

    public static IReadOnlyList<ScheduledTaskEntry> Collect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
                   ?? throw new InvalidOperationException("Utilità di pianificazione non disponibile.");
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();

        var result = new List<ScheduledTaskEntry>();
        object root = service.GetFolder(@"\");
        Walk(root, result);
        return result;
    }

    // I parametri sono object (e non dynamic) perché le chiamate ai nostri metodi restino tipizzate.
    private static void Walk(object folderObject, List<ScheduledTaskEntry> result)
    {
        dynamic folder = folderObject;
        dynamic tasks = folder.GetTasks(TaskEnumHidden);
        int count = tasks.Count;
        for (var i = 1; i <= count; i++)
        {
            try
            {
                object task = tasks.Item(i);
                result.Add(ReadTask(task));
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or RuntimeBinderException)
            {
                // Attività non leggibile senza privilegi: si salta.
            }
        }

        dynamic folders = folder.GetFolders(0);
        int folderCount = folders.Count;
        for (var i = 1; i <= folderCount; i++)
        {
            try
            {
                object subfolder = folders.Item(i);
                Walk(subfolder, result);
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or RuntimeBinderException)
            {
                // Cartella non accessibile: si salta.
            }
        }
    }

    private static ScheduledTaskEntry ReadTask(object taskObject)
    {
        dynamic task = taskObject;
        string path = task.Path;
        bool enabled = task.Enabled;
        int state = task.State;

        string? executable = null, author = null;
        var triggers = new List<string>();
        try
        {
            dynamic definition = task.Definition;
            author = definition.RegistrationInfo.Author;

            dynamic actions = definition.Actions;
            int actionCount = actions.Count;
            for (var i = 1; i <= actionCount && executable is null; i++)
            {
                dynamic action = actions.Item(i);
                if ((int)action.Type == TaskActionExec)
                {
                    executable = action.Path;
                }
            }

            dynamic taskTriggers = definition.Triggers;
            int triggerCount = taskTriggers.Count;
            for (var i = 1; i <= triggerCount; i++)
            {
                triggers.Add(TriggerName((int)taskTriggers.Item(i).Type));
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or RuntimeBinderException)
        {
            // Definizione non leggibile: restano percorso e stato.
        }

        return new ScheduledTaskEntry
        {
            Path = path,
            Enabled = enabled,
            State = state,
            Triggers = triggers,
            Executable = executable,
            Author = author,
        };
    }

    private static string TriggerName(int type) => type switch
    {
        0 => "Evento",
        1 => "Orario",
        2 => "Giornaliero",
        3 => "Settimanale",
        4 or 5 => "Mensile",
        6 => "Inattività",
        7 => "Registrazione",
        8 => "Avvio del sistema",
        9 => "Accesso",
        11 => "Cambio di sessione",
        _ => "Personalizzato",
    };
}

/// <summary>Driver kernel installati e driver dei dispositivi principali.</summary>
[SupportedOSPlatform("windows")]
internal static class DriverCollector
{
    public static IReadOnlyList<KernelDriverEntry> CollectKernel(string systemRoot, FileInfoCache files) =>
        Wmi.Query(Wmi.Cimv2, "SELECT Name, DisplayName, PathName, State, StartMode FROM Win32_SystemDriver")
            .Select(row =>
            {
                var path = ImagePathParser.ExecutablePath(ServiceCollector.Expand(row.Str("PathName")), systemRoot);
                return new KernelDriverEntry
                {
                    Name = row.Str("Name") ?? "",
                    DisplayName = row.Str("DisplayName") ?? "",
                    FilePath = path,
                    State = row.Str("State") ?? "",
                    StartMode = row.Str("StartMode") ?? "",
                    Company = files.Company(path),
                };
            })
            .ToList();

    public static IReadOnlyList<DeviceDriverEntry> CollectDevices() =>
        Wmi.Query(Wmi.Cimv2,
                "SELECT DeviceName, DeviceClass, DriverVersion, DriverDate, DriverProviderName, IsSigned FROM Win32_PnPSignedDriver " +
                "WHERE DeviceClass = 'NET' OR DeviceClass = 'MEDIA' OR DeviceClass = 'DISPLAY' OR IsSigned = FALSE")
            .Where(row => row.Str("DeviceName") is not null)
            .Select(row => new DeviceDriverEntry
            {
                DeviceName = row.Str("DeviceName")!,
                DeviceClass = row.Str("DeviceClass") ?? "",
                Version = row.Str("DriverVersion"),
                Date = row.CimDate("DriverDate"),
                Provider = row.Str("DriverProviderName"),
                IsSigned = row.Bool("IsSigned"),
            })
            .ToList();
}

/// <summary>
/// Processi in esecuzione: nome, memoria e sessione arrivano dall'istantanea di sistema
/// (NtQuerySystemInformation), senza aprire handle sui processi, quindi senza interagire
/// con giochi o anti-cheat.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ProcessCollector
{
    public static IReadOnlyList<ProcessEntry> Collect()
    {
        var result = new List<ProcessEntry>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    result.Add(new ProcessEntry
                    {
                        Name = process.ProcessName,
                        Id = process.Id,
                        SessionId = process.SessionId,
                        WorkingSetBytes = process.WorkingSet64,
                        PrivateBytes = process.PrivateMemorySize64,
                    });
                }
                catch (InvalidOperationException)
                {
                    // Processo terminato durante la lettura.
                }
            }
        }

        return result;
    }
}
