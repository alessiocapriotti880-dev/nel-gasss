using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using PcOptimizer.Core.Backup;
using PcOptimizer.Core.Journal;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tests.Fakes;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Core.Windows;

namespace PcOptimizer.Core.Tests;

/// <summary>
/// Prove dei provider reali: girano solo su Windows (in CI su windows-latest), altrove terminano subito.
/// Scrivono solo sotto una chiave di prova di HKCU, che viene cancellata; il resto è in sola lettura.
/// </summary>
public sealed class WindowsProviderTests
{
    private static readonly string TestKey = $@"Software\PcOptimizerProTest\{Guid.NewGuid():N}";

    [Fact]
    public void Registry_round_trip_on_a_test_key()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var provider = new RegistrySettingProvider();
        try
        {
            (RegValueKind Kind, SettingValue Value)[] cases =
            [
                (RegValueKind.DWord, SettingValue.Of(4294967295u)),
                (RegValueKind.QWord, SettingValue.Of(18446744073709551615ul)),
                (RegValueKind.String, SettingValue.Of("SwapEffectUpgradeEnable=1;")),
                (RegValueKind.MultiString, SettingValue.OfMulti([@"?:\pagefile.sys"])),
                (RegValueKind.Binary, SettingValue.OfBinary([3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])),
            ];
            foreach (var (kind, value) in cases)
            {
                var target = new RegistryValueTarget(RegHive.CurrentUser, TestKey, $"v{kind}", kind);
                Assert.Equal(SettingValue.Absent, provider.Read(target));
                provider.Write(target, value);
                Assert.Equal(value, provider.Read(target));
                provider.Write(target, SettingValue.Absent);
                Assert.Equal(SettingValue.Absent, provider.Read(target));
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\PcOptimizerProTest", throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void Engine_applies_and_reverts_with_the_real_store()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "pcopt-win-" + Guid.NewGuid().ToString("N"));
        var target = new RegistryValueTarget(RegHive.CurrentUser, TestKey, "Prova", RegValueKind.DWord);
        try
        {
            var engine = new TweakEngine(WindowsSettingProviders.CreateStore(), new ChangeJournal(Path.Combine(root, "j.jsonl")),
                new BackupService(Path.Combine(root, "b")), NullLogger<TweakEngine>.Instance);
            var tweak = new FakeTweak("prova.windows", [new SettingChange(target, SettingValue.Of(1u))]);
            var context = new TweakContext(HardwareTier.Mid, Environment.OSVersion.Version.Build);

            var result = engine.Apply(tweak, context);
            Assert.Equal(TweakStatus.Applied, result.StatusAfter);
            Assert.True(File.Exists(Path.Combine(result.BackupFolder!, "original.reg")));

            engine.Revert(result.TransactionId!.Value);
            Assert.Equal(TweakStatus.NotApplied, engine.Detect(tweak, context));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\PcOptimizerProTest", throwOnMissingSubKey: false);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Read_only_providers_work_on_this_system()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var scheme = new ActivePowerSchemeProvider().Read(new ActivePowerSchemeTarget());
        Assert.True(Guid.TryParse(scheme.Data, out var active));
        // Riscrivere lo stesso piano non cambia nulla ma prova la scrittura.
        new ActivePowerSchemeProvider().Write(new ActivePowerSchemeTarget(), scheme);
        Assert.Equal(active, PowerSettingProvider.ActiveScheme());

        var minState = new PowerSettingProvider().Read(new PowerSettingTarget(active, PowerGuids.SubProcessor, PowerGuids.ProcessorMinState, PowerSource.Ac, "min"));
        Assert.True(minState.Exists);

        Assert.True(new ServiceStartProvider().Read(new ServiceStartTarget("wuauserv")).Exists);
        Assert.False(new ServiceStartProvider().Read(new ServiceStartTarget("ServizioInesistente123")).Exists);

        var tasks = new ScheduledTaskProvider();
        Assert.False(tasks.Read(new ScheduledTaskTarget(@"\Microsoft\Windows\NonEsiste\Attività")).Exists);
        var defrag = tasks.Read(new ScheduledTaskTarget(@"\Microsoft\Windows\Defrag\ScheduledDefrag"));
        Assert.True(!defrag.Exists || defrag.Data is ScheduledTaskValues.Enabled or ScheduledTaskValues.Disabled);

        var bcd = new BcdProvider().Read(new BcdElementTarget("useplatformclock"));
        Assert.True(!bcd.Exists || bcd.Data is "Yes" or "No");

        _ = DisplayRefreshProvider.AvailableRefreshRates(@"\\.\DISPLAY1");
        _ = PowerOverlayProvider.IsSupported;
    }
}
