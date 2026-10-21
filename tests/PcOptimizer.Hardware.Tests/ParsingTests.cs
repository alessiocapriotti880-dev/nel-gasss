using System.Buffers.Binary;
using System.Net.NetworkInformation;
using System.Text;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Hardware.Parsing;

namespace PcOptimizer.Hardware.Tests;

public sealed class ParsingTests
{
    [Theory]
    [InlineData(0x000B0671u, 6, 0xB7, 1)]   // i9-14900K (Raptor Lake B0)
    [InlineData(0x000506E3u, 6, 0x5E, 3)]   // i5-6500 (Skylake)
    [InlineData(0x000906EAu, 6, 0x9E, 10)]  // i7-8700K (Coffee Lake)
    [InlineData(0x00870F10u, 0x17, 0x71, 0)] // Ryzen 5 3600 (Matisse)
    [InlineData(0x00A60F12u, 0x19, 0x61, 2)] // Ryzen 7 7800X3D (Raphael)
    [InlineData(0x00B40F40u, 0x1A, 0x44, 0)] // Ryzen 9000 (Granite Ridge)
    public void Cpuid_signature_is_decoded_to_display_family_and_model(uint eax, int family, int model, int stepping) =>
        Assert.Equal((family, model, stepping), CpuIdDecoder.DecodeSignature(eax));

    [Fact]
    public void Cpuid_vendor_uses_ebx_edx_ecx_order()
    {
        static int Reg(string s) => BitConverter.ToInt32(Encoding.ASCII.GetBytes(s));
        Assert.Equal("GenuineIntel", CpuIdDecoder.DecodeVendor(Reg("Genu"), Reg("ineI"), Reg("ntel")));
        Assert.Equal(CpuVendor.Amd, CpuIdDecoder.ParseVendor("AuthenticAMD"));
    }

    [Fact]
    public void Cpuid_brand_string_is_trimmed_and_spaces_collapsed()
    {
        var bytes = new byte[48];
        Encoding.ASCII.GetBytes("  Intel(R) Core(TM)  i5-6500 CPU @ 3.20GHz").CopyTo(bytes, 0);
        var leaves = Enumerable.Range(0, 3).Select(i => (
            BitConverter.ToInt32(bytes, i * 16), BitConverter.ToInt32(bytes, i * 16 + 4),
            BitConverter.ToInt32(bytes, i * 16 + 8), BitConverter.ToInt32(bytes, i * 16 + 12)));

        Assert.Equal("Intel(R) Core(TM) i5-6500 CPU @ 3.20GHz", CpuIdDecoder.DecodeBrand(leaves));
    }

    [Fact]
    public void Registry_identifier_is_parsed_as_fallback()
    {
        Assert.Equal((6, 183, 1), CpuIdDecoder.ParseRegistryIdentifier("Intel64 Family 6 Model 183 Stepping 1"));
        Assert.Equal((25, 97, 2), CpuIdDecoder.ParseRegistryIdentifier("AMD64 Family 25 Model 97 Stepping 2"));
        Assert.Null(CpuIdDecoder.ParseRegistryIdentifier("boh"));
    }

    [Fact]
    public void Topology_of_hybrid_cpu_separates_p_and_e_cores()
    {
        // i9-14900K: 8 core P con SMT (2 thread, classe 1) + 16 core E (1 thread, classe 0), L3 36 MB.
        var buffer = new List<byte>();
        var cpu = 0;
        for (var i = 0; i < 8; i++, cpu += 2)
        {
            buffer.AddRange(CoreEntry(efficiencyClass: 1, smt: true, mask: 0b11UL << cpu));
        }

        for (var i = 0; i < 16; i++, cpu++)
        {
            buffer.AddRange(CoreEntry(efficiencyClass: 0, smt: false, mask: 1UL << cpu));
        }

        buffer.AddRange(CacheEntry(level: 2, size: 2 * 1024 * 1024));
        buffer.AddRange(CacheEntry(level: 3, size: 36 * 1024 * 1024));
        buffer.AddRange(PackageEntry());

        var topology = ProcessorTopologyParser.Parse(buffer.ToArray());

        Assert.Equal(new ProcessorTopology(1, 24, 32, 8, 16, true, 36L * 1024 * 1024), topology);
    }

    [Fact]
    public void Topology_of_classic_cpu_has_no_e_cores()
    {
        var buffer = Enumerable.Range(0, 4).SelectMany(i => CoreEntry(0, false, 1UL << i)).ToArray();

        var topology = ProcessorTopologyParser.Parse(buffer);

        Assert.Equal((4, 4, 4, 0, false), (topology.PhysicalCores, topology.LogicalProcessors,
            topology.PerformanceCores, topology.EfficiencyCores, topology.HasSmt));
    }

    [Fact]
    public void Truncated_topology_buffer_does_not_throw()
    {
        var entry = CoreEntry(0, false, 1);
        var buffer = entry.Concat(entry.Take(20)).ToArray();

        Assert.Equal(1, ProcessorTopologyParser.Parse(buffer).PhysicalCores);
    }

    [Fact]
    public void Microcode_revision_uses_high_dword_on_intel_and_low_dword_on_amd()
    {
        Assert.Equal(0x12Bu, MicrocodeParser.Parse(CpuVendor.Intel, [0, 0, 0, 0, 0x2B, 0x01, 0, 0]));
        Assert.Equal(0x0A201052u, MicrocodeParser.Parse(CpuVendor.Amd, [0x52, 0x10, 0x20, 0x0A, 0, 0, 0, 0]));
        Assert.Null(MicrocodeParser.Parse(CpuVendor.Intel, new byte[8]));
        // Hyper-V riporta 0xFFFFFFFF al posto della revisione reale (visto nello smoke test della CI).
        Assert.Null(MicrocodeParser.Parse(CpuVendor.Amd, [0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0]));
        Assert.Null(MicrocodeParser.Parse(CpuVendor.Intel, [0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF]));
        Assert.Null(MicrocodeParser.Parse(CpuVendor.Intel, null));
    }

    [Fact]
    public void Pci_ids_are_extracted_from_pnp_and_adapter_paths()
    {
        Assert.Equal(("10DE", "2704"), PciIdParser.Parse(@"PCI\VEN_10DE&DEV_2704&SUBSYS_88A01043&REV_A1\4&2B6B2ACD&0&0008"));
        Assert.Equal(("8086", "A780"), PciIdParser.Parse(@"\\?\PCI#VEN_8086&DEV_A780&SUBSYS_86941043&REV_04#3&11583659&0&10#{5b45201d}"));
        Assert.Equal((null, null), PciIdParser.Parse(@"ROOT\DISPLAY\0000"));
        Assert.Equal(GpuVendor.Amd, PciIdParser.GpuVendorFromPci("1002"));
    }

    [Theory]
    [InlineData("32.0.15.6094", "560.94")]
    [InlineData("31.0.15.3623", "536.23")]
    [InlineData("27.21.14.5671", "456.71")]
    [InlineData("30.0.14.7212", "472.12")]
    [InlineData("non valido", null)]
    public void Nvidia_driver_version_is_converted(string windows, string? expected) =>
        Assert.Equal(expected, DriverVersionParser.NvidiaFromWindows(windows));

    [Theory]
    [InlineData(1600, MemoryType.Ddr4, 3200)] // BIOS che riporta il clock reale invece dei MT/s
    [InlineData(3200, MemoryType.Ddr4, 3200)]
    [InlineData(2400, MemoryType.Ddr5, 4800)]
    [InlineData(1800, MemoryType.Ddr4, 3600)]
    [InlineData(3000, MemoryType.Ddr5, 6000)]
    [InlineData(800, MemoryType.Ddr3, 1600)]
    [InlineData(667, MemoryType.Ddr3, 1334)]
    [InlineData(0, MemoryType.Ddr4, null)]
    public void Memory_data_rate_is_normalized(int value, MemoryType type, int? expected) =>
        Assert.Equal(expected, MemoryParser.NormalizeDataRate(value, type));

    [Theory]
    [InlineData("DIMM_A2", "BANK 1", "A")]
    [InlineData("DDR4_B1", null, "B")]
    [InlineData("ChannelA-DIMM0", "BANK 0", "A")]
    [InlineData("DIMM 1", "P0 CHANNEL B", "B")]
    [InlineData("DIMM 0", "P0_Node0_Channel1_Dimm0", "B")]
    [InlineData("Controller1-ChannelA-DIMM0", null, "C1-A")]
    [InlineData("A1", "BANK 0", "A")]
    [InlineData("DIMM1", "", null)]
    [InlineData("DIMM 0", "BANK 0", null)]
    public void Memory_channel_is_parsed_from_slot_names(string? locator, string? bank, string? expected) =>
        Assert.Equal(expected, MemoryParser.ParseChannel(locator, bank));

    [Theory]
    [InlineData("ROG STRIX Z790-E GAMING WIFI", "Z790")]
    [InlineData("PRIME B450M-A", "B450")]
    [InlineData("H110M-K", "H110")]
    [InlineData("X670E AORUS MASTER", "X670")]
    [InlineData("AB350 Gaming 3", "B350")]
    [InlineData("A520M-HDV", "A520")]
    [InlineData("TRX40 AORUS XTREME", "TRX40")]
    [InlineData("MAG B660M MORTAR WIFI DDR4 (MS-7D42)", "B660")]
    [InlineData("B450 TOMAHAWK MAX (MS-7C02)", "B450")]
    [InlineData("0K240Y", null)]
    [InlineData("8767", null)]
    public void Chipset_is_extracted_from_board_name(string product, string? expected) =>
        Assert.Equal(expected, BoardParser.ExtractChipset(product));

    [Theory]
    [InlineData("ASUSTeK COMPUTER INC.", BoardVendor.Asus)]
    [InlineData("Micro-Star International Co., Ltd.", BoardVendor.Msi)]
    [InlineData("Gigabyte Technology Co., Ltd.", BoardVendor.Gigabyte)]
    [InlineData("ASRock", BoardVendor.AsRock)]
    [InlineData("Dell Inc.", BoardVendor.Dell)]
    [InlineData("HP", BoardVendor.Hp)]
    [InlineData("LENOVO", BoardVendor.Lenovo)]
    [InlineData("NZXT", BoardVendor.Other)]
    [InlineData("", BoardVendor.Unknown)]
    public void Board_vendor_is_normalized(string manufacturer, BoardVendor expected) =>
        Assert.Equal(expected, BoardParser.ParseVendor(manufacturer));

    [Fact]
    public void Virtual_machines_are_recognized()
    {
        Assert.True(BoardParser.LooksVirtualMachine("Virtual Machine", "Microsoft Corporation"));
        Assert.True(BoardParser.LooksVirtualMachine(null, "VMware, Inc."));
        Assert.True(BoardParser.LooksVirtualMachine("Hyper-V UEFI Release v4.1"));
        Assert.False(BoardParser.LooksVirtualMachine("ROG STRIX Z790-E GAMING WIFI", "ASUSTeK COMPUTER INC.", "American Megatrends Inc."));
    }

    [Fact]
    public void Laptops_are_detected_from_system_type_or_chassis()
    {
        Assert.True(FormFactorParser.IsLaptop(2, []));
        Assert.True(FormFactorParser.IsLaptop(1, [10]));
        Assert.True(FormFactorParser.IsLaptop(null, [31]));
        Assert.False(FormFactorParser.IsLaptop(1, [3]));
    }

    [Fact]
    public void Storage_network_and_display_codes_are_mapped()
    {
        Assert.Equal(DiskMediaType.Hdd, StorageParser.MediaType(3));
        Assert.Equal(DiskBusType.Nvme, StorageParser.BusType(17));
        Assert.Equal(DiskBusType.Usb, StorageParser.BusType(7));
        Assert.Equal(DiskHealth.Unhealthy, StorageParser.Health(2));
        Assert.Equal(NetworkMedium.WiFi, NetworkParser.FromInterfaceType(NetworkInterfaceType.Wireless80211));
        Assert.Equal(NetworkMedium.Ethernet, NetworkParser.FromNdisMedium(14));
        Assert.True(NetworkParser.LooksVirtual("Hyper-V Virtual Ethernet Adapter"));
        Assert.False(NetworkParser.LooksVirtual("Realtek PCIe 2.5GbE Family Controller"));
        Assert.Equal(DisplayConnection.Hdmi, DisplayParser.Connection(5));
        Assert.Equal(DisplayConnection.Internal, DisplayParser.Connection(unchecked((int)0x80000000)));
    }

    // --- Costruzione di voci SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX come le restituisce Windows ---

    private static byte[] CoreEntry(byte efficiencyClass, bool smt, ulong mask)
    {
        var entry = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(entry, 0);            // RelationProcessorCore
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(4), 48); // Size
        entry[8] = (byte)(smt ? 1 : 0);                               // Flags (LTP_PC_SMT)
        entry[9] = efficiencyClass;
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(30), 1); // GroupCount
        BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(32), mask);
        return entry;
    }

    private static byte[] CacheEntry(byte level, uint size)
    {
        var entry = new byte[56];
        BinaryPrimitives.WriteInt32LittleEndian(entry, 2);            // RelationCache
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(4), 56);
        entry[8] = level;
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(12), size);
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(16), 0); // CacheUnified
        return entry;
    }

    private static byte[] PackageEntry()
    {
        var entry = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(entry, 3);            // RelationProcessorPackage
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(4), 48);
        return entry;
    }
}
