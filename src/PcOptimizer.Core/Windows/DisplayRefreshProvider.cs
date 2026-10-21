using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Windows;

/// <summary>
/// Frequenza di aggiornamento del monitor con le API di Windows (come Impostazioni > Schermo > Avanzate).
/// Imposta solo frequenze che il driver elenca per la risoluzione attuale, dopo una prova con CDS_TEST.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DisplayRefreshProvider : ISettingProvider
{
    private const int EnumCurrentSettings = -1;
    private const int DmDisplayFrequency = 0x400000;
    private const uint CdsUpdateRegistry = 0x1;
    private const uint CdsTest = 0x2;

    public bool CanHandle(SettingTarget target) => target is DisplayRefreshTarget;

    public SettingValue Read(SettingTarget target)
    {
        var device = ((DisplayRefreshTarget)target).DeviceName;
        var mode = NewMode();
        return EnumDisplaySettingsW(device, EnumCurrentSettings, ref mode)
            ? SettingValue.Of((uint)mode.DisplayFrequency)
            : SettingValue.Absent;
    }

    /// <summary>Frequenze disponibili alla risoluzione e profondità colore attuali.</summary>
    public static IReadOnlyList<int> AvailableRefreshRates(string device)
    {
        var current = NewMode();
        if (!EnumDisplaySettingsW(device, EnumCurrentSettings, ref current))
        {
            return [];
        }

        var rates = new SortedSet<int>();
        var mode = NewMode();
        for (var i = 0; EnumDisplaySettingsW(device, i, ref mode); i++)
        {
            if (mode.PelsWidth == current.PelsWidth && mode.PelsHeight == current.PelsHeight && mode.BitsPerPel == current.BitsPerPel)
            {
                rates.Add(mode.DisplayFrequency);
            }
        }

        return rates.ToList();
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        var device = ((DisplayRefreshTarget)target).DeviceName;
        if (!value.Exists)
        {
            return;
        }

        var hz = (int)value.AsUInt32();
        if (!AvailableRefreshRates(device).Contains(hz))
        {
            throw new InvalidOperationException($"{hz} Hz non è disponibile per {device} alla risoluzione attuale.");
        }

        var mode = NewMode();
        EnumDisplaySettingsW(device, EnumCurrentSettings, ref mode);
        mode.DisplayFrequency = hz;
        mode.Fields = DmDisplayFrequency;
        var test = ChangeDisplaySettingsExW(device, ref mode, IntPtr.Zero, CdsTest, IntPtr.Zero);
        if (test != 0)
        {
            throw new InvalidOperationException($"Il driver rifiuta {hz} Hz su {device} (codice {test.ToString(CultureInfo.InvariantCulture)}).");
        }

        var result = ChangeDisplaySettingsExW(device, ref mode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
        if (result != 0)
        {
            throw new InvalidOperationException($"Frequenza non cambiata su {device} (codice {result.ToString(CultureInfo.InvariantCulture)}).");
        }
    }

    private static DevMode NewMode() => new() { Size = (short)Marshal.SizeOf<DevMode>() };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsW(string deviceName, int modeNum, ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsExW(string deviceName, ref DevMode devMode, IntPtr hwnd, uint flags, IntPtr param);

    /// <summary>DEVMODEW nella variante per i display (220 byte).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public short SpecVersion;
        public short DriverVersion;
        public short Size;
        public short DriverExtra;
        public int Fields;
        public int PositionX;
        public int PositionY;
        public int DisplayOrientation;
        public int DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;
        public short LogPixels;
        public int BitsPerPel;
        public int PelsWidth;
        public int PelsHeight;
        public int DisplayFlags;
        public int DisplayFrequency;
        public int IcmMethod;
        public int IcmIntent;
        public int MediaType;
        public int DitherType;
        public int Reserved1;
        public int Reserved2;
        public int PanningWidth;
        public int PanningHeight;
    }
}
