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
}
