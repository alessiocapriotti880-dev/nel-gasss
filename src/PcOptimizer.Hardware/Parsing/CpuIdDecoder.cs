using System.Text;
using System.Text.RegularExpressions;
using PcOptimizer.Hardware.Model;

namespace PcOptimizer.Hardware.Parsing;

/// <summary>Decodifica dei registri CPUID e dei dati CPU del registro di Windows.</summary>
public static partial class CpuIdDecoder
{
    /// <summary>
    /// Decodifica la "firma" (CPUID leaf 1, EAX) nei valori display di famiglia, modello e stepping,
    /// secondo le regole Intel/AMD: la famiglia estesa si somma solo se la famiglia base è 0xF,
    /// il modello esteso si usa per le famiglie 0x6 e 0xF.
    /// </summary>
    public static (int Family, int Model, int Stepping) DecodeSignature(uint eax)
    {
        var stepping = (int)(eax & 0xF);
        var baseModel = (int)((eax >> 4) & 0xF);
        var baseFamily = (int)((eax >> 8) & 0xF);
        var extModel = (int)((eax >> 16) & 0xF);
        var extFamily = (int)((eax >> 20) & 0xFF);

        var family = baseFamily == 0xF ? baseFamily + extFamily : baseFamily;
        var model = baseFamily is 0x6 or 0xF ? (extModel << 4) + baseModel : baseModel;
        return (family, model, stepping);
    }

    /// <summary>Stringa del produttore da CPUID leaf 0: l'ordine dei registri è EBX, EDX, ECX.</summary>
    public static string DecodeVendor(int ebx, int edx, int ecx)
    {
        var bytes = new byte[12];
        BitConverter.GetBytes(ebx).CopyTo(bytes, 0);
        BitConverter.GetBytes(edx).CopyTo(bytes, 4);
        BitConverter.GetBytes(ecx).CopyTo(bytes, 8);
        return Encoding.ASCII.GetString(bytes);
    }

    /// <summary>Nome commerciale dalle leaf 0x80000002-0x80000004 (16 byte ciascuna).</summary>
    public static string DecodeBrand(IEnumerable<(int Eax, int Ebx, int Ecx, int Edx)> leaves)
    {
        var bytes = new List<byte>(48);
        foreach (var (eax, ebx, ecx, edx) in leaves)
        {
            bytes.AddRange(BitConverter.GetBytes(eax));
            bytes.AddRange(BitConverter.GetBytes(ebx));
            bytes.AddRange(BitConverter.GetBytes(ecx));
            bytes.AddRange(BitConverter.GetBytes(edx));
        }

        return NormalizeSpaces(Encoding.ASCII.GetString(bytes.ToArray()).Replace('\0', ' '));
    }

    public static CpuVendor ParseVendor(string? vendorId) => vendorId switch
    {
        "GenuineIntel" => CpuVendor.Intel,
        "AuthenticAMD" or "AMDisbetter!" => CpuVendor.Amd,
        _ => CpuVendor.Unknown,
    };

    /// <summary>
    /// Ripiego quando CPUID non è disponibile: il registro contiene
    /// "Intel64 Family 6 Model 183 Stepping 1" con valori già in forma display (decimali).
    /// </summary>
    public static (int Family, int Model, int Stepping)? ParseRegistryIdentifier(string? identifier)
    {
        if (identifier is null)
        {
            return null;
        }

        var m = IdentifierRegex().Match(identifier);
        return m.Success
            ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value))
            : null;
    }

    public static string NormalizeSpaces(string text) => SpacesRegex().Replace(text, " ").Trim();

    [GeneratedRegex(@"Family\s+(\d+)\s+Model\s+(\d+)\s+Stepping\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
