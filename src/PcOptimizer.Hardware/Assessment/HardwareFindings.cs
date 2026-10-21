using System.Globalization;
using PcOptimizer.Core.Model;
using PcOptimizer.Hardware.Database;
using PcOptimizer.Hardware.Model;

namespace PcOptimizer.Hardware.Assessment;

/// <summary>
/// Osservazioni sull'hardware: problemi rilevabili senza misure (quelle arrivano col modulo Benchmark)
/// e che spesso valgono più di qualsiasi ottimizzazione software, come un monitor a 60 Hz
/// che supporta 144 Hz o il cavo collegato alla scheda madre invece che alla scheda video.
/// </summary>
internal static class HardwareFindings
{
    private const double OneGiB = 1024.0 * 1024 * 1024;

    internal sealed record Input(
        HardwareProfile Profile,
        HardwareDatabase Db,
        CpuArchitectureEntry? Architecture,
        CpuAssessment Cpu,
        IReadOnlyList<GpuAssessment> Gpus,
        GpuAssessment? PrimaryGpu,
        MemoryAssessment Memory,
        StorageAssessment Storage,
        BoardAssessment Board);

    public static IReadOnlyList<Finding> Build(Input input)
    {
        var findings = new List<Finding>();
        AddCpu(input, findings);
        AddGpu(input, findings);
        AddDisplays(input, findings);
        AddMemory(input, findings);
        AddStorage(input, findings);
        AddNetwork(input, findings);
        AddPlatform(input, findings);

        // Ordinamento stabile: prima le più gravi, a parità di gravità l'ordine dei controlli.
        return findings.OrderByDescending(f => f.Severity).ToList();
    }

    private static void AddCpu(Input input, List<Finding> findings)
    {
        var p = input.Profile;
        foreach (var advisory in input.Architecture?.Advisories ?? [])
        {
            if (advisory.MinimumMicrocode is { } minimumText)
            {
                var minimum = (uint)HardwareDatabase.ParseHex(HardwareDatabase.CpuFileName, minimumText);
                if (p.Cpu.MicrocodeRevision is not { } revision)
                {
                    findings.Add(new Finding($"{advisory.Id}-unknown", FindingSeverity.Warning, "CPU",
                        "Revisione del microcode non leggibile",
                        $"{advisory.Detail} Non è stato possibile leggere la revisione del microcode in uso.",
                        advisory.Recommendation));
                }
                else if (revision < minimum)
                {
                    findings.Add(new Finding(advisory.Id, advisory.Severity, "CPU", advisory.Title,
                        $"{advisory.Detail} Microcode in uso: 0x{revision:X}, minimo: {minimumText}.",
                        advisory.Recommendation));
                }
            }

            if (advisory.BiosReleasedBefore is { } before && p.Board.BiosReleaseDate is { } biosDate && biosDate < before)
            {
                findings.Add(new Finding(advisory.Id, advisory.Severity, "CPU", advisory.Title,
                    $"{advisory.Detail} BIOS in uso: {p.Board.BiosVersion} del {Date(biosDate)}.",
                    advisory.Recommendation));
            }
        }

        if (input.Cpu.Confidence == Confidence.Low)
        {
            findings.Add(new Finding("cpu-unknown", FindingSeverity.Info, "CPU",
                "CPU non presente nelle tabelle",
                $"{p.Cpu.BrandString} (famiglia 0x{p.Cpu.Family:X}, modello 0x{p.Cpu.Model:X}) non è nelle tabelle: " +
                "la classificazione usa valori prudenti.",
                "Il report della scansione contiene i dati CPUID per aggiornare Data/cpu-architectures.json."));
        }

        if (p.Cpu.IsHybrid && p.Os.Build > 0 && !p.Os.IsWindows11)
        {
            findings.Add(new Finding("cpu-hybrid-windows10", FindingSeverity.Warning, "CPU",
                "CPU ibrida (core P/E) su Windows 10",
                "Windows 10 non supporta Intel Thread Director: lo scheduler non sa distinguere bene core P ed E " +
                "e i thread del gioco possono finire sui core E, con cali di FPS e frame time irregolari.",
                "Valuta il passaggio a Windows 11, che assegna i thread ai core P/E in modo molto più efficace."));
        }
    }

    private static void AddGpu(Input input, List<Finding> findings)
    {
        var p = input.Profile;
        foreach (var gpu in input.Gpus.Where(g => g.Gpu.UsesBasicDisplayDriver))
        {
            findings.Add(new Finding("gpu-driver-missing", FindingSeverity.Critical, "GPU",
                "Driver della scheda video non installato",
                $"La scheda video {VendorName(gpu.Gpu.Vendor)} (PCI {gpu.Gpu.PciVendorId}:{gpu.Gpu.PciDeviceId}) usa il driver " +
                "generico di Windows: niente accelerazione 3D completa, frequenze di aggiornamento e prestazioni limitate.",
                "Installa il driver dal sito del produttore (NVIDIA, AMD o Intel)."));
        }

        if (input.PrimaryGpu is null && !input.Gpus.Any(g => g.Gpu.UsesBasicDisplayDriver))
        {
            findings.Add(new Finding("gpu-none", FindingSeverity.Warning, "GPU",
                "Nessuna scheda video utilizzabile rilevata",
                "Sono presenti solo adattatori virtuali (desktop remoto, streaming) oppure il rilevamento non è riuscito.",
                "Se stai usando il PC in remoto, ripeti la scansione direttamente sul PC."));
        }

        if (input.PrimaryGpu is { Confidence: Confidence.Low } unknown)
        {
            findings.Add(new Finding("gpu-unknown", FindingSeverity.Info, "GPU",
                "Scheda video non presente nelle tabelle",
                $"{unknown.Gpu.Name}: le prestazioni sono stimate dalla memoria video, quindi la fascia è approssimata.",
                "Il nome esatto è nel report della scansione per aggiornare Data/gpus.json."));
        }

        if (input.PrimaryGpu is { IsIntegrated: false, Year: >= 2016 } primary && primary.Gpu.DriverDate is { } driverDate)
        {
            var months = (int)((Now(p) - driverDate).TotalDays / 30.4);
            if (months >= 12)
            {
                var version = primary.Gpu.VendorDriverVersion ?? primary.Gpu.DriverVersion;
                findings.Add(new Finding("gpu-driver-old", FindingSeverity.Suggestion, "GPU",
                    $"Driver video vecchio di {months} mesi",
                    $"{primary.ModelName ?? primary.Gpu.Name}: driver {version} del {Date(driverDate)}. " +
                    "I driver recenti correggono problemi di stabilità e frame time nei giochi nuovi.",
                    "Aggiorna il driver dal sito del produttore. Se dopo l'aggiornamento noti problemi, " +
                    "la sezione GPU spiega come fare un'installazione pulita con DDU."));
            }
        }

        // Monitor collegato alla scheda madre su un desktop con scheda video dedicata.
        if (p.Board.IsLaptop)
        {
            return;
        }

        var discrete = input.Gpus.FirstOrDefault(g => !g.IsVirtual && !g.IsIntegrated);
        if (discrete is null)
        {
            return;
        }

        foreach (var display in p.Displays)
        {
            var igpu = input.Gpus.FirstOrDefault(g => !g.IsVirtual && g.IsIntegrated &&
                g.Gpu.PciVendorId is not null &&
                g.Gpu.PciVendorId == display.AdapterPciVendorId &&
                g.Gpu.PciDeviceId == display.AdapterPciDeviceId);
            if (igpu is null)
            {
                continue;
            }

            findings.Add(new Finding("display-on-igpu", FindingSeverity.Critical, "Monitor",
                $"Monitor \"{display.Name}\" collegato alla grafica integrata",
                $"Il monitor è collegato alla scheda madre ({igpu.ModelName ?? igpu.Gpu.Name}) e non alla scheda video " +
                $"{discrete.ModelName ?? discrete.Gpu.Name}: i giochi rendono molto meno e la latenza aumenta.",
                "Collega il cavo del monitor alle uscite della scheda video (quelle orizzontali in basso sul retro del case), " +
                "non a quelle della scheda madre."));
        }
    }

    private static void AddDisplays(Input input, List<Finding> findings)
    {
        var displays = input.Profile.Displays;
        for (var i = 0; i < displays.Count; i++)
        {
            var d = displays[i];
            if (d.MaxRefreshHz is not { } max || max <= Math.Round(d.RefreshHz) + 2)
            {
                continue;
            }

            var current = ScoreMath.Fmt(d.RefreshHz, "0");
            findings.Add(new Finding($"display-refresh-{i + 1}", FindingSeverity.Warning, "Monitor",
                $"Monitor \"{d.Name}\" a {current} Hz invece di {max} Hz",
                $"Il monitor supporta {max} Hz alla risoluzione attuale ({d.Width}x{d.Height}) ma Windows lo usa a {current} Hz. " +
                "È uno dei problemi più comuni e incide direttamente su fluidità e latenza percepita.",
                $"Imposta {max} Hz in Impostazioni > Sistema > Schermo > Schermo avanzato " +
                "(Windows 10: Impostazioni schermo avanzate), poi lo stesso valore nelle opzioni del gioco."));
        }
    }

    private static void AddMemory(Input input, List<Finding> findings)
    {
        var m = input.Memory;
        var gb = m.InstalledBytes / OneGiB;
        if (m.InstalledBytes > 0 && gb + 0.25 < 8)
        {
            findings.Add(new Finding("ram-low", FindingSeverity.Warning, "RAM",
                $"Solo {ScoreMath.Fmt(gb)} GB di RAM",
                "Con meno di 8 GB Windows e il gioco competono per la memoria: il sistema ricorre al file di paging " +
                "e compaiono scatti (stuttering).",
                "Il profilo per la fascia Bassa libera più RAM possibile, ma l'intervento più efficace è passare " +
                "ad almeno 16 GB (due moduli uguali, in dual channel)."));
        }
        else if (m.InstalledBytes > 0 && gb + 0.25 < 12)
        {
            findings.Add(new Finding("ram-8gb", FindingSeverity.Suggestion, "RAM",
                $"{ScoreMath.Fmt(gb)} GB di RAM: al limite per i giochi recenti",
                "Bastano per molti titoli competitivi leggeri, ma con browser, Discord e launcher aperti la memoria si satura.",
                "Chiudi le applicazioni in background mentre giochi; per i titoli recenti sono consigliati 16 GB."));
        }

        var soldered = m.Type is MemoryType.Lpddr3 or MemoryType.Lpddr4 or MemoryType.Lpddr5;
        if (!soldered && m.Channels == 1 && !m.ChannelsAssumed)
        {
            findings.Add(m.ModuleCount <= 1
                ? new Finding("ram-single-channel", FindingSeverity.Warning, "RAM",
                    "Un solo modulo RAM: memoria in canale singolo",
                    "Con un solo modulo la banda della memoria è dimezzata: nei giochi limitati dalla CPU " +
                    "(e con la grafica integrata) calano FPS e soprattutto gli 1% low.",
                    "Aggiungi un secondo modulo identico negli slot indicati dal manuale per il dual channel (di solito A2 e B2).")
                : new Finding("ram-wrong-slots", FindingSeverity.Warning, "RAM",
                    "Moduli RAM installati sullo stesso canale",
                    $"I {m.ModuleCount} moduli risultano tutti sullo stesso canale: la memoria lavora in canale singolo " +
                    "con metà della banda disponibile.",
                    "Sposta i moduli negli slot consigliati dal manuale della scheda madre (di solito A2 e B2)."));
        }

        switch (m.Xmp)
        {
            case XmpState.LikelyDisabled:
                findings.Add(new Finding("ram-xmp-disabled", FindingSeverity.Suggestion, "RAM",
                    "Profilo XMP/EXPO probabilmente non attivo", m.XmpExplanation,
                    "Attiva XMP (Intel) o EXPO/DOCP (AMD) nel BIOS: la guida per la tua scheda madre è nella sezione " +
                    "Overclock e BIOS. Dopo l'attivazione verifica la stabilità con il test della RAM."));
                break;
            case XmpState.NotSupportedByPlatform:
                findings.Add(new Finding("ram-xmp-platform", FindingSeverity.Info, "RAM",
                    "Velocità della RAM limitata dalla piattaforma", m.XmpExplanation));
                break;
        }
    }

    private static void AddStorage(Input input, List<Finding> findings)
    {
        var p = input.Profile;
        if (input.Storage.Kind is StorageKind.Hdd or StorageKind.UsbHdd)
        {
            findings.Add(new Finding("disk-system-hdd", FindingSeverity.Warning, "Disco",
                "Windows è installato su un disco meccanico (HDD)",
                $"{input.Storage.SystemDisk?.FriendlyName}: tempi di accesso decine di volte più lenti di un SSD. " +
                "Avvio e caricamenti lunghi, scatti quando il gioco carica dati in background.",
                "Passare a un SSD (anche SATA) migliora la reattività più di qualsiasi ottimizzazione software. " +
                "Gli interventi per la fascia Bassa tengono conto del disco meccanico."));
        }

        foreach (var disk in p.Disks.Where(d => d.Health is DiskHealth.Warning or DiskHealth.Unhealthy))
        {
            findings.Add(new Finding($"disk-health-{disk.DeviceId}", FindingSeverity.Critical, "Disco",
                $"Disco \"{disk.FriendlyName}\" segnalato come {(disk.Health == DiskHealth.Unhealthy ? "guasto" : "a rischio")}",
                "Windows segnala un problema di salute del disco (contatori SMART o errori di lettura/scrittura).",
                "Fai subito un backup dei dati importanti e valuta la sostituzione del disco."));
        }

        if (p.Os.SystemDriveTotalBytes > 0)
        {
            var free = p.Os.SystemDriveFreeBytes;
            var ratio = (double)free / p.Os.SystemDriveTotalBytes;
            if (ratio < 0.10 || free < 15 * OneGiB)
            {
                findings.Add(new Finding("disk-low-space", FindingSeverity.Warning, "Disco",
                    $"Poco spazio libero sul disco di sistema ({ScoreMath.Fmt(free / OneGiB, "0")} GB, {ScoreMath.Fmt(ratio * 100, "0")}%)",
                    "Con poco spazio Windows gestisce male file di paging, aggiornamenti e cache degli shader; " +
                    "sugli SSD calano anche le prestazioni in scrittura.",
                    "Libera spazio fino ad almeno il 15-20% del disco (la pulizia dei file temporanei è nel modulo Storage)."));
            }
        }
    }

    private static void AddNetwork(Input input, List<Finding> findings)
    {
        var adapters = input.Profile.NetworkAdapters;
        var primary = adapters.FirstOrDefault(n => n.IsPrimary) ?? adapters.FirstOrDefault(n => n.IsConnected);
        if (primary?.Medium == NetworkMedium.WiFi)
        {
            findings.Add(new Finding("net-wifi", FindingSeverity.Suggestion, "Rete",
                "Connessione a Internet tramite Wi-Fi",
                "Il Wi-Fi aggiunge latenza variabile (jitter) e perdite di pacchetti dovute alle interferenze: " +
                "nei giochi online si traduce in picchi di ping.",
                "Se possibile usa un cavo Ethernet; in alternativa preferisci la banda a 5/6 GHz e avvicinati al router."));
        }

        if (primary is { Medium: NetworkMedium.Ethernet, LinkSpeedBps: { } bps } && bps is >= 10_000_000 and < 1_000_000_000)
        {
            findings.Add(new Finding("net-link-speed", FindingSeverity.Suggestion, "Rete",
                $"Ethernet collegata a {bps / 1_000_000} Mbps",
                "La scheda di rete ha negoziato meno di 1 Gbps: spesso la causa è un cavo danneggiato o di categoria vecchia.",
                "Prova un cavo Cat5e/Cat6 e un'altra porta del router."));
        }
    }

    private static void AddPlatform(Input input, List<Finding> findings)
    {
        var p = input.Profile;
        if (p.Board.IsLaptop && p.Board.OnBattery == true)
        {
            findings.Add(new Finding("power-on-battery", FindingSeverity.Warning, "Sistema",
                "Portatile alimentato a batteria",
                "A batteria Windows e il produttore limitano CPU e GPU: prestazioni e misure non sono rappresentative.",
                "Collega l'alimentatore prima di giocare e prima di eseguire i benchmark."));
        }

        if (p.Board.Firmware == FirmwareType.Uefi && p.Board.SecureBootEnabled == false)
        {
            findings.Add(new Finding("secureboot-off", FindingSeverity.Info, "Sistema",
                "Secure Boot disattivato",
                "Non incide sulle prestazioni, ma alcuni giochi competitivi lo richiedono per l'anti-cheat " +
                "(ad esempio Valorant su Windows 11 e Battlefield 6).",
                "Se un gioco lo richiede, attivalo nel BIOS (sezione Boot o Security)."));
        }
        else if (p.Board.Firmware == FirmwareType.LegacyBios)
        {
            findings.Add(new Finding("firmware-legacy", FindingSeverity.Info, "Sistema",
                "Windows avviato in modalità BIOS legacy (CSM)",
                "In modalità legacy non sono disponibili Secure Boot e Resizable BAR.",
                "Il passaggio a UEFI richiede di convertire il disco in GPT (strumento MBR2GPT di Windows): " +
                "da fare solo dopo un backup completo."));
        }

        if (p.ScanErrors.Count > 0)
        {
            findings.Add(new Finding("scan-partial", FindingSeverity.Info, "Sistema",
                "Alcune informazioni non sono state rilevate",
                string.Join("; ", p.ScanErrors),
                "La valutazione usa i dati disponibili; i dettagli sono nel log."));
        }
    }

    private static DateTime Now(HardwareProfile p) => p.ScannedAt != default ? p.ScannedAt.LocalDateTime : DateTime.Now;

    private static string Date(DateTime date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string VendorName(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => "NVIDIA",
        GpuVendor.Amd => "AMD",
        GpuVendor.Intel => "Intel",
        _ => "",
    };
}
