using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using Microsoft.Win32;
using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Evaluation;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Analysis.Parsing;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Windows;

namespace PcOptimizer.Analysis.Windows;

/// <summary>Piano energetico attivo, piani disponibili e impostazioni rilevanti.</summary>
[SupportedOSPlatform("windows")]
internal static class PowerCollector
{
    public static PowerSnapshot Collect()
    {
        var active = PowerSettingProvider.ActiveScheme();
        var provider = new PowerSettingProvider();
        var values = PowerSettingsCatalog.Settings
            .Select(d => new PowerValue(d.Key, Read(provider, active, d, PowerSource.Ac), Read(provider, active, d, PowerSource.Dc)))
            .ToList();

        return new PowerSnapshot
        {
            ActiveScheme = active,
            ActiveSchemeName = FriendlyName(active),
            Schemes = EnumerateSchemes().Select(g => new PowerSchemeEntry(g, FriendlyName(g) ?? g.ToString())).ToList(),
            Values = values,
        };
    }

    private static uint? Read(PowerSettingProvider provider, Guid scheme, PowerSettingDefinition d, PowerSource source)
    {
        try
        {
            var value = provider.Read(new PowerSettingTarget(scheme, d.SubGroup, d.Setting, source, d.Label));
            return value.Exists ? value.AsUInt32() : null;
        }
        catch (Win32Exception)
        {
            return null; // impostazione non presente in questo piano
        }
    }

    private static string? FriendlyName(Guid scheme)
    {
        uint size = 0;
        AnalysisNative.PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (size == 0)
        {
            return null;
        }

        var buffer = new byte[size];
        return AnalysisNative.PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
            ? Encoding.Unicode.GetString(buffer).TrimEnd('\0')
            : null;
    }

    private static IEnumerable<Guid> EnumerateSchemes()
    {
        for (uint index = 0; ; index++)
        {
            var buffer = new byte[16];
            uint size = 16;
            if (AnalysisNative.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AnalysisNative.AccessScheme, index, buffer, ref size) != 0)
            {
                yield break; // ERROR_NO_MORE_ITEMS o errore: fine dell'elenco
            }

            yield return new Guid(buffer);
        }
    }
}

/// <summary>Risoluzione del timer, configurazione di avvio (bcdedit) e dispositivo HPET.</summary>
[SupportedOSPlatform("windows")]
internal static class TimerCollector
{
    public static TimerSnapshot Collect()
    {
        double? current = null, finest = null, coarsest = null;
        if (AnalysisNative.NtQueryTimerResolution(out var maximum, out var minimum, out var now) == 0)
        {
            // "Maximum" è l'intervallo più lungo (es. 15,625 ms), "Minimum" il più fine (es. 0,5 ms).
            coarsest = MiscParsers.TimerMs(maximum);
            finest = MiscParsers.TimerMs(minimum);
            current = MiscParsers.TimerMs(now);
        }

        var (readable, elements) = ReadBcd();
        return new TimerSnapshot
        {
            CurrentMs = current,
            FinestMs = finest,
            CoarsestMs = coarsest,
            BcdReadable = readable,
            BcdElements = elements,
            HpetDeviceEnabled = ReadHpet(),
        };
    }

    /// <summary>
    /// Legge la voce di avvio corrente con lo strumento ufficiale (bcdedit, sola lettura, richiede amministratore).
    /// L'output usa la code page OEM della console e i valori sì/no nella lingua di Windows.
    /// </summary>
    private static (bool Readable, IReadOnlyDictionary<string, string> Elements) ReadBcd()
    {
        var empty = new Dictionary<string, string>();
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "bcdedit.exe"), "/enum {current}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            };

            using var process = Process.Start(info);
            if (process is null)
            {
                return (false, empty);
            }

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(10_000))
            {
                process.Kill();
                return (false, empty);
            }

            return process.ExitCode == 0
                ? (true, BcdParser.ParseElements(output, SystemAnalyzer.TimerBcdElements))
                : (false, empty); // tipicamente "accesso negato" senza privilegi di amministratore
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return (false, empty);
        }
    }

    /// <summary>Stato del dispositivo HPET (ACPI\PNP0103): codice 22 = disattivato dall'utente.</summary>
    private static bool? ReadHpet()
    {
        var rows = Wmi.Query(Wmi.Cimv2, @"SELECT ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'ACPI\\PNP0103%'");
        return rows.Count == 0 ? null : rows.Any(r => r.Int("ConfigManagerErrorCode") != 22);
    }
}

/// <summary>Configurazione degli interrupt dei dispositivi PCI: MSI, IRQ assegnati, affinità.</summary>
[SupportedOSPlatform("windows")]
internal static class InterruptCollector
{
    public static IReadOnlyList<PciDeviceEntry> Collect()
    {
        var devices = Wmi.Query(Wmi.Cimv2, @"SELECT Name, PNPDeviceID, ClassGuid FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'PCI\\%'");

        // Una sola query per tutte le risorse IRQ assegnate (più veloce di una per dispositivo).
        var irqs = Wmi.Query(Wmi.Cimv2, "SELECT Antecedent, Dependent FROM Win32_PnPAllocatedResource")
            .Select(r => IrqParser.ParseAllocation(r.Str("Antecedent"), r.Str("Dependent")))
            .Where(a => a is not null)
            .Select(a => a!.Value)
            .GroupBy(a => a.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Irq).Distinct().ToList(), StringComparer.OrdinalIgnoreCase);

        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return devices.Select(row =>
        {
            var pnp = row.Str("PNPDeviceID") ?? "";
            var basePath = $@"SYSTEM\CurrentControlSet\Enum\{pnp}\Device Parameters\Interrupt Management";
            using var msi = TryOpen(hklm, basePath + @"\MessageSignaledInterruptProperties");
            using var affinity = TryOpen(hklm, basePath + @"\Affinity Policy");

            return new PciDeviceEntry
            {
                Name = row.Str("Name") ?? pnp,
                PnpDeviceId = pnp,
                ClassGuid = Guid.TryParse(row.Str("ClassGuid"), out var classGuid) ? classGuid : null,
                MsiSupported = msi?.GetValue("MSISupported") is int supported ? supported != 0 : null,
                MessageNumberLimit = msi?.GetValue("MessageNumberLimit") as int?,
                Irqs = irqs.TryGetValue(pnp, out var list) ? list : [],
                AffinityPolicy = affinity?.GetValue("DevicePolicy") as int?,
                DevicePriority = affinity?.GetValue("DevicePriority") as int?,
                AffinityMask = MiscParsers.AffinityMask(affinity?.GetValue("AssignmentSetOverride") as byte[]),
            };
        }).ToList();
    }

    private static RegistryKey? TryOpen(RegistryKey root, string path)
    {
        try
        {
            return root.OpenSubKey(path);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Stato reale della pianificazione GPU (HAGS) e versione WDDM, chiesti al kernel grafico tramite D3DKMT:
/// il registro indica solo la preferenza, il driver dice cosa è supportato e attivo.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class GraphicsCollector
{
    private const int MaxPath = 260;

    public static IReadOnlyList<GpuSchedulingEntry> Collect()
    {
        var args = new D3dkmtEnumAdapters2();
        if (AnalysisNative.D3DKMTEnumAdapters2(ref args) != 0 || args.NumAdapters == 0)
        {
            return [];
        }

        var size = Marshal.SizeOf<D3dkmtAdapterInfo>();
        var buffer = Marshal.AllocHGlobal(size * (int)args.NumAdapters);
        try
        {
            args.Adapters = buffer;
            if (AnalysisNative.D3DKMTEnumAdapters2(ref args) != 0)
            {
                return [];
            }

            var result = new List<GpuSchedulingEntry>();
            for (var i = 0; i < args.NumAdapters; i++)
            {
                var info = Marshal.PtrToStructure<D3dkmtAdapterInfo>(buffer + i * size);
                try
                {
                    var caps = QueryUInt(info.Adapter, AnalysisNative.KmtQueryWddm27Caps) ?? 0;
                    result.Add(new GpuSchedulingEntry
                    {
                        AdapterName = QueryName(info.Adapter) ?? "Adattatore grafico",
                        HwSchSupported = (caps & 1) != 0,
                        HwSchEnabled = (caps & 2) != 0,
                        HwSchEnabledByDefault = (caps & 4) != 0,
                        WddmVersion = QueryUInt(info.Adapter, AnalysisNative.KmtQueryDriverVersion) is { } v ? (int)v : null,
                    });
                }
                finally
                {
                    var close = new D3dkmtCloseAdapter { Adapter = info.Adapter };
                    AnalysisNative.D3DKMTCloseAdapter(ref close);
                }
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static uint? QueryUInt(uint adapter, int type)
    {
        var data = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            Marshal.WriteInt32(data, 0);
            var query = new D3dkmtQueryAdapterInfo { Adapter = adapter, Type = type, PrivateDriverData = data, PrivateDriverDataSize = sizeof(uint) };
            return AnalysisNative.D3DKMTQueryAdapterInfo(ref query) == 0 ? (uint)Marshal.ReadInt32(data) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    /// <summary>Nome dell'adattatore: prima delle quattro stringhe di D3DKMT_ADAPTERREGISTRYINFO.</summary>
    private static string? QueryName(uint adapter)
    {
        const int size = 4 * MaxPath * sizeof(char);
        var data = Marshal.AllocHGlobal(size);
        try
        {
            var query = new D3dkmtQueryAdapterInfo
            {
                Adapter = adapter, Type = AnalysisNative.KmtQueryAdapterRegistryInfo, PrivateDriverData = data, PrivateDriverDataSize = size,
            };
            return AnalysisNative.D3DKMTQueryAdapterInfo(ref query) == 0 ? Marshal.PtrToStringUni(data) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }
}

/// <summary>Valori di registro del catalogo e impostazioni distribuite su più chiavi.</summary>
[SupportedOSPlatform("windows")]
internal static class RegistryCollector
{
    public static IReadOnlyDictionary<string, SettingValue> ReadProbes()
    {
        var provider = new RegistrySettingProvider();
        var values = new Dictionary<string, SettingValue>();
        foreach (var probe in RegistryProbes.All)
        {
            try
            {
                values[probe.Id] = provider.Read(probe.Target);
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException
                                           or InvalidCastException or FormatException or OverflowException)
            {
                values[probe.Id] = SettingValue.Absent; // tipo inatteso o chiave non leggibile
            }
        }

        return values;
    }

    /// <summary>Eseguibili con "Disattiva ottimizzazioni a schermo intero" (flag di compatibilità, utente e computer).</summary>
    public static IReadOnlyList<string> FsoDisabledApps()
    {
        var result = new List<string>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var layers = root.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
            foreach (var app in layers?.GetValueNames() ?? [])
            {
                if (MiscParsers.DisablesFullscreenOptimizations(layers!.GetValue(app) as string))
                {
                    result.Add(app);
                }
            }
        }

        return result;
    }

    /// <summary>Preferenze GPU per app (Impostazioni > Schermo > Grafica).</summary>
    public static IReadOnlyList<GpuPreferenceEntry> GpuPreferences()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = root.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
        var result = new List<GpuPreferenceEntry>();
        foreach (var app in key?.GetValueNames() ?? [])
        {
            if (app.Equals("DirectXUserGlobalSettings", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var settings = DirectXSettingsParser.Parse(key!.GetValue(app) as string);
            if (settings.TryGetValue("GpuPreference", out var value) && int.TryParse(value, out var preference))
            {
                result.Add(new GpuPreferenceEntry(app, preference));
            }
        }

        return result;
    }

    /// <summary>Impostazioni Nagle per interfaccia di rete: presenti solo se qualcuno le ha aggiunte.</summary>
    public static IReadOnlyList<TcpInterfaceEntry> TcpInterfaces()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var interfaces = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
        var result = new List<TcpInterfaceEntry>();
        foreach (var id in interfaces?.GetSubKeyNames() ?? [])
        {
            using var key = interfaces!.OpenSubKey(id);
            uint? Read(string name) => key?.GetValue(name) is int v ? unchecked((uint)v) : null;
            var entry = new TcpInterfaceEntry(id, Read("TcpAckFrequency"), Read("TCPNoDelay"), Read("TcpDelAckTicks"));
            if (entry.TcpAckFrequency is not null || entry.TcpNoDelay is not null || entry.TcpDelAckTicks is not null)
            {
                result.Add(entry);
            }
        }

        return result;
    }
}

/// <summary>VBS/HVCI, hypervisor, Defender, antivirus registrati e TPM. Ogni parte è indipendente.</summary>
[SupportedOSPlatform("windows")]
internal static class SecurityCollector
{
    public static SecuritySnapshot Collect(Action<string, Exception> reportError)
    {
        var snapshot = new SecuritySnapshot();

        void Try(string area, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
            {
                reportError(area, ex);
            }
        }

        Try("VBS", () =>
        {
            if (Wmi.Query(Wmi.DeviceGuard, "SELECT * FROM Win32_DeviceGuard").FirstOrDefault() is { } row)
            {
                snapshot = snapshot with
                {
                    VbsStatus = row.Int("VirtualizationBasedSecurityStatus"),
                    SecurityServicesRunning = row.Numbers("SecurityServicesRunning"),
                };
            }
        });

        Try("Hypervisor", () => snapshot = snapshot with
        {
            HypervisorPresent = Wmi.Query(Wmi.Cimv2, "SELECT HypervisorPresent FROM Win32_ComputerSystem").FirstOrDefault()?.Bool("HypervisorPresent"),
        });

        Try("Microsoft Defender", () =>
        {
            if (Wmi.Query(Wmi.Defender, "SELECT * FROM MSFT_MpComputerStatus").FirstOrDefault() is { } row)
            {
                snapshot = snapshot with
                {
                    DefenderRealTimeProtection = row.Bool("RealTimeProtectionEnabled"),
                    DefenderAntivirusEnabled = row.Bool("AntivirusEnabled"),
                    DefenderTamperProtected = row.Bool("IsTamperProtected"),
                    DefenderMode = row.Str("AMRunningMode"),
                };
            }
        });

        // productState: il bit 12 indica la protezione attiva (Centro sicurezza di Windows).
        Try("Antivirus", () => snapshot = snapshot with
        {
            AntivirusProducts = Wmi.Query(Wmi.SecurityCenter, "SELECT displayName, productState FROM AntiVirusProduct")
                .Select(r => new AntivirusEntry(r.Str("displayName") ?? "Antivirus", ((r.Long("productState") ?? 0) & 0x1000) != 0))
                .ToList(),
        });

        Try("TPM", () =>
        {
            var rows = Wmi.Query(Wmi.Tpm, "SELECT * FROM Win32_Tpm");
            snapshot = snapshot with
            {
                TpmPresent = rows.Count > 0,
                TpmSpecVersion = rows.FirstOrDefault()?.Str("SpecVersion"),
                TpmEnabled = rows.FirstOrDefault()?.Bool("IsEnabled_InitialValue"),
            };
        });

        return snapshot;
    }
}

/// <summary>File di paging: gestione automatica e file attivi.</summary>
[SupportedOSPlatform("windows")]
internal static class PageFileCollector
{
    public static PageFileSnapshot Collect()
    {
        var automatic = Wmi.Query(Wmi.Cimv2, "SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem")
            .FirstOrDefault()?.Bool("AutomaticManagedPagefile");
        var files = Wmi.Query(Wmi.Cimv2, "SELECT Name, AllocatedBaseSize FROM Win32_PageFileUsage")
            .Select(r => new PageFileEntry(r.Str("Name") ?? "", r.Int("AllocatedBaseSize") ?? 0))
            .ToList();
        return new PageFileSnapshot { AutomaticManaged = automatic ?? false, Files = files };
    }
}
