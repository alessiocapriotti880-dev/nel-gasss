using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PcOptimizer.Benchmark.Windows;

[StructLayout(LayoutKind.Sequential)]
internal struct KmtLuid
{
    public uint LowPart;
    public int HighPart;
}

/// <summary>D3DKMT_ADAPTERINFO (20 byte).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct D3dkmtAdapterInfo
{
    public uint Adapter;
    public KmtLuid AdapterLuid;
    public uint NumOfSources;
    public int PrecisePresentRegionsPreferred;
}

/// <summary>D3DKMT_ENUMADAPTERS2 (16 byte su x64).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct D3dkmtEnumAdapters2
{
    public uint NumAdapters;
    public IntPtr Adapters;
}

/// <summary>D3DKMT_QUERYADAPTERINFO (24 byte su x64).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct D3dkmtQueryAdapterInfo
{
    public uint Adapter;
    public int Type;
    public IntPtr PrivateDriverData;
    public uint PrivateDriverDataSize;
}

/// <summary>D3DKMT_CLOSEADAPTER (4 byte).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct D3dkmtCloseAdapter
{
    public uint Adapter;
}

/// <summary>
/// D3DKMT_ADAPTER_PERFDATA (64 byte): gli stessi dati che Gestione attività mostra per la GPU
/// (temperatura in decimi di °C, ventola in RPM, potenza in decimi di % del limite), validi per ogni produttore
/// con driver WDDM 2.4 o successivo.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct D3dkmtAdapterPerfData
{
    public uint PhysicalAdapterIndex;
    public ulong MemoryFrequency;
    public ulong MaxMemoryFrequency;
    public ulong MaxMemoryFrequencyOC;
    public ulong MemoryBandwidth;
    public ulong PcieBandwidth;
    public uint FanRpm;
    public uint Power;
    public uint Temperature;
    public byte PowerStateOverride;
}

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

/// <summary>Funzioni native usate dalle misure (tutte in lettura, tranne il timer del proprio processo).</summary>
[SupportedOSPlatform("windows")]
internal static class BenchmarkNative
{
    public const int KmtQueryAdapterRegistryInfo = 8;
    public const int KmtQueryAdapterPerfData = 62;
    public const uint CreateWaitableTimerHighResolution = 0x2;
    public const uint TimerAllAccess = 0x1F0003;
    public const uint WaitObject0 = 0;
    public const int SystemProcessInformation = 5;
    public const uint StatusInfoLengthMismatch = 0xC0000004;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint desiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completionRoutine, IntPtr argToCompletionRoutine, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("ntdll.dll")]
    public static extern uint NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returnLength);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryTimerResolution(out uint maximumTime, out uint minimumTime, out uint currentTime);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDeviceDrivers([Out] IntPtr[]? imageBases, int size, out int needed);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode)]
    public static extern int GetDeviceDriverBaseNameW(IntPtr imageBase, char[] baseName, int size);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupOfPowerSettingsGuid, IntPtr powerSettingGuid,
        byte[]? buffer, ref uint bufferSize);

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTEnumAdapters2(ref D3dkmtEnumAdapters2 enumAdapters);

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTQueryAdapterInfo(ref D3dkmtQueryAdapterInfo queryAdapterInfo);

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTCloseAdapter(ref D3dkmtCloseAdapter closeAdapter);
}
