using System.Text.Json;
using PcOptimizer.Core.Parsing;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Tests;

public sealed class SettingTargetTests
{
    [Fact]
    public void Registry_targets_compare_case_insensitively_like_windows()
    {
        var a = new RegistryValueTarget(RegHive.LocalMachine, @"SOFTWARE\Foo", "Bar", RegValueKind.DWord);
        var b = new RegistryValueTarget(RegHive.LocalMachine, @"software\foo", "BAR", RegValueKind.DWord);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual<SettingTarget>(a, a with { Hive = RegHive.CurrentUser });
    }

    [Fact]
    public void Service_and_power_targets_ignore_descriptive_fields()
    {
        Assert.Equal(new ServiceStartTarget("SysMain"), new ServiceStartTarget("sysmain"));

        var g = Guid.NewGuid();
        Assert.Equal(
            new PowerSettingTarget(g, g, g, PowerSource.Ac, "uno"),
            new PowerSettingTarget(g, g, g, PowerSource.Ac, "due"));
    }

    [Fact]
    public void Values_round_trip()
    {
        Assert.Equal(4294967295u, SettingValue.Of(4294967295u).AsUInt32());
        Assert.Equal(["a", "", "b"], SettingValue.OfMulti(["a", "", "b"]).AsMulti());
        Assert.Equal(new byte[] { 0, 255 }, SettingValue.OfBinary([0, 255]).AsBinary());
        Assert.Throws<InvalidOperationException>(() => SettingValue.Absent.AsUInt32());
    }

    [Fact]
    public void New_targets_survive_the_journal_json()
    {
        SettingTarget[] targets =
        [
            new ScheduledTaskTarget(@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser"),
            new BcdElementTarget("useplatformclock"),
            new ActivePowerSchemeTarget(),
            new PowerOverlayTarget(),
            new DisplayRefreshTarget(@"\\.\DISPLAY1"),
            new FileContentTarget(@"C:\Users\Public\Desktop\Gioco (P-core).cmd"),
        ];

        foreach (var target in targets)
        {
            var json = JsonSerializer.Serialize(target);
            Assert.Equal(target, JsonSerializer.Deserialize<SettingTarget>(json));
        }

        Assert.Equal(new ActivePowerSchemeTarget(), new ActivePowerSchemeTarget());
        Assert.Equal(new ScheduledTaskTarget(@"\A\B"), new ScheduledTaskTarget(@"\a\b"));
        Assert.Equal(new BcdElementTarget("UsePlatformClock"), new BcdElementTarget("useplatformclock"));
    }

    [Theory]
    [InlineData("Sì", "Yes")]
    [InlineData("no", "No")]
    [InlineData("Enhanced ", "Enhanced")]
    public void Bcd_values_are_normalized(string raw, string expected) => Assert.Equal(expected, BcdParser.Normalize(raw));

    [Fact]
    public void File_provider_only_handles_cmd_scripts()
    {
        var provider = new FileContentProvider();
        var dir = Path.Combine(Path.GetTempPath(), "pcopt-file-" + Guid.NewGuid().ToString("N"));
        var script = new FileContentTarget(Path.Combine(dir, "gioco.cmd"));
        try
        {
            Assert.Equal(SettingValue.Absent, provider.Read(script));
            provider.Write(script, SettingValue.Of("@echo off\r\nstart \"\" gioco.exe\r\n"));
            Assert.Equal("@echo off\r\nstart \"\" gioco.exe\r\n", provider.Read(script).Data);
            Assert.NotEqual(0xEF, File.ReadAllBytes(script.FilePath)[0]);
            provider.Write(script, SettingValue.Absent);
            Assert.False(File.Exists(script.FilePath));

            Assert.Throws<InvalidOperationException>(() => provider.Write(new FileContentTarget(Path.Combine(dir, "x.dll")), SettingValue.Of("x")));
            Assert.Throws<InvalidOperationException>(() => provider.Read(new FileContentTarget("relativo.cmd")));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
