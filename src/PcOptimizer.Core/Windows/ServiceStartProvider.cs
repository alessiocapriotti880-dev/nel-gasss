using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Windows;

/// <summary>
/// Tipo di avvio dei servizi.
/// <para>
/// Lettura dal registro (Services\&lt;nome&gt;\Start e DelayedAutostart), scrittura tramite
/// Service Control Manager (ChangeServiceConfig), come fa "sc config": è il canale supportato
/// e rispetta le protezioni di Windows. I servizi protetti (es. Defender) rifiutano la modifica
/// con "accesso negato": l'errore viene riportato e l'intervento annullato, mai aggirato.
/// </para>
/// Il servizio non viene fermato né avviato: il nuovo tipo vale dal prossimo avvio.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ServiceStartProvider : ISettingProvider
{
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services\";

    public bool CanHandle(SettingTarget target) => target is ServiceStartTarget;

    public SettingValue Read(SettingTarget target)
    {
        var name = ((ServiceStartTarget)target).ServiceName;
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(ServicesKey + name);
        if (key?.GetValue("Start") is not int start)
        {
            return SettingValue.Absent; // servizio non installato
        }

        var delayed = key.GetValue("DelayedAutostart") is int d && d != 0;
        return SettingValue.Of(start switch
        {
            0 => ServiceStartValues.Boot,
            1 => ServiceStartValues.System,
            2 => delayed ? ServiceStartValues.AutomaticDelayed : ServiceStartValues.Automatic,
            3 => ServiceStartValues.Manual,
            4 => ServiceStartValues.Disabled,
            _ => start.ToString(),
        });
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        var name = ((ServiceStartTarget)target).ServiceName;
        if (!value.Exists)
        {
            // Un servizio assente prima non può essere "ricreato" né va eliminato: nulla da fare.
            return;
        }

        var (startType, delayed) = value.Data switch
        {
            ServiceStartValues.Boot => (0u, false),
            ServiceStartValues.System => (1u, false),
            ServiceStartValues.Automatic => (2u, false),
            ServiceStartValues.AutomaticDelayed => (2u, true),
            ServiceStartValues.Manual => (3u, false),
            ServiceStartValues.Disabled => (4u, false),
            _ => throw new ArgumentException($"Tipo di avvio non valido: {value.Data}"),
        };

        var scm = Native.OpenSCManagerW(null, null, Native.ScManagerConnect);
        if (scm == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var svc = Native.OpenServiceW(scm, name, Native.ServiceChangeConfig | Native.ServiceQueryConfig);
            if (svc == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Servizio {name}: impossibile aprirlo.");
            }

            try
            {
                if (!Native.ChangeServiceConfigW(svc, Native.ServiceNoChange, startType, Native.ServiceNoChange,
                        null, null, IntPtr.Zero, null, null, null, null))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Servizio {name}: modifica rifiutata.");
                }

                // L'avvio ritardato ha senso solo con tipo Automatico; per gli altri lo azzeriamo.
                if (startType == 2 || delayed)
                {
                    var info = new Native.ServiceDelayedAutoStartInfo { DelayedAutostart = delayed };
                    if (!Native.ChangeServiceConfig2W(svc, Native.ServiceConfigDelayedAutoStartInfo, ref info))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), $"Servizio {name}: avvio ritardato non impostato.");
                    }
                }
            }
            finally
            {
                Native.CloseServiceHandle(svc);
            }
        }
        finally
        {
            Native.CloseServiceHandle(scm);
        }
    }

    private static class Native
    {
        public const uint ScManagerConnect = 0x0001;
        public const uint ServiceQueryConfig = 0x0001;
        public const uint ServiceChangeConfig = 0x0002;
        public const uint ServiceNoChange = 0xFFFFFFFF;
        public const uint ServiceConfigDelayedAutoStartInfo = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct ServiceDelayedAutoStartInfo
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool DelayedAutostart;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenServiceW(IntPtr scManager, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ChangeServiceConfigW(
            IntPtr service, uint serviceType, uint startType, uint errorControl,
            string? binaryPathName, string? loadOrderGroup, IntPtr tagId, string? dependencies,
            string? serviceStartName, string? password, string? displayName);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ChangeServiceConfig2W(IntPtr service, uint infoLevel, ref ServiceDelayedAutoStartInfo info);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseServiceHandle(IntPtr handle);
    }
}
