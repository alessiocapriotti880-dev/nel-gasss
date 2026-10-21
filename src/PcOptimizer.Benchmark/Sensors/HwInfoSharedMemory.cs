using System.Buffers.Binary;
using System.Text;

namespace PcOptimizer.Benchmark.Sensors;

/// <summary>Tipo di lettura di HWiNFO (SENSOR_READING_TYPE).</summary>
public enum HwInfoReadingType
{
    None = 0,
    Temperature = 1,
    Voltage = 2,
    Fan = 3,
    Current = 4,
    Power = 5,
    Clock = 6,
    Usage = 7,
    Other = 8,
}

/// <param name="Sensor">Nome originale (inglese) del sensore, es. "CPU [#0]: Intel Core i7-12700K: DTS".</param>
/// <param name="Label">Etichetta originale (inglese) della lettura, stabile anche se l'utente la rinomina.</param>
public sealed record HwInfoReading(HwInfoReadingType Type, string Sensor, string Label, string Unit, double Value, double Min, double Max, double Average);

/// <summary>
/// Decodifica della memoria condivisa di HWiNFO ("Global\HWiNFO_SENS_SM2"), disponibile quando HWiNFO è aperto con
/// "Shared Memory Support" attivo. Lettura passiva: l'app non installa driver propri per i sensori della CPU.
/// Formato (pack 1): intestazione con offset e dimensione delle sezioni, poi sensori e letture a dimensione fissa.
/// </summary>
public static class HwInfoSharedMemory
{
    public const string MapName = @"Global\HWiNFO_SENS_SM2";

    /// <summary>"HWiS" in little endian; "DEAD" quando HWiNFO ha chiuso la memoria condivisa.</summary>
    public const uint Signature = 0x53695748;

    public const int HeaderSize = 44;
    private const int StringLength = 128;
    private const int UnitLength = 16;

    /// <summary>Dimensione totale da leggere ricavata dall'intestazione, o null se l'intestazione non è valida.</summary>
    public static long? TotalSize(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(header) != Signature)
        {
            return null;
        }

        var sensorEnd = (long)U32(header, 20) + (long)U32(header, 24) * U32(header, 28);
        var readingEnd = (long)U32(header, 32) + (long)U32(header, 36) * U32(header, 40);
        var total = Math.Max(sensorEnd, readingEnd);
        return total is > HeaderSize and < 64 * 1024 * 1024 ? total : null;
    }

    public static IReadOnlyList<HwInfoReading> Parse(ReadOnlySpan<byte> data)
    {
        if (TotalSize(data) is not { } total || data.Length < total)
        {
            return [];
        }

        var sensorOffset = (int)U32(data, 20);
        var sensorSize = (int)U32(data, 24);
        var sensorCount = (int)U32(data, 28);
        var readingOffset = (int)U32(data, 32);
        var readingSize = (int)U32(data, 36);
        var readingCount = (int)U32(data, 40);
        if (sensorSize < 8 + StringLength || readingSize < 12 + 2 * StringLength + UnitLength + 32)
        {
            return [];
        }

        var sensors = new string[sensorCount];
        for (var i = 0; i < sensorCount; i++)
        {
            sensors[i] = Ansi(data.Slice(sensorOffset + i * sensorSize + 8, StringLength));
        }

        var readings = new List<HwInfoReading>(readingCount);
        for (var i = 0; i < readingCount; i++)
        {
            var r = data.Slice(readingOffset + i * readingSize, readingSize);
            var type = (HwInfoReadingType)U32(r, 0);
            var sensorIndex = (int)U32(r, 4);
            var label = Ansi(r.Slice(12, StringLength));
            var unit = Ansi(r.Slice(12 + 2 * StringLength, UnitLength));
            var values = 12 + 2 * StringLength + UnitLength;
            readings.Add(new HwInfoReading(
                type,
                sensorIndex >= 0 && sensorIndex < sensorCount ? sensors[sensorIndex] : "",
                label,
                unit,
                BinaryPrimitives.ReadDoubleLittleEndian(r[values..]),
                BinaryPrimitives.ReadDoubleLittleEndian(r[(values + 8)..]),
                BinaryPrimitives.ReadDoubleLittleEndian(r[(values + 16)..]),
                BinaryPrimitives.ReadDoubleLittleEndian(r[(values + 24)..])));
        }

        return readings;
    }

    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    private static string Ansi(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end >= 0 ? bytes[..end] : bytes).Trim();
    }
}

/// <summary>Scelta delle letture utili fra le centinaia esposte da HWiNFO, in base alle etichette originali.</summary>
public static class HwInfoSelectors
{
    // In ordine di preferenza: package Intel, Tctl/Tdie AMD, media del die, core più caldo.
    private static readonly string[] CpuTempLabels = ["CPU Package", "CPU (Tctl/Tdie)", "CPU Die (average)", "Core Max", "CPU Tctl", "CPU"];
    private static readonly string[] CpuPowerLabels = ["CPU Package Power", "CPU PPT"];
    private static readonly string[] VcoreLabels = ["Vcore", "CPU Core Voltage (SVI3 TFN)", "CPU Core Voltage (SVI2 TFN)", "CPU Core Voltage", "VCORE"];

    public static double? CpuTemperature(IReadOnlyList<HwInfoReading> readings) =>
        First(readings, HwInfoReadingType.Temperature, CpuTempLabels, preferCpuSensor: true);

    public static double? CpuPackagePower(IReadOnlyList<HwInfoReading> readings) =>
        First(readings, HwInfoReadingType.Power, CpuPowerLabels, preferCpuSensor: true);

    /// <summary>Tensione reale del core (dal sensore della scheda madre o SVI), non il VID richiesto.</summary>
    public static double? CpuCoreVoltage(IReadOnlyList<HwInfoReading> readings) =>
        First(readings, HwInfoReadingType.Voltage, VcoreLabels, preferCpuSensor: false);

    /// <summary>true se una qualsiasi lettura di "Thermal Throttling" della CPU vale Sì (1).</summary>
    public static bool? CpuThermalThrottling(IReadOnlyList<HwInfoReading> readings)
    {
        var flags = readings.Where(r => r.Label.Contains("Thermal Throttling", StringComparison.OrdinalIgnoreCase) &&
                                        r.Sensor.StartsWith("CPU", StringComparison.OrdinalIgnoreCase)).ToList();
        return flags.Count == 0 ? null : flags.Any(r => r.Value >= 1);
    }

    /// <summary>Errori hardware WHEA contati da HWiNFO (utile nei test di stabilità di overclock e undervolt).</summary>
    public static double? WheaErrors(IReadOnlyList<HwInfoReading> readings) =>
        readings.FirstOrDefault(r => r.Sensor.Contains("WHEA", StringComparison.OrdinalIgnoreCase) &&
                                     r.Label.Equals("Total Errors", StringComparison.OrdinalIgnoreCase))?.Value;

    private static double? First(IReadOnlyList<HwInfoReading> readings, HwInfoReadingType type, string[] labels, bool preferCpuSensor)
    {
        foreach (var label in labels)
        {
            var matches = readings.Where(r => r.Type == type && r.Label.Equals(label, StringComparison.OrdinalIgnoreCase)).ToList();
            var match = (preferCpuSensor ? matches.FirstOrDefault(r => r.Sensor.StartsWith("CPU", StringComparison.OrdinalIgnoreCase)) : null)
                        ?? matches.FirstOrDefault();
            if (match is not null && double.IsFinite(match.Value) && match.Value > 0)
            {
                return match.Value;
            }
        }

        return null;
    }
}
