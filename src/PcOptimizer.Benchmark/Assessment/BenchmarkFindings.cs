using System.Globalization;
using PcOptimizer.Benchmark.Catalog;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Statistics;
using PcOptimizer.Core.Model;

namespace PcOptimizer.Benchmark.Assessment;

/// <summary>Osservazioni ricavate da una sessione di misura: solo da valori misurati, con le soglie dichiarate.</summary>
public static class BenchmarkFindings
{
    /// <summary>DPC/ISR oltre questa durata causano scatti audio e irregolarità (soglia comune di LatencyMon).</summary>
    public const double HighLatencyUs = 1000;
    public const double SevereLatencyUs = 4000;

    public static IReadOnlyList<Finding> Evaluate(BenchmarkRun run, KnownDriverCatalog drivers)
    {
        var findings = new List<Finding>();
        Environment(run, findings);
        Background(run, findings);
        DpcIsr(run.DpcIsr, drivers, findings);
        Timer(run.Timer, findings);
        Frames(run.Frames, findings);
        Network(run.Network, findings);
        Sensors(run.Sensors, findings);
        Boot(run.Boot, findings);
        return findings;
    }

    private static void Environment(BenchmarkRun run, List<Finding> findings)
    {
        var env = run.Environment;
        if (env.OnAcPower == false)
        {
            findings.Add(new Finding("bench-on-battery", FindingSeverity.Warning, "Sistema",
                "Misura eseguita a batteria",
                "A batteria Windows e il firmware limitano frequenze e prestazioni: i valori non sono confrontabili con quelli da rete elettrica.",
                "Collega l'alimentatore e ripeti la misura."));
        }

        if (env.UptimeMinutes < 5)
        {
            findings.Add(new Finding("bench-recent-boot", FindingSeverity.Suggestion, "Sistema",
                "Misura subito dopo l'avvio",
                $"Il PC è acceso da {Num(env.UptimeMinutes)} minuti: nei primi minuti Windows esegue attività di avvio che disturbano la misura.",
                "Attendi qualche minuto dopo l'avvio prima di misurare."));
        }
    }

    private static void Background(BenchmarkRun run, List<Finding> findings)
    {
        if (run.Cpu is not { } cpu || run.Scenario != BenchmarkScenario.Idle || cpu.Busy.Count == 0)
        {
            return;
        }

        if (cpu.Busy.Mean > 10)
        {
            var top = string.Join(", ", cpu.TopProcesses.Take(3).Select(p => $"{p.Name} {Num(p.CpuPercent)}%"));
            findings.Add(new Finding("bench-background-load", FindingSeverity.Warning, "Sistema",
                "Attività in background durante la misura a riposo",
                $"Uso medio della CPU {Num(cpu.Busy.Mean)}% (processi più attivi: {top}). Con questo carico le misure di latenza sono disturbate e il confronto prima/dopo è meno affidabile.",
                "Chiudi i programmi aperti, attendi la fine di aggiornamenti o scansioni e ripeti la misura."));
        }
    }

    private static void DpcIsr(DpcIsrResult? dpc, KnownDriverCatalog drivers, List<Finding> findings)
    {
        if (dpc is null || dpc.WindowMaxDpcUs.Length == 0)
        {
            return;
        }

        if (dpc.LostEvents > 0)
        {
            findings.Add(new Finding("bench-etw-lost", FindingSeverity.Info, "Sistema",
                "Eventi di misura persi",
                $"{dpc.LostEvents} eventi del kernel non sono stati registrati (buffer pieni): i massimi potrebbero essere sottostimati."));
        }

        var windows = dpc.WindowMaxDpcUs.Length;
        var dpcHigh = dpc.WindowMaxDpcUs.Count(v => v > HighLatencyUs);
        var isrHigh = dpc.WindowMaxIsrUs.Count(v => v > HighLatencyUs);
        // Un picco isolato capita anche sui sistemi sani: conta la ripetizione (almeno il 2% dei secondi) o la gravità.
        var repeated = Math.Max(2, (int)Math.Ceiling(windows * 0.02));
        var worst = dpc.Drivers.FirstOrDefault(d => d.Module != "sconosciuto") ?? dpc.Drivers.FirstOrDefault();
        var known = worst is null ? null : drivers.Find(worst.Module);
        var culprit = worst is null ? "" : $" Driver con l'esecuzione più lunga: {worst.Module}{(known is null ? "" : $" ({known.Name})")}, " +
                                           $"{Num(Math.Max(worst.MaxDpcUs, worst.MaxIsrUs), "0")} µs.";

        if (dpcHigh >= repeated || isrHigh >= repeated || dpc.MaxDpcUs > SevereLatencyUs || dpc.MaxIsrUs > SevereLatencyUs)
        {
            var severe = dpc.MaxDpcUs > SevereLatencyUs || dpc.MaxIsrUs > SevereLatencyUs;
            findings.Add(new Finding("bench-dpc-high", severe ? FindingSeverity.Warning : FindingSeverity.Suggestion, "Latenza",
                "DPC/ISR lunghi",
                $"In {Math.Max(dpcHigh, isrHigh)} secondi su {windows} un DPC o un ISR ha superato {Num(HighLatencyUs, "0")} µs " +
                $"(massimo DPC {Num(dpc.MaxDpcUs, "0")} µs, ISR {Num(dpc.MaxIsrUs, "0")} µs). Possono causare scatti, audio che gracchia e input irregolare.{culprit}",
                known?.Hint ?? "Aggiorna i driver del dispositivo indicato (chipset, rete, audio, video) dal sito del produttore."));
        }
        else
        {
            findings.Add(new Finding("bench-dpc-ok", FindingSeverity.Info, "Latenza",
                "DPC/ISR nella norma",
                $"Massimo DPC {Num(dpc.MaxDpcUs, "0")} µs, ISR {Num(dpc.MaxIsrUs, "0")} µs; nessuna ripetizione oltre {Num(HighLatencyUs, "0")} µs.{culprit}"));
        }
    }

    private static void Timer(TimerLatencyResult? timer, List<Finding> findings)
    {
        if (timer is null || timer.Samples == 0)
        {
            return;
        }

        if (timer.LatenessUs.P99 > 1000)
        {
            findings.Add(new Finding("bench-timer-late", FindingSeverity.Warning, "Latenza",
                "Risveglio dei thread in ritardo",
                $"Nell'1% dei casi un thread ad alta priorità riparte con oltre {Num(timer.LatenessUs.P99, "0")} µs di ritardo (massimo {Num(timer.LatenessUs.Max, "0")} µs). " +
                "Le cause tipiche sono DPC lunghi, stati di risparmio energetico profondi della CPU o carico in background.",
                "Verifica il piano energetico e i DPC; poi ripeti la misura."));
        }
    }

    private static void Frames(FrameResult? frames, List<Finding> findings)
    {
        if (frames is null)
        {
            return;
        }

        if (frames.PresentMode is { } mode && mode.Contains("Composed", StringComparison.OrdinalIgnoreCase) && frames.PresentModeShare > 0.5)
        {
            findings.Add(new Finding("bench-frames-composed", FindingSeverity.Suggestion, "GPU",
                "Il gioco passa dal compositore di Windows",
                $"Modalità di presentazione prevalente: {mode}. Ogni frame viene composto dal Desktop Window Manager, con latenza aggiuntiva rispetto a \"Independent Flip\".",
                "Usa lo schermo intero o le \"Ottimizzazioni per i giochi in finestra\" e chiudi gli overlay che forzano la composizione; poi ripeti la misura."));
        }

        if (frames.VSync == true)
        {
            findings.Add(new Finding("bench-frames-vsync", FindingSeverity.Info, "GPU",
                "V-Sync attivo",
                "Il gioco sincronizza i frame con il monitor: niente tearing ma latenza in più quando gli FPS superano la frequenza del monitor.",
                "Con G-Sync/FreeSync: V-Sync nel pannello del driver e un limite FPS qualche fotogramma sotto la frequenza del monitor. Senza, per il competitivo si preferisce V-Sync disattivato."));
        }

        if (frames.GpuBoundShare is { } gpu)
        {
            findings.Add(gpu >= 0.75
                ? new Finding("bench-frames-gpu-bound", FindingSeverity.Info, "GPU",
                    "Limite: la scheda video",
                    $"La GPU è occupata quasi tutto il tempo nel {Num(gpu * 100, "0")}% dei frame: gli FPS dipendono dalla GPU.",
                    "Per abbassare la latenza con GPU al limite: NVIDIA Reflex/AMD Anti-Lag se il gioco li supporta, oppure un limite FPS appena sotto il valore medio.")
                : new Finding("bench-frames-cpu-bound", FindingSeverity.Info, "CPU",
                    "Limite: CPU o limitatore di FPS",
                    $"La GPU è al limite solo nel {Num(gpu * 100, "0")}% dei frame: gli FPS dipendono dalla CPU, dalla RAM o da un limite FPS impostato."));
        }

        var stutterShare = frames.Frames > 0 ? (double)frames.StutterCount / frames.Frames : 0;
        if (stutterShare > 0.01)
        {
            findings.Add(new Finding("bench-frames-stutter", FindingSeverity.Warning, "GPU",
                "Scatti frequenti",
                $"{frames.StutterCount} frame ({Num(stutterShare * 100)}%) hanno impiegato più del doppio della mediana recente " +
                $"(1% low {Num(frames.OnePercentLowFps, "0")} FPS contro una media di {Num(frames.AverageFps, "0")} FPS).",
                "Cause comuni: compilazione degli shader, RAM o VRAM piena, caricamenti dal disco, programmi in background, DPC lunghi."));
        }
    }

    private static void Network(NetworkResult? network, List<Finding> findings)
    {
        if (network is null)
        {
            return;
        }

        var wifi = network.AdapterType == "Wi-Fi";
        var gateway = network.Targets.FirstOrDefault(t => t.Kind == "gateway");
        if (gateway is { Sent: > 0 })
        {
            if (gateway.Received == 0)
            {
                findings.Add(new Finding("bench-net-gateway-noreply", FindingSeverity.Info, "Rete",
                    "Il router non risponde al ping",
                    $"Nessuna risposta da {gateway.Target}: alcuni router o reti aziendali bloccano il ping. Nessuna misura della rete locale."));
            }
            else
            {
                if (gateway.LossPercent >= 1)
                {
                    findings.Add(new Finding("bench-net-gateway-loss", FindingSeverity.Warning, "Rete",
                        "Pacchetti persi nella rete locale",
                        $"{Num(gateway.LossPercent)}% di pacchetti persi verso il router ({gateway.Target}): in gioco si traduce in rubber banding e colpi non registrati.",
                        wifi ? "Usa il cavo Ethernet o avvicinati al router; verifica interferenze sul canale Wi-Fi." : "Verifica cavo, porta del router e driver della scheda di rete."));
                }

                if (gateway.JitterMs > (wifi ? 5 : 1) || gateway.RttMs.P95 > (wifi ? 15 : 3))
                {
                    findings.Add(new Finding("bench-net-gateway-jitter", FindingSeverity.Warning, "Rete",
                        "Rete locale irregolare",
                        $"Verso il router: ping mediano {Num(gateway.RttMs.Median)} ms, P95 {Num(gateway.RttMs.P95)} ms, jitter {Num(gateway.JitterMs ?? 0)} ms.",
                        wifi ? "Il Wi-Fi aggiunge ritardi variabili: per il gioco competitivo usa il cavo Ethernet." : "Con il cavo questi valori dovrebbero restare sotto 1 ms: verifica driver, cavo e risparmio energetico della scheda."));
                }
            }
        }

        var internet = network.Targets.FirstOrDefault(t => t.Kind == "internet");
        if (internet is { Sent: > 0, Received: > 0 } && internet.LossPercent >= 2 && (gateway is null || gateway.LossPercent < 1))
        {
            findings.Add(new Finding("bench-net-internet-loss", FindingSeverity.Suggestion, "Rete",
                "Pacchetti persi verso Internet",
                $"{Num(internet.LossPercent)}% di pacchetti persi verso {internet.Target} con la rete locale regolare: il problema è nella linea o nel provider, non nel PC."));
        }

        if (wifi)
        {
            findings.Add(new Finding("bench-net-wifi", FindingSeverity.Info, "Rete",
                "Connessione Wi-Fi",
                "Il Wi-Fi ha latenza e jitter più variabili del cavo e i suoi driver generano spesso DPC lunghi.",
                "Per il gioco competitivo è preferibile il cavo Ethernet."));
        }
    }

    private static void Sensors(SensorSeries? sensors, List<Finding> findings)
    {
        if (sensors is null || sensors.Samples.Count == 0)
        {
            return;
        }

        if (sensors.CpuTempMaxC is >= 95)
        {
            findings.Add(new Finding("bench-cpu-hot", FindingSeverity.Warning, "CPU",
                "CPU al limite termico",
                $"Temperatura massima della CPU {Num(sensors.CpuTempMaxC.Value, "0")} °C: oltre i 95-100 °C la CPU riduce le frequenze.",
                "Verifica dissipatore, pasta termica, flusso d'aria del case e limiti di potenza nel BIOS (sezione Overclock e BIOS)."));
        }

        if (sensors.Samples.Any(s => s.CpuThermalThrottling == true))
        {
            findings.Add(new Finding("bench-cpu-throttling", FindingSeverity.Warning, "CPU",
                "Riduzione delle frequenze per temperatura (CPU)",
                "HWiNFO ha segnalato thermal throttling della CPU durante la misura."));
        }

        if (sensors.GpuTempMaxC is >= 87)
        {
            findings.Add(new Finding("bench-gpu-hot", FindingSeverity.Warning, "GPU",
                "GPU molto calda",
                $"Temperatura massima della GPU {Num(sensors.GpuTempMaxC.Value, "0")} °C.",
                "Verifica la ventilazione del case e la curva delle ventole; un undervolt della GPU riduce calore e consumi (sezione Overclock e Undervolt)."));
        }

        var limits = sensors.Samples.SelectMany(s => s.GpuLimits).GroupBy(l => l).ToDictionary(g => g.Key, g => g.Count());
        if (limits.Keys.Any(k => k.Contains("temperatura", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new Finding("bench-gpu-thermal-limit", FindingSeverity.Warning, "GPU",
                "La GPU riduce le frequenze per temperatura",
                $"Il driver NVIDIA ha segnalato limiti termici in {limits.Where(k => k.Key.Contains("temperatura", StringComparison.OrdinalIgnoreCase)).Sum(k => k.Value)} letture."));
        }
    }

    private static void Boot(BootResult? boot, List<Finding> findings)
    {
        if (boot?.Boots.FirstOrDefault(b => !b.AfterUpdate) is not { } last)
        {
            return;
        }

        if (last.BootMs > 90_000)
        {
            findings.Add(new Finding("bench-boot-slow", FindingSeverity.Suggestion, "Sistema",
                "Avvio lento",
                $"Ultimo avvio: {Num(last.BootMs / 1000, "0")} s fino al desktop pronto, di cui {Num(last.PostBootMs / 1000, "0")} s dopo la comparsa del desktop (programmi in avvio automatico).",
                "Riduci i programmi in avvio automatico (sezione Sistema); su disco meccanico valuta un SSD."));
        }
    }

    private static string Num(double value, string format = "0.#") =>
        value.ToString(format, CultureInfo.InvariantCulture).Replace('.', ',');
}
