using PcOptimizer.Core.Backup;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Tests;

public sealed class RegFileWriterTests
{
    private static RegistryValueTarget T(string name, RegValueKind kind, string key = @"SYSTEM\Test") =>
        new(RegHive.LocalMachine, key, name, kind);

    [Fact]
    public void Formats_every_value_kind_like_regedit()
    {
        var text = RegFileWriter.Build(
        [
            (T("Dw", RegValueKind.DWord), SettingValue.Of(0xFFFFFFFFu)),
            (T("Qw", RegValueKind.QWord), SettingValue.Of(1ul)),
            (T("Path", RegValueKind.String), SettingValue.Of(@"C:\a ""b""")),
            (T("Exp", RegValueKind.ExpandString), SettingValue.Of("%A%")),
            (T("Multi", RegValueKind.MultiString), SettingValue.OfMulti(["x", "y"])),
            (T("Bin", RegValueKind.Binary), SettingValue.OfBinary([0x01, 0xAB])),
            (T("Gone", RegValueKind.DWord), SettingValue.Absent),
            (T("", RegValueKind.String), SettingValue.Of("def")),
        ]);

        var expected = string.Join("\r\n",
            "Windows Registry Editor Version 5.00",
            "",
            @"[HKEY_LOCAL_MACHINE\SYSTEM\Test]",
            "\"Dw\"=dword:ffffffff",
            "\"Qw\"=hex(b):01,00,00,00,00,00,00,00",
            @"""Path""=""C:\\a \""b\""""",
            "\"Exp\"=hex(2):25,00,41,00,25,00,00,00",
            "\"Multi\"=hex(7):78,00,00,00,79,00,00,00,00,00",
            "\"Bin\"=hex:01,ab",
            "\"Gone\"=-",
            "@=\"def\"",
            "");
        Assert.Equal(expected, text);
    }

    [Fact]
    public void Groups_values_by_key_case_insensitively()
    {
        var text = RegFileWriter.Build(
        [
            (T("A", RegValueKind.DWord, @"SYSTEM\K"), SettingValue.Of(1u)),
            (T("B", RegValueKind.DWord, @"system\k"), SettingValue.Of(2u)),
        ]);

        Assert.Single(text.Split('[').Skip(1));
    }
}
