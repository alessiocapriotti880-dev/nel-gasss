using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Hardware.Parsing;

namespace PcOptimizer.Hardware.Windows;

/// <summary>
/// Monitor attivi tramite le API DisplayConfig (le stesse delle Impostazioni di Windows):
/// frequenza reale come frazione (es. 143,98 Hz), nome dall'EDID, tipo di collegamento e GPU
/// a cui il cavo è fisicamente collegato. La frequenza massima disponibile alla risoluzione
/// attuale viene da EnumDisplaySettings.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DisplayDetector
{
    public static IReadOnlyList<DisplayInfo> Detect()
    {
        var (paths, modes) = QueryActivePaths();
        var result = new List<DisplayInfo>();

        foreach (var path in paths)
        {
            var target = path.TargetInfo;
            var (friendlyName, outputTechnology) = GetTargetName(target.AdapterId, target.Id);
            var gdiName = GetSourceName(path.SourceInfo.AdapterId, path.SourceInfo.Id);
            var (vendorId, deviceId) = PciIdParser.Parse(GetAdapterPath(target.AdapterId));

            int width = 0, height = 0;
            var primary = false;
            if (path.SourceInfo.ModeInfoIdx < modes.Length &&
                modes[path.SourceInfo.ModeInfoIdx] is { InfoType: DisplayConfigModeInfo.TypeSource } sourceMode)
            {
                width = (int)sourceMode.SourceMode.Width;
                height = (int)sourceMode.SourceMode.Height;
                primary = sourceMode.SourceMode.Position is { X: 0, Y: 0 }; // il monitor principale ha origine (0,0)
            }

            // Frequenza del segnale dalla modalità di destinazione; altrimenti quella del percorso.
            var refresh = target.RefreshRate.Value;
            if (target.ModeInfoIdx < modes.Length &&
                modes[target.ModeInfoIdx] is { InfoType: DisplayConfigModeInfo.TypeTarget } targetMode &&
                targetMode.TargetVideoSignalInfo.VSyncFreq.Value > 0)
            {
                refresh = targetMode.TargetVideoSignalInfo.VSyncFreq.Value;
            }

            var connection = DisplayParser.Connection(outputTechnology != 0 ? outputTechnology : target.OutputTechnology);
            result.Add(new DisplayInfo
            {
                Name = !string.IsNullOrWhiteSpace(friendlyName) ? friendlyName
                    : connection == DisplayConnection.Internal ? "Schermo integrato"
                    : "Monitor",
                GdiDeviceName = gdiName,
                Width = width,
                Height = height,
                RefreshHz = Math.Round(refresh, 2),
                MaxRefreshHz = gdiName is null ? null : MaxRefreshAt(gdiName, width, height),
                Connection = connection,
                IsPrimary = primary,
                AdapterPciVendorId = vendorId,
                AdapterPciDeviceId = deviceId,
            });
        }

        return result;
    }

    private static (DisplayConfigPathInfo[] Paths, DisplayConfigModeInfo[] Modes) QueryActivePaths()
    {
        // La configurazione può cambiare tra le due chiamate (monitor collegato/scollegato): si riprova.
        for (var attempt = 0; ; attempt++)
        {
            var error = NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QdcOnlyActivePaths, out var pathCount, out var modeCount);
            if (error != 0)
            {
                throw new Win32Exception(error);
            }

            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            error = NativeMethods.QueryDisplayConfig(NativeMethods.QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (error == 0)
            {
                return (paths[..(int)pathCount], modes[..(int)modeCount]);
            }

            if (error != NativeMethods.ErrorInsufficientBuffer || attempt >= 3)
            {
                throw new Win32Exception(error);
            }
        }
    }

    private static (string? Name, int OutputTechnology) GetTargetName(Luid adapter, uint id)
    {
        var packet = new DisplayConfigTargetDeviceName
        {
            Header = Header(DisplayConfigDeviceInfoHeader.GetTargetName, Marshal.SizeOf<DisplayConfigTargetDeviceName>(), adapter, id),
            MonitorFriendlyDeviceName = "",
            MonitorDevicePath = "",
        };
        return NativeMethods.DisplayConfigGetDeviceInfo(ref packet) == 0
            ? (packet.MonitorFriendlyDeviceName, packet.OutputTechnology)
            : (null, 0);
    }

    private static string? GetSourceName(Luid adapter, uint id)
    {
        var packet = new DisplayConfigSourceDeviceName
        {
            Header = Header(DisplayConfigDeviceInfoHeader.GetSourceName, Marshal.SizeOf<DisplayConfigSourceDeviceName>(), adapter, id),
            ViewGdiDeviceName = "",
        };
        return NativeMethods.DisplayConfigGetDeviceInfo(ref packet) == 0 && packet.ViewGdiDeviceName.Length > 0
            ? packet.ViewGdiDeviceName
            : null;
    }

    private static string? GetAdapterPath(Luid adapter)
    {
        var packet = new DisplayConfigAdapterName
        {
            Header = Header(DisplayConfigDeviceInfoHeader.GetAdapterName, Marshal.SizeOf<DisplayConfigAdapterName>(), adapter, 0),
            AdapterDevicePath = "",
        };
        return NativeMethods.DisplayConfigGetDeviceInfo(ref packet) == 0 ? packet.AdapterDevicePath : null;
    }

    private static DisplayConfigDeviceInfoHeader Header(int type, int size, Luid adapter, uint id) =>
        new() { Type = type, Size = (uint)size, AdapterId = adapter, Id = id };

    /// <summary>Frequenza più alta offerta dal monitor alla risoluzione indicata (null se non determinabile).</summary>
    private static int? MaxRefreshAt(string gdiDeviceName, int width, int height)
    {
        var mode = new DevMode { DeviceName = "", FormName = "", Size = (short)Marshal.SizeOf<DevMode>() };
        var max = 0;
        for (var i = 0; NativeMethods.EnumDisplaySettings(gdiDeviceName, i, ref mode); i++)
        {
            if (mode.PelsWidth == width && mode.PelsHeight == height)
            {
                max = Math.Max(max, mode.DisplayFrequency);
            }
        }

        // 0 e 1 significano "frequenza predefinita dell'hardware": non informativi.
        return max > 1 ? max : null;
    }
}
