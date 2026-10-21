using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Windows;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Tweaks.Catalog;
using PcOptimizer.Tweaks.Context;

namespace PcOptimizer.Tweaks.Windows;

/// <summary>Raccolta in sola lettura dei dati aggiuntivi che servono al catalogo degli interventi.</summary>
[SupportedOSPlatform("windows")]
public sealed class TweakEnvironmentCollector(ILogger<TweakEnvironmentCollector> logger)
{
    private const string NetClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    public TweakEnvironment Collect(HardwareProfile profile, GameLibrary? games = null)
    {
        var registry = new RegistrySettingProvider();
        var values = new Dictionary<string, SettingValue>();
        foreach (var (id, target) in TweakProbes.All)
        {
            values[id] = Safe(() => registry.Read(target), SettingValue.Absent, $"registro {id}");
        }

        foreach (var game in games?.Games ?? [])
        {
            values[GameTweaks.GameProbe(game)] = Safe(() => registry.Read(GameTweaks.LayersTarget(game)), SettingValue.Absent, "livelli di compatibilità");
        }

        return new TweakEnvironment
        {
            PrimaryAdapter = Safe(() => ReadAdapter(profile), null, "scheda di rete"),
            BitLockerOnSystemDrive = Safe(BitLocker, null, "BitLocker"),
            PowerOverlaySupported = Safe(() => PowerOverlayProvider.IsSupported, false, "modalità di alimentazione"),
            ActivePowerScheme = Safe<Guid?>(() => PowerSettingProvider.ActiveScheme(), null, "piano energetico"),
            Displays = Safe(() => ReadDisplays(profile), [], "monitor"),
            Registry = values,
            LaunchScriptFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) is { Length: > 0 } desktop ? desktop : null,
        };
    }

    /// <summary>Solo il piano attivo, da aggiornare dopo aver cambiato piano senza rifare tutta la raccolta.</summary>
    public static Guid? ActiveScheme()
    {
        try
        {
            return PowerSettingProvider.ActiveScheme();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static NetworkAdapterConfig? ReadAdapter(HardwareProfile profile)
    {
        var primary = profile.NetworkAdapters.FirstOrDefault(a => a.IsPrimary) ?? profile.NetworkAdapters.FirstOrDefault(a => a.IsConnected);
        if (primary?.InterfaceId is not { } interfaceId)
        {
            return null;
        }

        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var classKey = root.OpenSubKey(NetClass);
        foreach (var sub in classKey?.GetSubKeyNames().Where(n => n.All(char.IsAsciiDigit)) ?? [])
        {
            using var instance = classKey!.OpenSubKey(sub);
            if (instance?.GetValue("NetCfgInstanceId") is not string id ||
                !id.Trim('{', '}').Equals(interfaceId.Trim('{', '}'), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return new NetworkAdapterConfig(
                instance.GetValue("DriverDesc") as string ?? primary.Description,
                interfaceId,
                $@"{NetClass}\{sub}",
                primary.Medium == NetworkMedium.WiFi,
                ReadParameters(instance));
        }

        return null;
    }

    /// <summary>Parametri avanzati dichiarati dal driver in Ndi\params, con valore attuale, ammessi e limiti.</summary>
    private static IReadOnlyList<AdapterParameter> ReadParameters(RegistryKey instance)
    {
        using var parameters = instance.OpenSubKey(@"Ndi\params");
        var result = new List<AdapterParameter>();
        foreach (var keyword in parameters?.GetSubKeyNames() ?? [])
        {
            using var p = parameters!.OpenSubKey(keyword);
            if (p is null)
            {
                continue;
            }

            using var enumKey = p.OpenSubKey("enum");
            result.Add(new AdapterParameter(
                keyword,
                p.GetValue("ParamDesc") as string,
                instance.GetValue(keyword)?.ToString(),
                p.GetValue("default")?.ToString(),
                p.GetValue("type") as string ?? "",
                enumKey?.GetValueNames().Where(n => n.Length > 0).ToList() ?? [],
                long.TryParse(p.GetValue("min")?.ToString(), out var min) ? min : null,
                long.TryParse(p.GetValue("max")?.ToString(), out var max) ? max : null));
        }

        return result;
    }

    private static bool? BitLocker()
    {
        var drive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\');
        var rows = Wmi.Query(@"root\CIMV2\Security\MicrosoftVolumeEncryption",
            $"SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = '{drive}'");
        return rows.Count == 0 ? false : rows.Any(r => r.Long("ProtectionStatus") == 1);
    }

    private static IReadOnlyList<DisplayModes> ReadDisplays(HardwareProfile profile)
    {
        var provider = new DisplayRefreshProvider();
        return profile.Displays
            .Where(d => d.GdiDeviceName is not null)
            .Select(d => (Name: d.GdiDeviceName!, Current: provider.Read(new DisplayRefreshTarget(d.GdiDeviceName!))))
            .Where(x => x.Current.Exists)
            .Select(x => new DisplayModes(x.Name, (int)x.Current.AsUInt32(), DisplayRefreshProvider.AvailableRefreshRates(x.Name).Where(hz => hz >= 20).ToList()))
            .ToList();
    }

    private T Safe<T>(Func<T> read, T fallback, string what)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogInformation(ex, "Dato non disponibile per gli interventi: {What}", what);
            return fallback;
        }
    }
}
