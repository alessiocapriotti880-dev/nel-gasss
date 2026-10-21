using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PcOptimizer.Analysis.Windows;

// Strutture D3DKMT (interfaccia del kernel grafico usata da dxgi e dagli strumenti di sistema).
// Separate dalle chiamate P/Invoke perché i test ne verificano la dimensione su qualsiasi sistema.

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

/// <summary>Funzioni native in sola lettura usate dall'analisi.</summary>
[SupportedOSPlatform("windows")]
internal static class AnalysisNative
{
    /// <summary>KMTQAITYPE_ADAPTERREGISTRYINFO: nomi dell'adattatore (4 stringhe da MAX_PATH caratteri).</summary>
    public const int KmtQueryAdapterRegistryInfo = 8;

    /// <summary>KMTQAITYPE_DRIVERVERSION: versione WDDM del driver (es. 3100 = WDDM 3.1).</summary>
    public const int KmtQueryDriverVersion = 13;

    /// <summary>KMTQAITYPE_WDDM_2_7_CAPS: bit 0 HAGS supportato, bit 1 attivo, bit 2 attivo per impostazione predefinita.</summary>
    public const int KmtQueryWddm27Caps = 70;

    public const uint AccessScheme = 16;
    public const uint ErrorNoMoreItems = 259;

    [DllImport("ntdll.dll")]
    public static extern int NtQueryTimerResolution(out uint maximumTime, out uint minimumTime, out uint currentTime);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupOfPowerSettingsGuid, IntPtr powerSettingGuid,
        byte[]? buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    public static extern uint PowerEnumerate(
        IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupOfPowerSettingsGuid, uint accessFlags, uint index,
        byte[] buffer, ref uint bufferSize);

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTEnumAdapters2(ref D3dkmtEnumAdapters2 enumAdapters);

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTQueryAdapterInfo(ref D3dkmtQueryAdapterInfo queryAdapterInfo);

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTCloseAdapter(ref D3dkmtCloseAdapter closeAdapter);
}
