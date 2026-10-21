using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Versioning;
using Microsoft.Win32;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Hardware.Parsing;

namespace PcOptimizer.Hardware.Windows;

/// <summary>
/// CPU: CPUID (istruzione della CPU, nessun driver), topologia P/E da Windows,
/// microcode dal registro. Ogni fonte ha un ripiego (registro, WMI) se non disponibile.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class CpuDetector
{
    private const string CpuKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";

    public static CpuInfo Detect()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey(CpuKey);

        var cpuid = X86Base.IsSupported ? ReadCpuId() : null;
        var vendorId = cpuid?.VendorId ?? key?.GetValue("VendorIdentifier") as string ?? "";
        var vendor = CpuIdDecoder.ParseVendor(vendorId);

        var signature = cpuid is not null
            ? CpuIdDecoder.DecodeSignature(cpuid.Signature)
            : CpuIdDecoder.ParseRegistryIdentifier(key?.GetValue("Identifier") as string) ?? (0, 0, 0);

        var brand = !string.IsNullOrWhiteSpace(cpuid?.Brand)
            ? cpuid.Brand
            : CpuIdDecoder.NormalizeSpaces(key?.GetValue("ProcessorNameString") as string ?? "");

        var topology = TryReadTopology() ?? ReadTopologyFromWmi();
        var registryMhz = key?.GetValue("~MHz") is int mhz && mhz > 0 ? mhz : (int?)null;

        return new CpuInfo
        {
            Vendor = vendor,
            VendorId = vendorId,
            BrandString = brand,
            Family = signature.Family,
            Model = signature.Model,
            Stepping = signature.Stepping,
            Packages = topology.Packages,
            PhysicalCores = topology.PhysicalCores,
            LogicalProcessors = topology.LogicalProcessors,
            PerformanceCores = topology.PerformanceCores,
            EfficiencyCores = topology.EfficiencyCores,
            HasSmt = topology.HasSmt,
            L3CacheBytes = topology.L3CacheBytes,
            MicrocodeRevision = MicrocodeParser.Parse(vendor, key?.GetValue("Update Revision") as byte[]),
            BaseClockMHz = cpuid?.BaseMHz ?? registryMhz,
            MaxClockMHz = cpuid?.MaxMHz,
            HypervisorPresent = cpuid?.Hypervisor ?? false,
        };
    }

    private sealed record CpuIdSnapshot(string VendorId, string Brand, uint Signature, int? BaseMHz, int? MaxMHz, bool Hypervisor);

    private static CpuIdSnapshot ReadCpuId()
    {
        var leaf0 = X86Base.CpuId(0, 0);
        var maxLeaf = leaf0.Eax;
        var leaf1 = X86Base.CpuId(1, 0);

        var brand = "";
        var maxExtended = (uint)X86Base.CpuId(unchecked((int)0x80000000), 0).Eax;
        if (maxExtended >= 0x80000004)
        {
            brand = CpuIdDecoder.DecodeBrand(
            [
                X86Base.CpuId(unchecked((int)0x80000002), 0),
                X86Base.CpuId(unchecked((int)0x80000003), 0),
                X86Base.CpuId(unchecked((int)0x80000004), 0),
            ]);
        }

        // Leaf 0x16 (Intel 6ª gen+): EAX = frequenza base, EBX = massima, in MHz. Zero se non disponibile.
        int? baseMhz = null, maxMhz = null;
        if (maxLeaf >= 0x16)
        {
            var leaf16 = X86Base.CpuId(0x16, 0);
            baseMhz = (leaf16.Eax & 0xFFFF) is > 0 and var b ? b : null;
            maxMhz = (leaf16.Ebx & 0xFFFF) is > 0 and var m ? m : null;
        }

        return new CpuIdSnapshot(
            CpuIdDecoder.DecodeVendor(leaf0.Ebx, leaf0.Edx, leaf0.Ecx),
            brand,
            (uint)leaf1.Eax,
            baseMhz,
            maxMhz,
            Hypervisor: (leaf1.Ecx & (1 << 31)) != 0);
    }

    private static ProcessorTopology? TryReadTopology()
    {
        uint length = 0;
        NativeMethods.GetLogicalProcessorInformationEx(NativeMethods.RelationAll, IntPtr.Zero, ref length);
        if (length == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!NativeMethods.GetLogicalProcessorInformationEx(NativeMethods.RelationAll, buffer, ref length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var bytes = new byte[length];
            Marshal.Copy(buffer, bytes, 0, (int)length);
            var topology = ProcessorTopologyParser.Parse(bytes);
            return topology.PhysicalCores > 0 ? topology : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ProcessorTopology ReadTopologyFromWmi()
    {
        var rows = Wmi.Query(Wmi.Cimv2, "SELECT NumberOfCores, NumberOfLogicalProcessors, L3CacheSize FROM Win32_Processor");
        var cores = rows.Sum(r => r.Int("NumberOfCores") ?? 0);
        var threads = rows.Sum(r => r.Int("NumberOfLogicalProcessors") ?? 0);
        var l3 = rows.Sum(r => (r.Long("L3CacheSize") ?? 0) * 1024);
        return new ProcessorTopology(
            Packages: Math.Max(rows.Count, 1),
            PhysicalCores: cores,
            LogicalProcessors: threads,
            PerformanceCores: cores,
            EfficiencyCores: 0,
            HasSmt: threads > cores,
            L3CacheBytes: l3 > 0 ? l3 : null);
    }
}
