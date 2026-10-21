using System.Buffers.Binary;
using System.Text;
using PcOptimizer.Benchmark.Boot;
using PcOptimizer.Benchmark.Cpu;
using PcOptimizer.Benchmark.Network;
using PcOptimizer.Benchmark.Sensors;

namespace PcOptimizer.Benchmark.Tests;

public class SystemProbeTests
{
    [Fact]
    public void Ping_ComputesLossAndJitter()
    {
        var r = PingStatistics.Compute("192.168.1.1", "gateway", [1, 3, null, 2, 2]);

        Assert.Equal(5, r.Sent);
        Assert.Equal(4, r.Received);
        Assert.Equal(20, r.LossPercent, 9);
        Assert.Equal(2, r.RttMs.Median, 9);
        // Differenze consecutive fra le risposte: |3-1|, |2-3|, |2-2| → media 1.
        Assert.Equal(1, r.JitterMs!.Value, 9);
        Assert.Equal([2.0, 1, 0], r.JitterSamplesMs);
    }

    [Fact]
    public void Ping_AllLost()
    {
        var r = PingStatistics.Compute("1.1.1.1", "internet", [null, null]);
        Assert.Equal(100, r.LossPercent);
        Assert.Null(r.JitterMs);
        Assert.Equal(0, r.RttMs.Count);
    }

    [Fact]
    public void BootEvent_ParsesTimes()
    {
        const string xml = """
            <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
              <System><EventID>100</EventID><TimeCreated SystemTime='2024-05-02T08:01:30.1234567Z'/></System>
              <EventData>
                <Data Name='BootTsVersion'>2</Data>
                <Data Name='BootStartTime'>2024-05-02T08:00:00.0000000Z</Data>
                <Data Name='BootTime'>41250</Data>
                <Data Name='MainPathBootTime'>18000</Data>
                <Data Name='BootPostBootTime'>23250</Data>
                <Data Name='BootIsRebootAfterInstall'>false</Data>
              </EventData>
            </Event>
            """;

        var boot = BootEventParser.Parse(xml)!;

        Assert.Equal(41250, boot.BootMs);
        Assert.Equal(18000, boot.MainPathMs);
        Assert.Equal(23250, boot.PostBootMs);
        Assert.False(boot.AfterUpdate);
        Assert.Equal(new DateTimeOffset(2024, 5, 2, 8, 0, 0, TimeSpan.Zero), boot.Time);
        Assert.Null(BootEventParser.Parse("<Event/>"));
        Assert.Null(BootEventParser.Parse("non xml"));
    }

    [Fact]
    public void BusyPercent_UsesIdleOverKernelPlusUser()
    {
        var before = new SystemTimes(Idle: 1000, Kernel: 1500, User: 500);
        var after = new SystemTimes(Idle: 1750, Kernel: 2500, User: 500);

        // Totale 1000 (kernel include idle), idle 750 → 25% occupato.
        Assert.Equal(25, CpuMath.BusyPercent(before, after), 9);
        Assert.Equal(0, CpuMath.BusyPercent(before, before));
    }

    [Fact]
    public void TopProcesses_UsesDeltasAndHandlesPidReuse()
    {
        ProcessCpuTimes[] before = [new(0, 0, "Idle", 50_000_000), new(100, 1, "chrome.exe", 10_000_000), new(200, 5, "old.exe", 1_000)];
        ProcessCpuTimes[] after =
        [
            new(0, 0, "Idle", 90_000_000),
            new(100, 1, "chrome.exe", 14_000_000),   // +0,4 s
            new(200, 9, "new.exe", 2_000_000),        // stesso PID, processo nuovo: +0,2 s
            new(300, 3, "quiet.exe", 0),
        ];

        // 2 s su 4 processori = 8 s di capacità.
        var top = CpuMath.TopProcesses(before, after, 2, 4);

        Assert.Equal(["chrome.exe", "new.exe"], top.Select(p => p.Name));
        Assert.Equal(5, top[0].CpuPercent, 9);
        Assert.Equal(2.5, top[1].CpuPercent, 9);
    }

    [Fact]
    public void SystemProcessInfoParser_ReadsEntriesAndNames()
    {
        const long baseAddress = 0x7FF0_0000_0000;
        var buffer = new byte[400];
        WriteEntry(buffer, 0, next: 100, pid: 0, user: 0, kernel: 999, nameOffset: -1, name: "");
        WriteEntry(buffer, 100, next: 120, pid: 4, user: 10, kernel: 20, nameOffset: -1, name: "");
        WriteEntry(buffer, 220, next: 0, pid: 1234, user: 300, kernel: 200, nameOffset: 320, name: "game.exe");

        var entries = SystemProcessInfoParser.Parse(buffer, baseAddress);

        Assert.Equal(3, entries.Count);
        Assert.Equal("Idle", entries[0].Name);
        Assert.Equal("System", entries[1].Name);
        Assert.Equal(30, entries[1].CpuTime100ns);
        Assert.Equal(new ProcessCpuTimes(1234, 77, "game.exe", 500), entries[2]);

        void WriteEntry(byte[] b, int at, int next, long pid, long user, long kernel, int nameOffset, string name)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), (uint)next);
            BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(at + 32), 77);
            BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(at + 40), user);
            BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(at + 48), kernel);
            BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(at + 80), pid);
            if (nameOffset >= 0)
            {
                var bytes = Encoding.Unicode.GetBytes(name);
                bytes.CopyTo(b, nameOffset);
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at + 56), (ushort)bytes.Length);
                BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(at + 64), baseAddress + nameOffset);
            }
        }
    }

    [Fact]
    public void HwInfo_ParsesSharedMemoryAndSelectsCpuReadings()
    {
        var data = HwInfoImage(
            ["CPU [#0]: Intel Core i7-12700K: DTS", "ASUS PRIME Z690-P (Nuvoton NCT6798D)", "CPU [#0]: Intel Core i7-12700K: Enhanced", "Windows Hardware Errors (WHEA)"],
            [
                (HwInfoReadingType.Temperature, 1, "CPU", "°C", 41),
                (HwInfoReadingType.Temperature, 0, "Core Max", "°C", 70),
                (HwInfoReadingType.Temperature, 2, "CPU Package", "°C", 72),
                (HwInfoReadingType.Power, 2, "CPU Package Power", "W", 125.5),
                (HwInfoReadingType.Voltage, 1, "Vcore", "V", 1.256),
                (HwInfoReadingType.Other, 0, "Core Thermal Throttling", "Yes/No", 0),
                (HwInfoReadingType.Other, 2, "Package/Ring Thermal Throttling", "Yes/No", 1),
                (HwInfoReadingType.Other, 3, "Total Errors", "", 2),
            ]);

        var readings = HwInfoSharedMemory.Parse(data);

        Assert.Equal(8, readings.Count);
        Assert.Equal("CPU [#0]: Intel Core i7-12700K: Enhanced", readings[2].Sensor);
        Assert.Equal(72, HwInfoSelectors.CpuTemperature(readings));
        Assert.Equal(125.5, HwInfoSelectors.CpuPackagePower(readings));
        Assert.Equal(1.256, HwInfoSelectors.CpuCoreVoltage(readings));
        Assert.True(HwInfoSelectors.CpuThermalThrottling(readings));
        Assert.Equal(2, HwInfoSelectors.WheaErrors(readings));
    }

    [Fact]
    public void HwInfo_RejectsWrongSignature()
    {
        var data = HwInfoImage(["CPU"], [(HwInfoReadingType.Temperature, 0, "CPU Package", "°C", 50)]);
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0x44414544); // "DEAD"
        Assert.Empty(HwInfoSharedMemory.Parse(data));
        Assert.Null(HwInfoSharedMemory.TotalSize(data));
    }

    [Fact]
    public void NvmlLimits_DescribesOnlyRealLimits()
    {
        Assert.Empty(NvmlLimits.Describe(0x1 | 0x2));
        Assert.Equal(["limite di potenza", "temperatura (hardware)"], NvmlLimits.Describe(0x4 | 0x40 | 0x1));
    }

    /// <summary>Costruisce un'immagine della memoria condivisa di HWiNFO (layout pack 1, elementi da 264/316 byte).</summary>
    internal static byte[] HwInfoImage(string[] sensors, (HwInfoReadingType Type, int Sensor, string Label, string Unit, double Value)[] readings)
    {
        const int sensorSize = 264;
        const int readingSize = 316;
        var sensorOffset = HwInfoSharedMemory.HeaderSize;
        var readingOffset = sensorOffset + sensors.Length * sensorSize;
        var data = new byte[readingOffset + readings.Length * readingSize];
        var span = data.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, HwInfoSharedMemory.Signature);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], (uint)sensorOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], sensorSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], (uint)sensors.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[32..], (uint)readingOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(span[36..], readingSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], (uint)readings.Length);
        for (var i = 0; i < sensors.Length; i++)
        {
            Encoding.Latin1.GetBytes(sensors[i]).CopyTo(span[(sensorOffset + i * sensorSize + 8)..]);
        }

        for (var i = 0; i < readings.Length; i++)
        {
            var r = span[(readingOffset + i * readingSize)..];
            BinaryPrimitives.WriteUInt32LittleEndian(r, (uint)readings[i].Type);
            BinaryPrimitives.WriteUInt32LittleEndian(r[4..], (uint)readings[i].Sensor);
            Encoding.Latin1.GetBytes(readings[i].Label).CopyTo(r[12..]);
            Encoding.Latin1.GetBytes("Rinominato").CopyTo(r[140..]);
            Encoding.Latin1.GetBytes(readings[i].Unit).CopyTo(r[268..]);
            BinaryPrimitives.WriteDoubleLittleEndian(r[284..], readings[i].Value);
        }

        return data;
    }
}
