using System.Runtime.Versioning;
using Microsoft.Win32;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Windows;

/// <summary>
/// Legge e scrive valori di registro. Usa sempre la vista a 64 bit, così il risultato
/// non dipende da come è compilato il processo.
/// Scrive solo valori: non cancella mai chiavi intere (al massimo le crea vuote se mancano).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistrySettingProvider : ISettingProvider
{
    public bool CanHandle(SettingTarget target) => target is RegistryValueTarget;

    public SettingValue Read(SettingTarget target)
    {
        var t = (RegistryValueTarget)target;
        using var root = OpenRoot(t.Hive);
        using var key = root.OpenSubKey(t.KeyPath, writable: false);
        if (key is null)
        {
            return SettingValue.Absent;
        }

        var raw = key.GetValue(t.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null)
        {
            return SettingValue.Absent;
        }

        return t.Kind switch
        {
            // GetValue restituisce int/long con segno: li riconvertiamo nella rappresentazione senza segno.
            RegValueKind.DWord => SettingValue.Of(unchecked((uint)Convert.ToInt32(raw))),
            RegValueKind.QWord => SettingValue.Of(unchecked((ulong)Convert.ToInt64(raw))),
            RegValueKind.String or RegValueKind.ExpandString => SettingValue.Of(raw.ToString()!),
            RegValueKind.MultiString => SettingValue.OfMulti((string[])raw),
            RegValueKind.Binary => SettingValue.OfBinary((byte[])raw),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        var t = (RegistryValueTarget)target;
        using var root = OpenRoot(t.Hive);

        if (!value.Exists)
        {
            using var existing = root.OpenSubKey(t.KeyPath, writable: true);
            existing?.DeleteValue(t.ValueName, throwOnMissingValue: false);
            return;
        }

        using var key = root.CreateSubKey(t.KeyPath, writable: true)
            ?? throw new InvalidOperationException($"Impossibile aprire {t.Describe()}.");

        switch (t.Kind)
        {
            case RegValueKind.DWord:
                key.SetValue(t.ValueName, unchecked((int)value.AsUInt32()), RegistryValueKind.DWord);
                break;
            case RegValueKind.QWord:
                key.SetValue(t.ValueName, unchecked((long)value.AsUInt64()), RegistryValueKind.QWord);
                break;
            case RegValueKind.String:
                key.SetValue(t.ValueName, value.Data!, RegistryValueKind.String);
                break;
            case RegValueKind.ExpandString:
                key.SetValue(t.ValueName, value.Data!, RegistryValueKind.ExpandString);
                break;
            case RegValueKind.MultiString:
                key.SetValue(t.ValueName, value.AsMulti(), RegistryValueKind.MultiString);
                break;
            case RegValueKind.Binary:
                key.SetValue(t.ValueName, value.AsBinary(), RegistryValueKind.Binary);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    private static RegistryKey OpenRoot(RegHive hive) => RegistryKey.OpenBaseKey(
        hive == RegHive.LocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
        RegistryView.Registry64);
}
