using Microsoft.Extensions.Logging;
using PcOptimizer.Analysis;
using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Evaluation;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Analysis.Windows;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Hardware.Assessment;
using PcOptimizer.Hardware.Database;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Hardware.Windows;
using PcOptimizer.Tweaks;
using PcOptimizer.Tweaks.Context;
using PcOptimizer.Tweaks.Model;
using PcOptimizer.Tweaks.Windows;

namespace PcOptimizer.Cli;

/// <summary>Scansione, analisi e contesto degli interventi: i passi comuni a "plan", "apply" e "session".</summary>
internal sealed record Inspection(
    HardwareProfile Profile,
    HardwareAssessment Assessment,
    SystemSnapshot Snapshot,
    SystemReport Report,
    TweakContext Context,
    IReadOnlyList<CatalogTweak> Tweaks)
{
    public static async Task<Inspection> RunAsync(ILoggerFactory loggers, string[] dataDirs, GameLibrary games)
    {
        Console.WriteLine("Scansione dell'hardware e analisi della configurazione in corso...");
        var profile = await new WindowsHardwareScanner(loggers.CreateLogger<WindowsHardwareScanner>()).ScanAsync();
        var assessment = new HardwareAnalyzer(HardwareDatabase.Load(dataDirs)).Assess(profile);
        var software = KnownSoftwareCatalog.Load(dataDirs);
        var snapshot = await new WindowsSystemCollector(loggers.CreateLogger<WindowsSystemCollector>()).CollectAsync();
        var report = new SystemAnalyzer(software).Analyze(snapshot, AnalysisContext.From(profile, assessment));
        var environment = new TweakEnvironmentCollector(loggers.CreateLogger<TweakEnvironmentCollector>()).Collect(profile, games);
        var context = TweakCatalog.CreateContext(profile, assessment, snapshot, report, environment, software, games);
        return new Inspection(profile, assessment, snapshot, report, context, TweakCatalog.Build(context));
    }
}
