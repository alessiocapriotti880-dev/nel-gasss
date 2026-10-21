using Microsoft.Extensions.Logging.Abstractions;
using PcOptimizer.Optimization.Windows;

namespace PcOptimizer.Optimization.Tests;

/// <summary>Attività pianificata di ripresa: creata e cancellata davvero (solo su Windows, in CI come amministratore).</summary>
public class WindowsResumeTests
{
    [Fact]
    public void ScheduleAndCancel()
    {
        if (!OperatingSystem.IsWindows() || !new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
        {
            return;
        }

        var scheduler = new ScheduledTaskResumeScheduler(@"C:\Windows\System32\cmd.exe", "/c exit 0", NullLogger<ScheduledTaskResumeScheduler>.Instance);
        scheduler.Schedule();
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service")!;
            dynamic service = Activator.CreateInstance(type)!;
            service.Connect();
            dynamic task = service.GetFolder(@"\" + ScheduledTaskResumeScheduler.FolderName).GetTask(ScheduledTaskResumeScheduler.TaskName);
            Assert.Equal(1, (int)task.Definition.Principal.RunLevel);
        }
        finally
        {
            scheduler.Cancel();
        }

        scheduler.Cancel(); // seconda cancellazione: nessun errore
    }
}
