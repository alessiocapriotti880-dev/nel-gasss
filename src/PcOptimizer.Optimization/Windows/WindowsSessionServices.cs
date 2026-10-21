using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using PcOptimizer.Benchmark;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Windows;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Optimization.Session;
using PcOptimizer.Tweaks.Context;
using PcOptimizer.Tweaks.Windows;

namespace PcOptimizer.Optimization.Windows;

/// <summary>Misure della sessione con il modulo Benchmark (latenza a riposo; rete se servono a qualche intervento).</summary>
[SupportedOSPlatform("windows")]
public sealed class BenchmarkMeasurer(BenchmarkRunner runner, BenchmarkOptions options, IProgress<string>? progress = null) : IMeasurer
{
    public Task<BenchmarkRun> MeasureAsync(string label, CancellationToken cancellationToken) =>
        runner.RunAsync(options with { Label = label, Boot = false }, progress, cancellationToken);
}

/// <summary>Aggiorna nel contesto il piano energetico attivo dopo che un intervento lo ha cambiato.</summary>
[SupportedOSPlatform("windows")]
public sealed class PowerContextRefresher : IContextRefresher
{
    public void Refresh(TweakContext context)
    {
        if (context.Get<TweakEnvironment>() is { } environment)
        {
            context.With(environment with { ActivePowerScheme = TweakEnvironmentCollector.ActiveScheme() });
        }
    }
}

/// <summary>
/// Ripresa dopo il riavvio con un'attività pianificata all'accesso dell'utente, con privilegi elevati (le chiavi Run
/// non avviano programmi che richiedono l'amministratore). L'attività si cancella alla ripresa o con "Ripristina tutto".
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScheduledTaskResumeScheduler(string executable, string arguments, ILogger<ScheduledTaskResumeScheduler> log) : IResumeScheduler
{
    public const string FolderName = "PcOptimizerPro";
    public const string TaskName = "Ripresa sessione";
    private const int TriggerLogon = 9;
    private const int ActionExec = 0;
    private const int CreateOrUpdate = 6;
    private const int LogonInteractiveToken = 3;
    private const int RunLevelHighest = 1;

    public void Schedule()
    {
        dynamic service = Connect();
        try
        {
            dynamic folder = Folder(service, create: true)!;
            var user = WindowsIdentity.GetCurrent().Name;
            dynamic task = service.NewTask(0);
            task.RegistrationInfo.Description = "Riprende la sessione di PC Optimizer Pro dopo il riavvio (misura e decide quali interventi tenere). Si cancella da sola.";
            task.Principal.UserId = user;
            task.Principal.LogonType = LogonInteractiveToken;
            task.Principal.RunLevel = RunLevelHighest;
            task.Settings.DisallowStartIfOnBatteries = false;
            task.Settings.StopIfGoingOnBatteries = false;
            task.Settings.ExecutionTimeLimit = "PT2H";
            dynamic trigger = task.Triggers.Create(TriggerLogon);
            trigger.UserId = user;
            trigger.Delay = "PT30S";
            dynamic action = task.Actions.Create(ActionExec);
            action.Path = executable;
            action.Arguments = arguments;
            folder.RegisterTaskDefinition(TaskName, task, CreateOrUpdate, null, null, LogonInteractiveToken);
            log.LogInformation("Ripresa programmata all'accesso: {Exe} {Args}", executable, arguments);
        }
        finally
        {
            Marshal.FinalReleaseComObject(service);
        }
    }

    public void Cancel()
    {
        try
        {
            dynamic service = Connect();
            try
            {
                Folder(service, create: false)?.DeleteTask(TaskName, 0);
            }
            finally
            {
                Marshal.FinalReleaseComObject(service);
            }
        }
        catch (COMException ex)
        {
            log.LogDebug(ex, "Nessuna attività di ripresa da cancellare");
        }
    }

    private static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new NotSupportedException("Utilità di pianificazione non disponibile.");
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service;
    }

    private static dynamic? Folder(dynamic service, bool create)
    {
        dynamic root = service.GetFolder("\\");
        try
        {
            return root.GetFolder("\\" + FolderName);
        }
        catch (COMException) when (create)
        {
            return root.CreateFolder(FolderName);
        }
        catch (COMException)
        {
            return null;
        }
    }
}
