using PcOptimizer.Hardware.Assessment;
using PcOptimizer.Hardware.Model;
using static PcOptimizer.Cli.ConsoleOutput;

namespace PcOptimizer.Cli;

/// <summary>Stampa leggibile del profilo hardware e della sua valutazione.</summary>
internal static class HardwareReportPrinter
{
    public static void Print(HardwareProfile p, HardwareAssessment a)
    {
        var t = a.Tier;
        Heading($"Fascia: {TierClassifier.TierName(t.Tier).ToUpperInvariant()}  " +
                $"(punteggio {Num(t.Score)}/100, affidabilità {ConfidenceName(t.Confidence)})");
        foreach (var c in t.Components)
        {
            Console.WriteLine($"  {c.Component,-6} {c.Score,5:0}  peso {Num(c.Weight, "0.00")}  {c.Explanation}");
        }

        foreach (var cap in t.AppliedCaps)
        {
            Console.WriteLine($"  Limite: {cap}");
        }

        Heading("Componenti");
        var cpu = p.Cpu;
        Line("CPU", cpu.BrandString);
        Line("", $"{a.Cpu.ArchitectureName}, {a.Cpu.Generation}; famiglia 0x{cpu.Family:X} modello 0x{cpu.Model:X} " +
                 $"stepping {cpu.Stepping}, microcode {(cpu.MicrocodeRevision is { } mc ? $"0x{mc:X}" : "n/d")}");

        foreach (var g in a.Gpus)
        {
            var vram = g.Gpu.DedicatedMemoryBytes is { } bytes ? $"{Num(bytes / GiB)} GB, " : "";
            var driver = g.Gpu.VendorDriverVersion ?? g.Gpu.DriverVersion ?? "n/d";
            var date = g.Gpu.DriverDate is { } d ? $" del {d:dd/MM/yyyy}" : "";
            var role = g == a.PrimaryGpu ? " [principale]" : g.IsVirtual ? " [virtuale]" : g.IsIntegrated ? " [integrata]" : "";
            Line("GPU", $"{g.Gpu.Name}{role}: {vram}driver {driver}{date}");
        }

        var m = a.Memory;
        var channels = m.Channels is { } ch ? $"{ch} {(ch == 1 ? "canale" : "canali")}{(m.ChannelsAssumed ? " (stimato)" : "")}" : "canali n/d";
        var type = m.Type == MemoryType.Unknown ? "" : $" {m.Type.ToString().ToUpperInvariant()}";
        var dataRate = m.ConfiguredMTs is { } mts ? $"{mts} MT/s" : "velocità n/d";
        Line("RAM", $"{Num(m.InstalledGB)} GB{type}, {dataRate}, {m.ModuleCount} moduli, {channels}");
        Line("", $"XMP/EXPO: {m.XmpExplanation}");

        foreach (var disk in p.Disks)
        {
            Line("Disco", $"{disk.FriendlyName}: {Num(disk.SizeBytes / GiB, "0")} GB, {disk.MediaType}/{disk.BusType}, " +
                          $"salute {disk.Health}{(disk.IsSystemDisk ? " [sistema]" : "")}");
        }

        var b = p.Board;
        var chipset = a.Board.Chipset is { } cs
            ? $", chipset {cs} (OC CPU: {OcName(a.Board.CpuOverclock)}, OC RAM: {OcName(a.Board.MemoryOverclock)})"
            : "";
        Line("Scheda", $"{b.BoardManufacturer} {b.BoardProduct}{chipset}{(b.IsLaptop ? " [portatile]" : "")}");
        Line("BIOS", $"{b.BiosVendor} {b.BiosVersion}{(b.BiosReleaseDate is { } bd ? $" del {bd:dd/MM/yyyy}" : "")}, " +
                     $"{b.Firmware}, Secure Boot {(b.SecureBootEnabled switch { true => "attivo", false => "disattivo", _ => "n/d" })}");

        foreach (var n in p.NetworkAdapters.Where(n => n.IsConnected))
        {
            var speed = n.LinkSpeedBps is { } bps ? $" {bps / 1_000_000} Mbps" : "";
            Line("Rete", $"{n.Description}: {n.Medium}{speed}{(n.IsPrimary ? " [principale]" : "")}");
        }

        foreach (var d in p.Displays)
        {
            var max = d.MaxRefreshHz is { } mx ? $" (max {mx} Hz)" : "";
            var hz = d.RefreshHz > 0 ? $"{Num(d.RefreshHz, "0.##")} Hz" : "frequenza n/d";
            Line("Monitor", $"{d.Name}: {d.Width}x{d.Height} a {hz}{max}, {d.Connection}{(d.IsPrimary ? " [principale]" : "")}");
        }

        Line("Sistema", $"{p.Os.FriendlyName} {p.Os.DisplayVersion} (build {p.Os.Build}.{p.Os.Ubr})");
        PrintFindings(a.Findings);
    }

    private static string ConfidenceName(Confidence c) => c switch
    {
        Confidence.High => "alta",
        Confidence.Medium => "media",
        _ => "bassa",
    };

    private static string OcName(OcSupport s) => s switch
    {
        OcSupport.Yes => "sì",
        OcSupport.No => "no",
        OcSupport.Limited => "limitato",
        _ => "n/d",
    };
}
