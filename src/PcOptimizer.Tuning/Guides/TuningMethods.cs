using PcOptimizer.Core.Model;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Tuning.Bios;
using PcOptimizer.Tuning.Safety;

namespace PcOptimizer.Tuning.Guides;

public enum TuningComponent
{
    Cpu,
    Gpu,
    Memory,
}

public enum StabilityTestKind
{
    Cpu,
    Memory,
    Gpu,
    Vram,
}

/// <summary>
/// Procedura guidata di undervolt o overclock. Il valore si imposta a mano (BIOS o strumento del produttore della GPU):
/// l'app indica il valore successivo, esegue il test di stabilità, registra l'esito e suggerisce il valore finale.
/// </summary>
public sealed record TuningMethod
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required TuningComponent Component { get; init; }

    /// <summary>Dove si imposta il valore (menu del BIOS o programma).</summary>
    public required string Where { get; init; }

    public required IReadOnlyList<string> Steps { get; init; }

    public required StepLimits Limits { get; init; }

    public required IReadOnlyList<StabilityTestKind> Tests { get; init; }

    /// <summary>Durata minima del test di stabilità per ogni passo.</summary>
    public TimeSpan TestDuration { get; init; } = TimeSpan.FromMinutes(10);

    public string? Warning { get; init; }

    /// <summary>Se non null, la procedura non è disponibile su questo PC (con il motivo).</summary>
    public string? Blocked { get; init; }
}

/// <summary>Procedure disponibili per questo PC, con i blocchi di sicurezza (Raptor Lake, portatili, macchine virtuali).</summary>
public static class TuningMethods
{
    public static IReadOnlyList<TuningMethod> For(HardwareProfile profile, HardwareAssessment assessment, BiosGuide bios)
    {
        var methods = new List<TuningMethod>();
        var menus = bios.Menus(assessment.Board.Vendor);
        var laptop = profile.Board.IsLaptop;
        var vm = assessment.Board.IsVirtualMachine;
        var cpu = profile.Cpu;
        var raptorBlocked = BiosGuide.RaptorLakeNeedsUpdate(profile, assessment);
        string? CommonBlock() => vm ? "Non disponibile in una macchina virtuale." : null;

        if (cpu.Vendor == CpuVendor.Intel)
        {
            methods.Add(new TuningMethod
            {
                Id = "cpu.intel-offset",
                Title = "Undervolt della CPU Intel (offset di tensione)",
                Component = TuningComponent.Cpu,
                Where = menus?.Settings.GetValueOrDefault("intelUndervolt") ?? "BIOS: tensione del core in modalità Offset, segno negativo",
                Steps =
                [
                    "Esegui la misura di riferimento e un test CPU di 10 minuti alle impostazioni attuali (temperature e frequenze).",
                    "Nel BIOS imposta l'offset indicato dall'app (passi di -10 mV), salva e riavvia.",
                    "In Windows esegui il test di stabilità CPU per 10 minuti, poi gioca normalmente: annota eventuali crash.",
                    "Se è stabile, scendi di un altro passo; al primo errore, schermata blu o riavvio torna all'ultimo valore stabile.",
                    "Valore finale consigliato: l'ultimo stabile ridotto di un passo (margine di sicurezza).",
                ],
                Limits = new StepLimits(-30, -10, -100, "mV", Risk.Medium),
                Tests = [StabilityTestKind.Cpu],
                Warning = "Dalla 12ª generazione molte schede hanno \"Undervolt Protection\": se attiva, l'undervolt funziona solo dal BIOS " +
                          "(gli strumenti di Windows come XTU o ThrottleStop non hanno effetto). L'app non scrive tensioni: si imposta solo dal BIOS.",
                Blocked = CommonBlock()
                          ?? (raptorBlocked ? "Prima aggiorna il BIOS: questa CPU Intel 13ª/14ª gen ha un microcode precedente alle correzioni per il degrado (vedi checklist del BIOS)." : null)
                          ?? (laptop ? "Sui portatili l'undervolt è quasi sempre bloccato dal produttore: non è proposto." : null),
            });
        }

        if (cpu.Vendor == CpuVendor.Amd)
        {
            var zen3Plus = assessment.Cpu.ArchitectureId is { } arch && (arch.Contains("zen3", StringComparison.OrdinalIgnoreCase) ||
                arch.Contains("zen4", StringComparison.OrdinalIgnoreCase) || arch.Contains("zen5", StringComparison.OrdinalIgnoreCase));
            methods.Add(new TuningMethod
            {
                Id = "cpu.amd-curve-optimizer",
                Title = "Undervolt della CPU AMD (Curve Optimizer)",
                Component = TuningComponent.Cpu,
                Where = menus?.Settings.GetValueOrDefault("curveOptimizer") ?? "BIOS: Precision Boost Overdrive > Curve Optimizer, All Cores, Negative",
                Steps =
                [
                    "Esegui la misura di riferimento e un test CPU di 10 minuti alle impostazioni attuali.",
                    "Nel BIOS imposta Curve Optimizer: All Cores, Negative, con il valore indicato dall'app (passi di 5), salva e riavvia.",
                    "Esegui il test CPU (errori di calcolo ed errori WHEA corretti sono segni di instabilità), poi usa il PC normalmente: " +
                    "con Curve Optimizer l'instabilità compare spesso a basso carico o al minimo.",
                    "Se è stabile, aumenta di 5; al primo errore torna all'ultimo valore stabile.",
                    "Valore finale consigliato: l'ultimo stabile ridotto di un passo.",
                ],
                Limits = new StepLimits(-10, -5, -30, "conteggi", Risk.Medium),
                Tests = [StabilityTestKind.Cpu],
                Warning = "Il test dell'app trova gli errori sotto carico; per l'instabilità al minimo usa il PC qualche giorno prima di considerare stabile il valore.",
                Blocked = CommonBlock()
                          ?? (laptop ? "Curve Optimizer non è disponibile sui portatili." : null)
                          ?? (!zen3Plus ? "Curve Optimizer richiede un Ryzen 5000 o successivo (non APU mobile)." : null),
            });
        }

        var gpu = assessment.PrimaryGpu?.Gpu;
        var gpuVendor = gpu?.Vendor ?? GpuVendor.Unknown;
        var gpuTool = gpuVendor switch
        {
            GpuVendor.Nvidia => "MSI Afterburner (editor della curva con Ctrl+F)",
            GpuVendor.Amd => "AMD Software: Adrenalin > Prestazioni > Ottimizzazione > Ottimizzazione manuale (tensione GPU)",
            GpuVendor.Intel => "Intel Graphics Software / Arc Control > Prestazioni",
            _ => "strumento di overclock del produttore della GPU",
        };
        var gpuBlock = vm ? "Non disponibile in una macchina virtuale."
            : gpu is null || assessment.PrimaryGpu!.IsIntegrated ? "Serve una scheda video dedicata."
            : null;

        methods.Add(new TuningMethod
        {
            Id = "gpu.undervolt",
            Title = "Undervolt della GPU (curva tensione/frequenza)",
            Component = TuningComponent.Gpu,
            Where = gpuTool,
            Steps = gpuVendor == GpuVendor.Nvidia
                ?
                [
                    "Esegui la misura di riferimento e il test GPU: annota frequenza media, potenza e temperatura.",
                    "In Afterburner apri l'editor della curva (Ctrl+F), scegli la frequenza media annotata e trascina il punto alla tensione indicata dall'app; " +
                    "appiattisci i punti a destra (Shift+clic e trascina sotto, poi Applica).",
                    "Esegui il test GPU e quello della VRAM, poi gioca: artefatti, crash del driver o del gioco indicano instabilità.",
                    "Se è stabile, scendi di 25 mV mantenendo la frequenza; al primo errore torna all'ultimo valore stabile.",
                    "Salva il profilo in Afterburner e attiva \"Applica all'avvio\" solo quando il valore è confermato.",
                ]
                :
                [
                    "Esegui la misura di riferimento e il test GPU.",
                    $"In {gpuTool} riduci la tensione massima al valore indicato dall'app lasciando invariata la frequenza.",
                    "Esegui il test GPU e quello della VRAM, poi gioca: artefatti o crash del driver indicano instabilità.",
                    "Se è stabile, scendi di 25 mV; al primo errore torna all'ultimo valore stabile.",
                ],
            Limits = new StepLimits(-50, -25, -200, "mV rispetto alla tensione di fabbrica", Risk.Low),
            Tests = [StabilityTestKind.Gpu, StabilityTestKind.Vram],
            Warning = "Riduce consumi e temperature con prestazioni uguali o migliori (boost più stabile). Non aumentare mai il cursore della tensione.",
            Blocked = gpuBlock,
        });

        methods.Add(new TuningMethod
        {
            Id = "gpu.core-overclock",
            Title = "Overclock del core della GPU",
            Component = TuningComponent.Gpu,
            Where = gpuTool,
            Steps =
            [
                "Esegui la misura di riferimento e il test GPU.",
                "Porta il limite di potenza al massimo consentito dal programma; NON toccare il cursore della tensione.",
                "Aumenta la frequenza del core del valore indicato dall'app (passi di +15 MHz) e applica.",
                "Esegui il test GPU e gioca: artefatti, crash del driver o del gioco indicano instabilità.",
                "Al primo errore torna indietro di due passi. Misura gli FPS: tieni l'overclock solo se migliorano.",
            ],
            Limits = new StepLimits(30, 15, 200, "MHz", Risk.Medium),
            Tests = [StabilityTestKind.Gpu],
            Warning = "Aumenta consumi e temperature. La garanzia non copre i danni da overclock in alcuni paesi o marchi.",
            Blocked = gpuBlock ?? (laptop ? "Sconsigliato sui portatili: il raffreddamento è già al limite." : null),
        });

        methods.Add(new TuningMethod
        {
            Id = "gpu.memory-overclock",
            Title = "Overclock della memoria della GPU",
            Component = TuningComponent.Gpu,
            Where = gpuTool,
            Steps =
            [
                "Esegui la misura di riferimento (FPS in gioco) e il test della VRAM.",
                "Aumenta la frequenza della memoria del valore indicato dall'app (passi di +50 MHz).",
                "Esegui il test della VRAM e misura gli FPS: con GDDR6X gli errori vengono corretti ritrasmettendo i dati, quindi " +
                "il segnale di instabilità è un calo di prestazioni oltre agli errori.",
                "Fermati al primo errore o calo di FPS e torna indietro di due passi.",
            ],
            Limits = new StepLimits(100, 50, 1000, "MHz", Risk.Medium),
            Tests = [StabilityTestKind.Vram],
            Blocked = gpuBlock ?? (laptop ? "Sconsigliato sui portatili." : null),
        });

        methods.Add(new TuningMethod
        {
            Id = "memory.profile",
            Title = "Profilo XMP/EXPO della RAM",
            Component = TuningComponent.Memory,
            Where = menus?.Settings.GetValueOrDefault(cpu.Vendor == CpuVendor.Amd ? "expo" : "xmp") ?? "BIOS: profilo della memoria (XMP/EXPO)",
            Steps =
            [
                "Aggiorna prima il BIOS: migliora la compatibilità con i profili della RAM (con DDR5 su AM5 serve un BIOS recente).",
                "Attiva il profilo 1 (XMP o EXPO), salva e riavvia.",
                "Verifica con \"pcopt scan\" che la velocità configurata sia quella di targa.",
                "Esegui il test della RAM dell'app (almeno 2 passaggi); per la conferma usa anche un test dedicato (TestMem5, Karhu, MemTest86).",
                "Se ci sono errori: prova il profilo 2 se esiste, altrimenti torna ai valori predefiniti. Non aumentare le tensioni a mano.",
            ],
            Limits = new StepLimits(1, 0, 1, "profilo", Risk.Medium),
            Tests = [StabilityTestKind.Memory],
            TestDuration = TimeSpan.FromMinutes(20),
            Blocked = vm ? "Non disponibile in una macchina virtuale."
                : laptop ? "Sui portatili i profili della RAM non sono quasi mai disponibili."
                : assessment.Board.MemoryOverclock == OcSupport.No ? $"Il chipset {assessment.Board.Chipset} non supporta i profili della RAM."
                : null,
        });

        return methods;
    }
}
