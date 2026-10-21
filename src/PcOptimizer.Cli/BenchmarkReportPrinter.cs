using PcOptimizer.Benchmark.Comparison;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Statistics;
using PcOptimizer.Benchmark.Storage;
using static PcOptimizer.Cli.ConsoleOutput;

namespace PcOptimizer.Cli;

/// <summary>Stampa leggibile delle sessioni di misura e dei confronti prima/dopo.</summary>
internal static class BenchmarkReportPrinter
{
    public static void Print(BenchmarkRun run)
    {
        Heading($"Misura \"{(run.Label.Length > 0 ? run.Label : "senza nome")}\" - {ScenarioName(run.Scenario)} - {run.StartedAt:dd/MM/yyyy HH:mm}");
        var env = run.Environment;
        Line("Contesto", $"Windows {env.WindowsBuild ?? "n/d"}; piano energetico {env.PowerSchemeName ?? "n/d"}; " +
                         $"timer {(env.TimerResolutionMs is { } t ? Num(t, "0.###") + " ms" : "n/d")}; acceso da {Num(env.UptimeMinutes, "0")} min" +
                         (env.OnAcPower == false ? "; A BATTERIA" : ""));

        if (run.Score is { } score)
        {
            Line("Punteggio", $"{score.Score}/100{(score.Partial ? " (parziale)" : "")} - indicativo, confrontabile solo con misure di questo PC");
            foreach (var c in score.Components)
            {
                Line("", $"{c.Name}: {Num(c.Value, "0.##")} {c.Unit} → {Num(c.Score, "0")}");
            }
        }

        if (run.DpcIsr is { } d)
        {
            Line("DPC", $"{d.DpcCount:N0} eventi; massimo {Num(d.MaxDpcUs, "0")} µs; P95 dei massimi al secondo {Num(Descriptive.Percentile(d.WindowMaxDpcUs, 95), "0")} µs; mediana {Num(d.Dpc.Median, "0.#")} µs");
            Line("ISR", $"{d.IsrCount:N0} eventi; massimo {Num(d.MaxIsrUs, "0")} µs; P95 dei massimi al secondo {Num(Descriptive.Percentile(d.WindowMaxIsrUs, 95), "0")} µs");
            Line("Carico DPC/ISR", $"{Num(d.WindowLoadPercent.DefaultIfEmpty(0).Average(), "0.###")}% del tempo CPU");
            foreach (var driver in d.Drivers.Take(5))
            {
                Line("  driver", $"{driver.Module}{(driver.Description is null ? "" : $" ({driver.Description})")}: " +
                                 $"DPC max {Num(driver.MaxDpcUs, "0")} µs, ISR max {Num(driver.MaxIsrUs, "0")} µs, totale {Num(driver.TotalMs, "0.#")} ms");
            }
        }

        if (run.Timer is { } timer)
        {
            Line("Timer", $"ritardo di risveglio mediano {Num(timer.LatenessUs.Median, "0")} µs, P99 {Num(timer.LatenessUs.P99, "0")} µs, massimo {Num(timer.LatenessUs.Max, "0")} µs ({timer.Samples:N0} campioni)");
        }

        if (run.Cpu is { } cpu && cpu.Busy.Count > 0)
        {
            var top = string.Join(", ", cpu.TopProcesses.Take(5).Select(p => $"{p.Name} {Num(p.CpuPercent, "0.#")}%"));
            Line("CPU", $"uso medio {Num(cpu.Busy.Mean, "0.#")}%, massimo {Num(cpu.Busy.Max, "0")}%; processi più attivi: {(top.Length > 0 ? top : "nessuno")}");
        }

        if (run.Sensors is { Samples.Count: > 0 } s)
        {
            Line("Sensori", $"fonti: {string.Join(", ", s.Sources)}; CPU max {Temp(s.CpuTempMaxC)}, potenza media {Watt(s.CpuPowerAvgW)}; " +
                            $"GPU max {Temp(s.GpuTempMaxC)}, potenza media {Watt(s.GpuPowerAvgW)}, frequenza media {(s.GpuClockAvgMHz is { } c ? Num(c, "0") + " MHz" : "n/d")}");
        }

        if (run.Frames is { } f)
        {
            Line("Gioco", $"{f.Application} ({f.Source}): {f.Frames:N0} frame in {Num(f.DurationSeconds, "0")} s");
            Line("FPS", $"medi {Num(f.AverageFps, "0.#")}; 1% low {Num(f.OnePercentLowFps, "0.#")}; 0,1% low {Num(f.PointOnePercentLowFps, "0.#")}");
            Line("Frame time", $"mediana {Num(f.FrameTimeMs.Median, "0.##")} ms, P99 {Num(f.FrameTimeMs.P99, "0.##")} ms, massimo {Num(f.FrameTimeMs.Max, "0.#")} ms; scatti {f.StutterCount}; persi {Num(f.DroppedPercent, "0.#")}%");
            Line("Presentazione", $"{f.PresentMode ?? "n/d"}; V-Sync {YesNo(f.VSync)}; limite GPU {(f.GpuBoundShare is { } g ? Num(g * 100, "0") + "% dei frame" : "n/d")}; " +
                                  $"latenza fino allo schermo {(f.AverageDisplayLatencyMs is { } l ? Num(l, "0.#") + " ms" : "n/d")}");
        }

        if (run.Network is { } net)
        {
            Line("Rete", $"{net.AdapterName ?? "scheda non rilevata"} ({net.AdapterType ?? "n/d"})");
            foreach (var p in net.Targets)
            {
                Line($"  {(p.Kind == "gateway" ? "router" : "internet")}", p.Received == 0
                    ? $"{p.Target}: nessuna risposta su {p.Sent}"
                    : $"{p.Target}: mediana {Num(p.RttMs.Median, "0.#")} ms, P95 {Num(p.RttMs.P95, "0.#")} ms, jitter {Num(p.JitterMs ?? 0, "0.##")} ms, persi {Num(p.LossPercent, "0.#")}%");
            }
        }

        if (run.Boot is { Boots.Count: > 0 } boot)
        {
            var b = boot.Boots[0];
            Line("Avvio", $"ultimo {b.Time.LocalDateTime:dd/MM HH:mm}: {Num(b.BootMs / 1000, "0.#")} s (desktop {Num(b.MainPathMs / 1000, "0.#")} s + attività dopo il desktop {Num(b.PostBootMs / 1000, "0.#")} s)" +
                              (b.AfterUpdate ? " - dopo un aggiornamento" : ""));
        }

        foreach (var note in run.Notes)
        {
            Line("Nota", note);
        }

        PrintFindings(run.Findings);
    }

    public static void PrintList(IReadOnlyList<BenchmarkEntry> entries)
    {
        Heading(entries.Count == 0 ? "Nessuna misura salvata" : $"Misure salvate ({entries.Count})");
        foreach (var e in entries)
        {
            Console.WriteLine($"  {e.Id.ToString("N")[..8]}  {e.StartedAt:dd/MM/yyyy HH:mm}  {ScenarioName(e.Scenario),-14} " +
                              $"{(e.Score is { } s ? $"{s,3}/100" : "   -   ")}  {e.Label}");
        }
    }

    public static void PrintComparison(IReadOnlyList<MetricComparison> comparisons, int before, int after)
    {
        Heading($"Confronto: {before} sessioni prima, {after} dopo (test di Mann-Whitney, soglia p < 0,05)");
        foreach (var c in comparisons.Where(c => c.BaselineSamples + c.AfterSamples > 0))
        {
            var change = double.IsFinite(c.RelativeChange) ? $"{(c.RelativeChange >= 0 ? "+" : "")}{Num(c.RelativeChange * 100, "0.#")}%" : "n/d";
            var p = double.IsFinite(c.PValue) ? (c.PValue < 0.001 ? "<0,001" : Num(c.PValue, "0.###")) : "n/d";
            Console.WriteLine($"  {VerdictName(c.Verdict),-14} {c.Title,-38} {Num(c.BaselineMedian, "0.##"),9} → {Num(c.AfterMedian, "0.##"),-9} {c.Unit,-3} {change,8}  p={p}" +
                              (c.NoiseFloor > 0 ? $"  (variabilità fra misure prima: {Num(c.NoiseFloor * 100, "0")}%)" : ""));
        }

        Console.WriteLine("\n  Migliorato/peggiorato solo se la differenza è statisticamente significativa e supera la soglia minima della grandezza.");
    }

    private static string VerdictName(ComparisonVerdict v) => v switch
    {
        ComparisonVerdict.Improved => "MIGLIORATO",
        ComparisonVerdict.Worse => "PEGGIORATO",
        ComparisonVerdict.Unchanged => "invariato",
        _ => "dati scarsi",
    };

    private static string ScenarioName(BenchmarkScenario s) => s switch
    {
        BenchmarkScenario.Idle => "a riposo",
        BenchmarkScenario.CpuLoad => "sotto carico",
        _ => "in gioco",
    };

    private static string Temp(double? c) => c is { } v ? $"{Num(v, "0")} °C" : "n/d";

    private static string Watt(double? w) => w is { } v ? $"{Num(v, "0")} W" : "n/d";
}
