using Microsoft.Extensions.Logging.Abstractions;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Backup;
using PcOptimizer.Core.Journal;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Tweaks.Catalog;
using PcOptimizer.Tweaks.Context;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Tests;

/// <summary>Regole che valgono per l'intero catalogo, su un sistema "carico" di software da ottimizzare.</summary>
public class CatalogSafetyTests
{
    private static (TweakContext Context, IReadOnlyList<CatalogTweak> Tweaks) Busy()
    {
        var snapshot = Fixtures.Snapshot() with
        {
            Services =
            [
                .. Fixtures.Snapshot().Services,
                Fixtures.Service("gupdate", ServiceStartValues.AutomaticDelayed, "Google Update"),
                Fixtures.Service("edgeupdate", ServiceStartValues.AutomaticDelayed),
                Fixtures.Service("Steam Client Service", ServiceStartValues.Automatic),
                Fixtures.Service("SmartByte Network Service x64", ServiceStartValues.Automatic),
                Fixtures.Service("LightingService", ServiceStartValues.Automatic, "ASUS Lighting"),
                Fixtures.Service("EasyAntiCheat_EOS", ServiceStartValues.Automatic),
                Fixtures.Service("vgc", ServiceStartValues.Automatic),
                Fixtures.Service("AdobeARMservice", ServiceStartValues.Automatic),
            ],
            Startup =
            [
                .. Fixtures.Snapshot().Startup,
                new StartupEntry { Name = "Discord", Source = StartupSource.RegistryUser, Enabled = true, ApprovalName = "Discord", Command = "Update.exe --processStart Discord.exe" },
                new StartupEntry { Name = "Steam", Source = StartupSource.RegistryUser, Enabled = true, ApprovalName = "Steam", Command = "steam.exe -silent" },
                new StartupEntry { Name = "MiaVPN", Source = StartupSource.StartupFolderUser, Enabled = true, ApprovalName = "MiaVPN.lnk", Company = "Vpn Srl" },
                new StartupEntry { Name = "SpotifyAB.SpotifyMusic", Source = StartupSource.StoreApp, Enabled = true, ApprovalName = @"SpotifyAB.SpotifyMusic_zpdnekdrzrea0\Spotify", StoreTaskState = 2 },
            ],
            ScheduledTasks =
            [
                .. Fixtures.Snapshot().ScheduledTasks,
                new ScheduledTaskEntry { Path = @"\GoogleUpdateTaskMachineCore", Enabled = true },
                new ScheduledTaskEntry { Path = @"\MicrosoftEdgeUpdateTaskMachineUA", Enabled = true },
                new ScheduledTaskEntry { Path = @"\Adobe Acrobat Update Task", Enabled = true, Triggers = ["Accesso"] },
            ],
            PciDevices =
            [
                Fixtures.Pci("NVIDIA GeForce RTX 3060", Fixtures.DisplayClass, 16),
                Fixtures.Pci("Controller NVMe", Fixtures.StorageClass, 17),
            ],
            Registry = new Dictionary<string, SettingValue>(Fixtures.Snapshot().Registry)
            {
                ["memory.largesystemcache"] = SettingValue.Of(1u),
                ["placebo.irq8priority"] = SettingValue.Of(1u),
            },
            Timer = new TimerSnapshot { BcdReadable = true, BcdElements = new Dictionary<string, string> { ["useplatformclock"] = "Yes" } },
        };
        var games = new GameLibrary([new GameEntry("Counter-Strike 2", @"C:\Games\cs2\game\bin\win64\cs2.exe")]);
        var context = Fixtures.Context(snapshot: snapshot, games: games,
            profile: Fixtures.Profile(cpu: "Intel(R) Core(TM) i7-13700K", pCores: 8, eCores: 8));
        return (context, TweakCatalog.Build(context));
    }

    [Fact]
    public void IdsAreUniqueAndTextsComplete()
    {
        var (_, tweaks) = Busy();

        Assert.True(tweaks.Count > 40);
        Assert.Equal(tweaks.Count, tweaks.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(tweaks, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Title));
            Assert.False(string.IsNullOrWhiteSpace(t.WhatChanges));
            Assert.False(string.IsNullOrWhiteSpace(t.Why));
            Assert.False(string.IsNullOrWhiteSpace(t.ExpectedBenefit));
            Assert.All(t.MeasuredBy, m => Assert.NotNull(PcOptimizer.Benchmark.Comparison.MetricCatalog.Find(m)));
        });
    }

    [Fact]
    public void ApplicableTweaksAlwaysPlanChanges()
    {
        var (context, tweaks) = Busy();
        foreach (var t in tweaks.Where(t => t.IsApplicable(context, out _)))
        {
            Assert.True(t.PlanChanges(context).Count > 0, t.Id);
        }
    }

    [Fact]
    public void NeverTouchesProtectedServicesSecurityOrUpdates()
    {
        var (context, tweaks) = Busy();
        var targets = tweaks.SelectMany(t => t.PlanChanges(context)).Select(c => c.Target).ToList();

        foreach (var service in targets.OfType<ServiceStartTarget>())
        {
            Assert.False(ServiceTweaks.IsProtected(service.ServiceName), service.ServiceName);
        }

        Assert.DoesNotContain(targets.OfType<ServiceStartTarget>(), s => s.ServiceName is "gupdate" or "edgeupdate" or "Steam Client Service" or "vgc" or "EasyAntiCheat_EOS" or "WinDefend" or "wuauserv");
        Assert.DoesNotContain(targets.OfType<ScheduledTaskTarget>(), t => t.TaskPath.Contains("Google", StringComparison.OrdinalIgnoreCase) || t.TaskPath.Contains("Edge", StringComparison.OrdinalIgnoreCase) || t.TaskPath.Contains("Defrag", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(targets.OfType<RegistryValueTarget>(), r =>
            r.KeyPath.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase) ||
            r.KeyPath.Contains("WindowsUpdate", StringComparison.OrdinalIgnoreCase) ||
            r.KeyPath.Contains(@"\Services\WinDefend", StringComparison.OrdinalIgnoreCase));
        Assert.All(targets.OfType<FileContentTarget>(), f => Assert.EndsWith(".cmd", f.FilePath));
    }

    [Fact]
    public void SecurityReducingTweaksAreNeverPreselected()
    {
        var (context, tweaks) = Busy();
        var recommended = TweakCatalog.Recommended(tweaks, context);

        Assert.NotEmpty(recommended);
        Assert.DoesNotContain(recommended, t => t.AffectsSecurity);
        Assert.All(tweaks.Where(t => t.AffectsSecurity), t => Assert.False(string.IsNullOrWhiteSpace(t.SecurityWarning)));
        Assert.All(recommended, t => Assert.True(t.AppliesTo.Includes(context.Tier)));
    }

    [Fact]
    public void WholeCatalogAppliesAndRevertsThroughTheEngine()
    {
        var (context, tweaks) = Busy();
        var root = Path.Combine(Path.GetTempPath(), "pcopt-tweaks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new InMemoryProvider();
            var engine = new TweakEngine(new SettingStore([provider]), new ChangeJournal(Path.Combine(root, "journal.jsonl")),
                new BackupService(Path.Combine(root, "backups")), NullLogger<TweakEngine>.Instance);

            var applied = 0;
            foreach (var tweak in tweaks.Where(t => t.IsApplicable(context, out _)))
            {
                var result = engine.Apply(tweak, context);
                Assert.Equal(TweakStatus.Applied, result.StatusAfter);
                applied++;
            }

            Assert.True(applied > 30);
            Assert.NotEmpty(provider.Values);

            var report = engine.RevertAll();
            Assert.True(report.Success);
            Assert.Empty(provider.Values); // il sistema finto partiva vuoto: tutto deve tornare "assente"
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
