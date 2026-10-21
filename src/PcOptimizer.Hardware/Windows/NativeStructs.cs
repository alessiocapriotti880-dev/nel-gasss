using System.Runtime.InteropServices;

namespace PcOptimizer.Hardware.Windows;

// Strutture native di Windows. Sono separate dalle chiamate P/Invoke (solo Windows) perché la loro
// dimensione deve coincidere al byte con quella delle API: i test la verificano su qualsiasi sistema.

[StructLayout(LayoutKind.Sequential)]
internal struct Luid
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigRational
{
    public uint Numerator;
    public uint Denominator;

    public readonly double Value => Denominator == 0 ? 0 : (double)Numerator / Denominator;
}

/// <summary>DISPLAYCONFIG_PATH_SOURCE_INFO (20 byte).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathSourceInfo
{
    public Luid AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public uint StatusFlags;
}

/// <summary>DISPLAYCONFIG_PATH_TARGET_INFO (48 byte).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathTargetInfo
{
    public Luid AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public int OutputTechnology;
    public int Rotation;
    public int Scaling;
    public DisplayConfigRational RefreshRate;
    public int ScanLineOrdering;
    public int TargetAvailable;
    public uint StatusFlags;
}

/// <summary>DISPLAYCONFIG_PATH_INFO (72 byte): un monitor attivo.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathInfo
{
    public DisplayConfigPathSourceInfo SourceInfo;
    public DisplayConfigPathTargetInfo TargetInfo;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfig2DRegion
{
    public uint Cx;
    public uint Cy;
}

/// <summary>DISPLAYCONFIG_VIDEO_SIGNAL_INFO (48 byte): VSyncFreq è la frequenza reale del segnale.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigVideoSignalInfo
{
    public ulong PixelRate;
    public DisplayConfigRational HSyncFreq;
    public DisplayConfigRational VSyncFreq;
    public DisplayConfig2DRegion ActiveSize;
    public DisplayConfig2DRegion TotalSize;
    public uint VideoStandard;
    public int ScanLineOrdering;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PointL
{
    public int X;
    public int Y;
}

/// <summary>DISPLAYCONFIG_SOURCE_MODE (20 byte): risoluzione e posizione sul desktop.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigSourceMode
{
    public uint Width;
    public uint Height;
    public int PixelFormat;
    public PointL Position;
}

/// <summary>DISPLAYCONFIG_MODE_INFO (64 byte): unione di modalità sorgente e destinazione.</summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct DisplayConfigModeInfo
{
    public const int TypeSource = 1;
    public const int TypeTarget = 2;

    [FieldOffset(0)]
    public int InfoType;

    [FieldOffset(4)]
    public uint Id;

    [FieldOffset(8)]
    public Luid AdapterId;

    [FieldOffset(16)]
    public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;

    [FieldOffset(16)]
    public DisplayConfigSourceMode SourceMode;
}

/// <summary>DISPLAYCONFIG_DEVICE_INFO_HEADER (20 byte).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigDeviceInfoHeader
{
    public const int GetSourceName = 1;
    public const int GetTargetName = 2;
    public const int GetAdapterName = 4;

    public int Type;
    public uint Size;
    public Luid AdapterId;
    public uint Id;
}

/// <summary>DISPLAYCONFIG_TARGET_DEVICE_NAME (420 byte): nome del monitor dall'EDID.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigTargetDeviceName
{
    public DisplayConfigDeviceInfoHeader Header;
    public uint Flags;
    public int OutputTechnology;
    public ushort EdidManufactureId;
    public ushort EdidProductCodeId;
    public uint ConnectorInstance;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string MonitorFriendlyDeviceName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string MonitorDevicePath;
}

/// <summary>DISPLAYCONFIG_SOURCE_DEVICE_NAME (84 byte): nome GDI, es. "\\.\DISPLAY1".</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigSourceDeviceName
{
    public DisplayConfigDeviceInfoHeader Header;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string ViewGdiDeviceName;
}

/// <summary>DISPLAYCONFIG_ADAPTER_NAME (276 byte): percorso PnP della GPU (contiene VEN_/DEV_).</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigAdapterName
{
    public DisplayConfigDeviceInfoHeader Header;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string AdapterDevicePath;
}

/// <summary>DEVMODEW per gli schermi (220 byte).</summary>
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

/// <summary>SYSTEM_POWER_STATUS: ACLineStatus 0 = batteria, 1 = alimentatore, 255 = sconosciuto.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemPowerStatus
{
    public byte AcLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;
}
