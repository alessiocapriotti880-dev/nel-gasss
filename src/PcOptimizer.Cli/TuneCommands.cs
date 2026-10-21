using System.Globalization;
using Microsoft.Extensions.Logging;
using PcOptimizer.Core;
using PcOptimizer.Hardware.Assessment;
using PcOptimizer.Hardware.Database;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Hardware.Windows;
using PcOptimizer.Tuning.Bios;
using PcOptimizer.Tuning.Guides;
using PcOptimizer.Tuning.Safety;
using PcOptimizer.Tuning.Stability;
using PcOptimizer.Tuning.Windows;
using static PcOptimizer.Cli.ConsoleOutput;

namespace PcOptimizer.Cli;

/// <summary>Comandi "pcopt tune": checklist del BIOS, procedure guidate di undervolt/overclock e test di stabilità.</summary>
internal static class TuneCommands
{
    public static async Task<int> RunAsync(string[] options, ILoggerFactory loggers, string[] dataDirs)
    {
        var sub = options.FirstOrDefault()?.ToLowerInvariant() ?? "help";
        if (sub == "test")
        {
            return await TestAsync(options, loggers);
        }

        Console.WriteLine("Scansione dell'hardware in corso...");
        var profile = await new WindowsHardwareScanner(loggers.CreateLogger<WindowsHardwareScanner>()).ScanAsync();
        var assessment = new HardwareAnalyzer(HardwareDatabase.Load(dataDirs)).Assess(profile);
        var bios = BiosGuide.Load(dataDirs);
        var methods = TuningMethods.For(profile, assessment, bios);
        var store = new TuningProfileStore(new AppPaths());

        switch (sub)
        {
            case "bios":
                PrintChecklist(bios.Build(profile, assessment));
                return 0;
            case "methods" or "status":
                PrintMethods(methods, store);
                return 0;
            case "step":
                return await StepAsync(options, methods, store, loggers);
            case "record":
                return Record(options, methods, store);
            default:
                Console.WriteLine("Uso: pcopt tune bios | methods | step <procedura> | record <procedura> <valore> stabile|instabile | test cpu|ram|gpu|vram");
                return 2;
        }
    }

    private static void PrintChecklist(IReadOnlyList<BiosChecklistItem> items)
    {
        Heading("Checklist del BIOS (l'app non modifica il BIOS: indica dove e cosa impostare)");
        foreach (var i in items)
        {
            var state = i.State switch
            {
                ChecklistState.Action => "DA FARE",
                ChecklistState.Done => "a posto",
                ChecklistState.Check => "verifica",
                ChecklistState.Blocked => "BLOCCATO",
                _ => "info",
            };
            Console.WriteLine($"\n  [{state}] {i.Title} ({PlanPrinter.RiskName(i.Risk)})");
            Console.WriteLine($"      {i.Why}");
            if (i.MenuPath is not null)
            {
                Console.WriteLine($"      Dove: {i.MenuPath}");
            }

            Console.WriteLine($"      → {i.Recommendation}");
            if (i.Verify is not null)
            {
                Console.WriteLine($"      Verifica: {i.Verify}");
            }
        }

        Console.WriteLine("\n  I nomi dei menu cambiano con la versione del BIOS: in caso di dubbio usa la funzione di ricerca del BIOS o il manuale della scheda madre.");
    }

    private static void PrintMethods(IReadOnlyList<TuningMethod> methods, TuningProfileStore store)
    {
        var profiles = store.Load();
        Heading("Procedure guidate di undervolt e overclock");
        foreach (var m in methods)
        {
            Console.WriteLine($"\n  {m.Id}: {m.Title}");
            if (m.Blocked is not null)
            {
                Console.WriteLine($"      Non disponibile: {m.Blocked}");
                continue;
            }

            Console.WriteLine($"      Dove: {m.Where}");
            Console.WriteLine($"      Passi di {Value(m.Limits.Step, m)} da {Value(m.Limits.Start, m)} fino a {Value(m.Limits.Limit, m)} (limite fisso); test: {string.Join(", ", m.Tests.Select(TestName))}");
            if (profiles.LastOrDefault(p => p.MethodId == m.Id) is { } p)
            {
                var next = TuningPlanner.NextValue(p, m);
                var recommended = TuningPlanner.Recommended(p, m);
                Console.WriteLine($"      Provati: {string.Join(", ", p.Steps.Select(s => $"{Value(s.Value, m)} {OutcomeName(s.Outcome)}"))}");
                Console.WriteLine(next is { } n ? $"      Prossimo valore: {Value(n, m)} (pcopt tune step {m.Id})"
                    : recommended is { } r ? $"      Valore finale consigliato: {Value(r, m)}"
                    : "      Nessun valore stabile: lascia le impostazioni di fabbrica.");
            }

            if (m.Warning is not null)
            {
                Console.WriteLine($"      Nota: {m.Warning}");
            }
        }
    }

    private static async Task<int> StepAsync(string[] options, IReadOnlyList<TuningMethod> methods, TuningProfileStore store, ILoggerFactory loggers)
    {
        var method = methods.FirstOrDefault(m => m.Id.Equals(options.ElementAtOrDefault(1), StringComparison.OrdinalIgnoreCase));
        if (method is null)
        {
            Console.Error.WriteLine($"Procedura sconosciuta. Disponibili: {string.Join(", ", methods.Select(m => m.Id))}");
            return 2;
        }

        if (method.Blocked is not null)
        {
            Console.Error.WriteLine($"Non disponibile: {method.Blocked}");
            return 1;
        }

        var profile = store.Open(method.Id);
        if (profile.Steps.LastOrDefault() is { Outcome: StepOutcome.Crashed } crashed)
        {
            Console.WriteLine($"Il test precedente con {Value(crashed.Value, method)} non si è concluso: il PC si è bloccato o riavviato. Valore considerato instabile.");
            store.Save(profile);
        }

        if (TuningPlanner.NextValue(profile, method) is not { } value)
        {
            var recommended = TuningPlanner.Recommended(profile, method);
            Console.WriteLine(recommended is { } r
                ? $"Procedura conclusa. Imposta il valore finale consigliato: {Value(r, method)} ({method.Where})."
                : "Procedura conclusa senza valori stabili: riporta le impostazioni di fabbrica.");
            store.Save(profile with { Completed = true });
            return 0;
        }

        Heading(method.Title);
        for (var i = 0; i < method.Steps.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {method.Steps[i]}");
        }

        Console.WriteLine($"\n  Valore da impostare ora: {Value(value, method)}");
        Console.WriteLine($"  Dove: {method.Where}");
        Console.Write($"\n  Hai impostato {Value(value, method)} e riavviato se necessario? (s/N) ");
        if (Console.ReadLine()?.Trim().ToLowerInvariant() is not ("s" or "si" or "sì" or "y"))
        {
            return 1;
        }

        // Il passo si registra PRIMA del test: se il PC si blocca, al prossimo avvio risulta "crash".
        profile = TuningPlanner.Record(profile, new TuningStep { Value = value, StartedAt = DateTimeOffset.Now, Outcome = StepOutcome.Pending });
        store.Save(profile);

        var minutes = double.TryParse(Option(options, "--minutes"), NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : method.TestDuration.TotalMinutes;
        var runner = new StabilityRunner(loggers);
        StabilityReport? last = null;
        foreach (var kind in method.Tests)
        {
            Console.WriteLine($"\n  Test {TestName(kind)} per {minutes:0.#} minuti: non usare il PC (Ctrl+C per interrompere).");
            using var cancel = CancelOnCtrlC();
            var workload = StabilityRunner.CreateWorkload(kind);
            try
            {
                last = await runner.RunAsync(workload, TimeSpan.FromMinutes(minutes), new Progress<string>(t => Console.WriteLine($"    {t}")), cancel.Token);
            }
            finally
            {
                (workload as IDisposable)?.Dispose();
            }

            PrintReport(last);
            if (!last.Passed)
            {
                break;
            }
        }

        profile = TuningPlanner.Record(profile, new TuningStep { Value = value, StartedAt = DateTimeOffset.Now, Outcome = last!.Outcome, Report = last });
        store.Save(profile);
        var next = TuningPlanner.NextValue(profile, method);
        Console.WriteLine(last.Outcome switch
        {
            StepOutcome.Stable when next is { } n => $"\n  Stabile. Usa il PC normalmente per un po'; poi prosegui con {Value(n, method)}: pcopt tune step {method.Id}",
            StepOutcome.Stable => "\n  Stabile e limite di sicurezza raggiunto: la procedura è conclusa.",
            StepOutcome.Aborted => "\n  Test interrotto per temperatura: migliora il raffreddamento prima di continuare. Torna subito all'ultimo valore stabile.",
            StepOutcome.Pending => "\n  Test annullato: il valore resta da verificare.",
            _ => $"\n  INSTABILE: torna SUBITO all'ultimo valore stabile ({(profile.LastStable is { } s ? Value(s, method) : "impostazioni di fabbrica")}). " +
                 $"Valore finale consigliato: {(TuningPlanner.Recommended(profile, method) is { } r ? Value(r, method) : "impostazioni di fabbrica")}.",
        });
        return 0;
    }

    private static int Record(string[] options, IReadOnlyList<TuningMethod> methods, TuningProfileStore store)
    {
        var method = methods.FirstOrDefault(m => m.Id.Equals(options.ElementAtOrDefault(1), StringComparison.OrdinalIgnoreCase));
        if (method is null || !double.TryParse(options.ElementAtOrDefault(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            options.ElementAtOrDefault(3)?.ToLowerInvariant() is not ({ } outcome and ("stabile" or "instabile")))
        {
            Console.Error.WriteLine("Uso: pcopt tune record <procedura> <valore> stabile|instabile [--note <testo>]");
            return 2;
        }

        var profile = TuningPlanner.Record(store.Open(method.Id), new TuningStep
        {
            Value = value,
            StartedAt = DateTimeOffset.Now,
            Outcome = outcome == "stabile" ? StepOutcome.Stable : StepOutcome.Unstable,
            Note = Option(options, "--note"),
        });
        store.Save(profile);
        Console.WriteLine($"Registrato {Value(value, method)} come {outcome}.");
        return 0;
    }

    private static async Task<int> TestAsync(string[] options, ILoggerFactory loggers)
    {
        var kind = options.ElementAtOrDefault(1)?.ToLowerInvariant() switch
        {
            "cpu" => StabilityTestKind.Cpu,
            "ram" or "memory" => StabilityTestKind.Memory,
            "gpu" => StabilityTestKind.Gpu,
            "vram" => StabilityTestKind.Vram,
            _ => (StabilityTestKind?)null,
        };
        if (kind is null)
        {
            Console.Error.WriteLine("Uso: pcopt tune test cpu|ram|gpu|vram [--minutes <n>] [--seconds <n>] [--mb <MB di RAM>] [--warp]");
            return 2;
        }

        var duration = double.TryParse(Option(options, "--seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? TimeSpan.FromSeconds(s)
            : TimeSpan.FromMinutes(double.TryParse(Option(options, "--minutes"), NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 10);
        Console.WriteLine($"Test {TestName(kind.Value)} per {duration.TotalMinutes:0.#} minuti, limiti: CPU {SafetyLimits.CpuTempHardC:0} °C, GPU {SafetyLimits.GpuTempC:0} °C. Ctrl+C per interrompere.");
        using var cancel = CancelOnCtrlC();
        var workload = StabilityRunner.CreateWorkload(kind.Value, options.Contains("--warp", StringComparer.OrdinalIgnoreCase),
            long.TryParse(Option(options, "--mb"), out var mb) && mb > 0 ? mb * 1024 * 1024 : null);
        try
        {
            var report = await new StabilityRunner(loggers).RunAsync(workload, duration, new Progress<string>(t => Console.WriteLine($"  {t}")), cancel.Token);
            PrintReport(report);
            return report.Passed ? 0 : 1;
        }
        finally
        {
            (workload as IDisposable)?.Dispose();
        }
    }

    private static void PrintReport(StabilityReport r)
    {
        Heading($"Esito del test {TestName(r.Kind)}: {OutcomeName(r.Outcome).ToUpperInvariant()}");
        Line("Durata", $"{r.Duration:mm\\:ss}, {r.WorkUnits:N0} unità di lavoro verificate");
        Line("Errori", $"{r.Errors} di calcolo/memoria, {r.WheaErrors} errori hardware WHEA");
        var cpu = r.Sensors.Select(x => x.CpuTempC).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(double.NaN).Max();
        var gpu = r.Sensors.Select(x => x.GpuTempC).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(double.NaN).Max();
        Line("Temperature", $"CPU max {(double.IsNaN(cpu) ? "n/d (avvia HWiNFO)" : Num(cpu, "0") + " °C")}, GPU max {(double.IsNaN(gpu) ? "n/d" : Num(gpu, "0") + " °C")}");
        if (r.AbortReason is not null)
        {
            Line("Interrotto", r.AbortReason);
        }

        foreach (var note in r.Notes.Take(5))
        {
            Line("Nota", note);
        }
    }

    private static string Value(double value, TuningMethod m) =>
        $"{(value > 0 && m.Limits.Step != 0 && m.Component != TuningComponent.Memory ? "+" : "")}{Num(value, "0.#")} {m.Limits.Unit}";

    private static string TestName(StabilityTestKind k) => k switch
    {
        StabilityTestKind.Cpu => "CPU",
        StabilityTestKind.Memory => "RAM",
        StabilityTestKind.Gpu => "GPU",
        _ => "VRAM",
    };

    private static string OutcomeName(StepOutcome o) => o switch
    {
        StepOutcome.Stable => "stabile",
        StepOutcome.Unstable => "instabile",
        StepOutcome.Crashed => "crash",
        StepOutcome.Aborted => "interrotto",
        _ => "da verificare",
    };

    private static string? Option(string[] options, string name)
    {
        var i = Array.FindIndex(options, o => o.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < options.Length ? options[i + 1] : null;
    }

    private static CancellationTokenSource CancelOnCtrlC()
    {
        var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        return cancel;
    }
}
