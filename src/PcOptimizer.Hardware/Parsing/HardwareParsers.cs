using System.Text.RegularExpressions;
using PcOptimizer.Hardware.Model;

namespace PcOptimizer.Hardware.Parsing;

/// <summary>Revisione del microcode dal valore di registro "Update Revision".</summary>
public static class MicrocodeParser
{
    /// <summary>
    /// Windows salva il valore a 64 bit del registro MSR 0x8B (REG_BINARY, little-endian).
    /// Intel tiene la revisione nei 32 bit alti, AMD (PATCH_LEVEL) nei 32 bit bassi.
    /// Restituisce null se il valore manca o è zero (tipico delle macchine virtuali).
    /// </summary>
    public static uint? Parse(CpuVendor vendor, byte[]? raw)
    {
        if (raw is null || raw.Length < 4)
        {
            return null;
        }

        uint value;
        if (raw.Length >= 8)
        {
            var low = BitConverter.ToUInt32(raw, 0);
            var high = BitConverter.ToUInt32(raw, 4);
            value = vendor switch
            {
                CpuVendor.Intel => high,
                CpuVendor.Amd => low,
                _ => high != 0 ? high : low,
            };
        }
        else
        {
            value = BitConverter.ToUInt32(raw, 0);
        }

        return value == 0 ? null : value;
    }
}

/// <summary>ID PCI (produttore e dispositivo) da stringhe PnP come "PCI\VEN_10DE&amp;DEV_2704&amp;...".</summary>
public static partial class PciIdParser
{
    public static (string? VendorId, string? DeviceId) Parse(string? text)
    {
        if (text is null)
        {
            return (null, null);
        }

        var m = PciRegex().Match(text);
        return m.Success ? (m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value.ToUpperInvariant()) : (null, null);
    }

    public static GpuVendor GpuVendorFromPci(string? vendorId) => vendorId?.ToUpperInvariant() switch
    {
        null => GpuVendor.Unknown,
        "10DE" => GpuVendor.Nvidia,
        "1002" or "1022" => GpuVendor.Amd,
        "8086" => GpuVendor.Intel,
        "1414" => GpuVendor.Microsoft,
        _ => GpuVendor.Other,
    };

    [GeneratedRegex(@"VEN_([0-9A-F]{4})&DEV_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex PciRegex();
}

/// <summary>Conversione delle versioni driver dal formato Windows a quello del produttore.</summary>
public static class DriverVersionParser
{
    /// <summary>
    /// NVIDIA: le ultime 5 cifre di (terzo campo + quarto campo a 4 cifre) formano la versione.
    /// Es. "32.0.15.6094" → "15" + "6094" → "56094" → "560.94".
    /// </summary>
    public static string? NvidiaFromWindows(string? windowsVersion)
    {
        var parts = windowsVersion?.Split('.');
        if (parts is not { Length: 4 } || !parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)))
        {
            return null;
        }

        var combined = parts[2] + parts[3].PadLeft(4, '0');
        if (combined.Length < 5)
        {
            return null;
        }

        var last5 = combined[^5..];
        return $"{last5[..3]}.{last5[3..]}";
    }
}

/// <summary>Interpretazione dei dati sui moduli RAM forniti dal firmware (SMBIOS tramite WMI).</summary>
public static partial class MemoryParser
{
    /// <summary>Tipo di memoria dal codice SMBIOS (Win32_PhysicalMemory.SMBIOSMemoryType).</summary>
    public static MemoryType FromSmbiosType(int? code) => code switch
    {
        24 => MemoryType.Ddr3,
        26 => MemoryType.Ddr4,
        29 => MemoryType.Lpddr3,
        30 => MemoryType.Lpddr4,
        34 => MemoryType.Ddr5,
        35 => MemoryType.Lpddr5,
        _ => MemoryType.Unknown,
    };

    /// <summary>
    /// I BIOS con SMBIOS precedente alla 3.1.1 riportano il clock reale (MHz) invece della velocità
    /// di trasferimento (MT/s): 1600 invece di 3200 per una DDR4-3200. Se il valore è sotto la
    /// velocità minima usata in pratica per quel tipo di memoria, lo raddoppiamo.
    /// </summary>
    public static int? NormalizeDataRate(int? value, MemoryType type)
    {
        if (value is null or <= 0)
        {
            return null;
        }

        var minimum = type switch
        {
            MemoryType.Ddr3 => 1066,
            MemoryType.Lpddr3 => 1600,
            MemoryType.Ddr4 => 2133,
            MemoryType.Lpddr4 => 3200,
            MemoryType.Ddr5 or MemoryType.Lpddr5 => 4400,
            _ => 0,
        };
        return value < minimum && value * 2 >= minimum ? value * 2 : value;
    }

    /// <summary>
    /// Ricava il canale di memoria dal nome dello slot. Formati comuni:
    /// "DIMM_A2" (ASUS), "DDR4_B1" (Gigabyte), "ChannelA-DIMM0" (portatili/Intel),
    /// "P0 CHANNEL A" (AMD), "P0_Node0_Channel1_Dimm0" (ASRock), "A1".
    /// Restituisce null se non interpretabile. Il controller (sistemi con più controller)
    /// fa parte della chiave, così "Controller0-ChannelA" e "Controller1-ChannelA" restano distinti.
    /// </summary>
    public static string? ParseChannel(string? deviceLocator, string? bankLabel)
    {
        foreach (var text in new[] { deviceLocator, bankLabel })
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var controller = ControllerRegex().Match(text) is { Success: true } c ? $"C{c.Groups[1].Value}-" : "";

            if (ChannelLetterRegex().Match(text) is { Success: true } letter)
            {
                return controller + letter.Groups[1].Value.ToUpperInvariant();
            }

            if (ChannelNumberRegex().Match(text) is { Success: true } number)
            {
                return controller + (char)('A' + int.Parse(number.Groups[1].Value));
            }

            if (SlotLetterRegex().Match(text) is { Success: true } slot)
            {
                return controller + slot.Groups[1].Value.ToUpperInvariant();
            }

            if (BareSlotRegex().Match(text.Trim()) is { Success: true } bare)
            {
                return controller + bare.Groups[1].Value.ToUpperInvariant();
            }
        }

        return null;
    }

    [GeneratedRegex(@"CONTROLLER\s*(\d)", RegexOptions.IgnoreCase)]
    private static partial Regex ControllerRegex();

    [GeneratedRegex(@"CHANNEL\s*([A-H])(?![A-Z])", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelLetterRegex();

    [GeneratedRegex(@"CHANNEL\s*(\d)", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelNumberRegex();

    [GeneratedRegex(@"(?:DIMM|DDR\d?)[\s_-]*([A-H])\d", RegexOptions.IgnoreCase)]
    private static partial Regex SlotLetterRegex();

    [GeneratedRegex(@"^([A-H])\d$", RegexOptions.IgnoreCase)]
    private static partial Regex BareSlotRegex();
}

/// <summary>Produttore e chipset della scheda madre.</summary>
public static partial class BoardParser
{
    public static BoardVendor ParseVendor(string? manufacturer)
    {
        var m = manufacturer?.ToUpperInvariant() ?? "";
        return m switch
        {
            _ when m.Contains("ASUSTEK") || m == "ASUS" => BoardVendor.Asus,
            _ when m.Contains("MICRO-STAR") || m.Contains("MSI") => BoardVendor.Msi,
            _ when m.Contains("GIGABYTE") || m.Contains("AORUS") => BoardVendor.Gigabyte,
            _ when m.Contains("ASROCK") => BoardVendor.AsRock,
            _ when m.Contains("BIOSTAR") => BoardVendor.Biostar,
            _ when m.Contains("DELL") || m.Contains("ALIENWARE") => BoardVendor.Dell,
            _ when m.StartsWith("HP") || m.Contains("HEWLETT") => BoardVendor.Hp,
            _ when m.Contains("LENOVO") => BoardVendor.Lenovo,
            _ when m.Contains("ACER") => BoardVendor.Acer,
            "" => BoardVendor.Unknown,
            _ => BoardVendor.Other,
        };
    }

    /// <summary>
    /// Estrae il chipset dal nome commerciale della scheda: "ROG STRIX Z790-E GAMING WIFI" → "Z790",
    /// "PRIME B450M-A" → "B450", "AB350 Gaming 3" → "B350", "TRX40 AORUS XTREME" → "TRX40".
    /// </summary>
    public static string? ExtractChipset(string? product)
    {
        if (string.IsNullOrWhiteSpace(product))
        {
            return null;
        }

        var m = ChipsetRegex().Match(product.ToUpperInvariant());
        return m.Success ? m.Groups[1].Value + m.Groups[2].Value : null;
    }

    // Una "A" iniziale opzionale copre la nomenclatura Gigabyte per AMD (AB350, AX370).
    [GeneratedRegex(@"(?<![A-Z0-9])A?(TRX|WRX|[ABHQWXZ])(\d{2,3})(?!\d)")]
    private static partial Regex ChipsetRegex();
}

/// <summary>Riconoscimento dei portatili.</summary>
public static class FormFactorParser
{
    // SMBIOS Chassis Types: 8 Portable, 9 Laptop, 10 Notebook, 11 Hand Held, 14 Sub Notebook,
    // 30 Tablet, 31 Convertible, 32 Detachable.
    private static readonly HashSet<int> PortableChassis = [8, 9, 10, 11, 14, 30, 31, 32];

    /// <param name="pcSystemType">Win32_ComputerSystem.PCSystemType: 2 = Mobile.</param>
    public static bool IsLaptop(int? pcSystemType, IReadOnlyCollection<int> chassisTypes) =>
        pcSystemType == 2 || chassisTypes.Any(PortableChassis.Contains);
}

/// <summary>Codici numerici di MSFT_PhysicalDisk (Storage Management API).</summary>
public static class StorageParser
{
    public static DiskMediaType MediaType(int? code) => code switch
    {
        3 => DiskMediaType.Hdd,
        4 => DiskMediaType.Ssd,
        5 => DiskMediaType.Scm,
        _ => DiskMediaType.Unknown,
    };

    public static DiskBusType BusType(int? code) => code switch
    {
        1 => DiskBusType.Scsi,
        3 or 11 => DiskBusType.Sata,
        7 => DiskBusType.Usb,
        8 => DiskBusType.Raid,
        10 => DiskBusType.Sas,
        12 => DiskBusType.Sd,
        13 => DiskBusType.Mmc,
        14 or 15 => DiskBusType.Virtual,
        17 => DiskBusType.Nvme,
        null or 0 => DiskBusType.Unknown,
        _ => DiskBusType.Other,
    };

    public static DiskHealth Health(int? code) => code switch
    {
        0 => DiskHealth.Healthy,
        1 => DiskHealth.Warning,
        2 => DiskHealth.Unhealthy,
        _ => DiskHealth.Unknown,
    };
}

/// <summary>Tipo di collegamento di rete.</summary>
public static class NetworkParser
{
    public static NetworkMedium FromInterfaceType(System.Net.NetworkInformation.NetworkInterfaceType type) => type switch
    {
        System.Net.NetworkInformation.NetworkInterfaceType.Ethernet or
        System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet or
        System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetT or
        System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetFx or
        System.Net.NetworkInformation.NetworkInterfaceType.Ethernet3Megabit => NetworkMedium.Ethernet,
        System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 => NetworkMedium.WiFi,
        System.Net.NetworkInformation.NetworkInterfaceType.Unknown => NetworkMedium.Unknown,
        _ => NetworkMedium.Other,
    };

    /// <summary>NDIS_PHYSICAL_MEDIUM di MSFT_NetAdapter: 14 = 802.3 (Ethernet), 9/1 = Wi-Fi.</summary>
    public static NetworkMedium FromNdisMedium(int? medium) => medium switch
    {
        14 => NetworkMedium.Ethernet,
        1 or 9 => NetworkMedium.WiFi,
        null or 0 => NetworkMedium.Unknown,
        _ => NetworkMedium.Other,
    };

    private static readonly string[] VirtualMarkers =
    [
        "virtual", "hyper-v", "vmware", "virtualbox", "tap-", "wireguard", "bluetooth", "npcap", "loopback",
        "wan miniport", "teredo", "zerotier", "tailscale", "openvpn", "vpn", "wi-fi direct",
    ];

    /// <summary>Riconosce schede virtuali (VPN, macchine virtuali, Bluetooth) quando WMI non è disponibile.</summary>
    public static bool LooksVirtual(string? description) =>
        description is not null && VirtualMarkers.Any(m => description.Contains(m, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Tecnologia di uscita video (DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY).</summary>
public static class DisplayParser
{
    public static DisplayConnection Connection(int outputTechnology) => outputTechnology switch
    {
        0 => DisplayConnection.Vga,
        4 => DisplayConnection.Dvi,
        5 => DisplayConnection.Hdmi,
        10 or 18 => DisplayConnection.DisplayPort,
        6 or 11 or 13 or unchecked((int)0x80000000) => DisplayConnection.Internal,
        15 => DisplayConnection.Wireless,
        16 or 17 => DisplayConnection.Indirect,
        _ => DisplayConnection.Other,
    };
}
