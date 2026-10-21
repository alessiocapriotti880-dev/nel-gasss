using System.Text.Json;
using System.Text.Json.Serialization;
using PcOptimizer.Core.Model;
using PcOptimizer.Hardware.Model;

namespace PcOptimizer.Tuning.Bios;

public sealed record BiosVendorMenus
{
    public BoardVendor Vendor { get; init; }

    public string Name { get; init; } = "";

    public string AdvancedModeKey { get; init; } = "";

    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();
}

public sealed record BiosMenusFile
{
    public IReadOnlyList<BiosVendorMenus> Vendors { get; init; } = [];

    public IReadOnlyDictionary<string, string> Oem { get; init; } = new Dictionary<string, string>();
}

/// <summary>Stato di una voce della checklist rispetto a quanto l'app riesce a verificare.</summary>
public enum ChecklistState
{
    /// <summary>Da fare: l'app ha rilevato che serve.</summary>
    Action,
    /// <summary>Già a posto secondo i dati rilevati.</summary>
    Done,
    /// <summary>L'app non può verificarlo da Windows: da controllare nel BIOS.</summary>
    Check,
    /// <summary>Solo informativo o sconsigliato su questo sistema.</summary>
    Info,
    /// <summary>Bloccato per sicurezza finché non si risolve un'altra voce (es. microcode).</summary>
    Blocked,
}

/// <summary>Una voce della checklist del BIOS: cosa fare, dove si trova nel menu, perché e con che rischio.</summary>
public sealed record BiosChecklistItem(
    string Id,
    string Title,
    ChecklistState State,
    string Why,
    string? MenuPath,
    string Recommendation,
    Risk Risk,
    string? Verify = null);

/// <summary>
/// Checklist del BIOS per questa scheda madre e CPU. L'app non scrive mai nel BIOS né aggiorna il firmware:
/// indica percorsi dei menu, valori consigliati e come verificare il risultato da Windows.
/// </summary>
public sealed class BiosGuide
{
    public const string FileName = "bios-menus.json";

    /// <summary>CPU Intel Core 13ª/14ª gen desktop soggette al problema di degrado "Vmin shift".</summary>
    public const string RaptorLakeArchitecture = "intel-raptorlake-s";

    /// <summary>Primo microcode con tutte le correzioni Intel per il degrado (0x12B, settembre 2024).</summary>
    public const uint RaptorLakeSafeMicrocode = 0x12B;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly BiosMenusFile _menus;

    private BiosGuide(BiosMenusFile menus) => _menus = menus;

    public static BiosGuide Load(params string[] overrideDirectories)
    {
        foreach (var dir in overrideDirectories)
        {
            var path = Path.Combine(dir, FileName);
            if (File.Exists(path))
            {
                using var file = File.OpenRead(path);
                return new BiosGuide(JsonSerializer.Deserialize<BiosMenusFile>(file, JsonOptions) ?? new BiosMenusFile());
            }
        }

        using var stream = typeof(BiosGuide).Assembly.GetManifestResourceStream("Data." + FileName)
            ?? throw new InvalidOperationException($"Risorsa incorporata mancante: {FileName}.");
        return new BiosGuide(JsonSerializer.Deserialize<BiosMenusFile>(stream, JsonOptions) ?? new BiosMenusFile());
    }

    public BiosVendorMenus? Menus(BoardVendor vendor) => _menus.Vendors.FirstOrDefault(v => v.Vendor == vendor);

    /// <summary>true per le CPU Raptor Lake con un microcode precedente alle correzioni (o non leggibile).</summary>
    public static bool RaptorLakeNeedsUpdate(HardwareProfile profile, HardwareAssessment assessment) =>
        assessment.Cpu.ArchitectureId == RaptorLakeArchitecture &&
        profile.Cpu.MicrocodeRevision is not >= RaptorLakeSafeMicrocode;

    public IReadOnlyList<BiosChecklistItem> Build(HardwareProfile profile, HardwareAssessment assessment)
    {
        var items = new List<BiosChecklistItem>();
        var board = assessment.Board;
        var menus = Menus(board.Vendor);
        var retail = menus is not null && !profile.Board.IsLaptop;
        var intel = profile.Cpu.Vendor == CpuVendor.Intel;
        var raptor = assessment.Cpu.ArchitectureId == RaptorLakeArchitecture;
        var raptorBlocked = RaptorLakeNeedsUpdate(profile, assessment);
        var bios = $"{profile.Board.BiosVendor} {profile.Board.BiosVersion}".Trim() +
                   (profile.Board.BiosReleaseDate is { } date ? $" del {date:dd/MM/yyyy}" : "");
        string? Menu(string key) => menus?.Settings.TryGetValue(key, out var path) == true ? path : null;

        if (board.IsVirtualMachine)
        {
            items.Add(new("bios.vm", "Macchina virtuale", ChecklistState.Info,
                "In una macchina virtuale il BIOS è quello dell'hypervisor: le voci seguenti valgono solo su un PC fisico.",
                null, "Esegui l'app sul PC fisico per una checklist utile.", Risk.Low));
        }

        items.Add(new("bios.update", "Aggiornamento del BIOS", raptorBlocked ? ChecklistState.Action : ChecklistState.Check,
            raptorBlocked
                ? $"CPU Intel 13ª/14ª gen con microcode {(profile.Cpu.MicrocodeRevision is { } mc ? $"0x{mc:X}" : "non leggibile")}: le versioni precedenti a 0x{RaptorLakeSafeMicrocode:X} " +
                  "possono applicare tensioni troppo alte e degradare la CPU in modo permanente."
                : "Gli aggiornamenti del BIOS correggono stabilità, compatibilità della RAM e sicurezza (microcode della CPU).",
            retail ? Menu("biosUpdate") : _menus.Oem.GetValueOrDefault("biosUpdate"),
            $"BIOS attuale: {bios}. Scarica la versione più recente SOLO dal sito del produttore della scheda madre (modello {profile.Board.BoardProduct}). " +
            "Non spegnere il PC durante l'aggiornamento.",
            Risk.Medium,
            "Dopo il riavvio esegui \"pcopt scan\": data del BIOS e microcode devono risultare aggiornati."));

        if (raptor)
        {
            items.Add(new("bios.intel-defaults", "Profilo \"Intel Default Settings\"", raptorBlocked ? ChecklistState.Action : ChecklistState.Check,
                "Molte schede madri impostavano limiti di potenza e corrente oltre le specifiche Intel. Il profilo predefinito Intel " +
                "applica i limiti corretti (PL1/PL2, ICCMax, AC/DC load line).",
                Menu("intelDefaults"), "Seleziona il profilo Intel Default Settings (o \"Intel Baseline/Performance\" se presente).",
                Risk.Low));
        }

        if (assessment.Memory.Xmp is XmpState.LikelyDisabled && !profile.Board.IsLaptop)
        {
            var amd = profile.Cpu.Vendor == CpuVendor.Amd;
            items.Add(new("bios.memory-profile", amd && assessment.Memory.Type == MemoryType.Ddr5 ? "Profilo EXPO della RAM" : "Profilo XMP/EXPO della RAM",
                assessment.Board.MemoryOverclock == OcSupport.No ? ChecklistState.Info : ChecklistState.Action,
                $"{assessment.Memory.XmpExplanation} Senza profilo la RAM lavora alla velocità base JEDEC: meno banda e frame time più alti nei giochi limitati dalla CPU.",
                Menu(amd ? "expo" : "xmp") ?? Menu("xmp"),
                assessment.Board.MemoryOverclock == OcSupport.No
                    ? $"Il chipset {board.Chipset} non permette di attivare i profili della RAM."
                    : "Attiva il profilo 1, salva e riavvia; poi esegui il test della RAM dell'app (sezione Stabilità). Se il PC non parte, il BIOS torna da solo ai valori sicuri dopo qualche tentativo.",
                Risk.Medium,
                "\"pcopt scan\" deve mostrare la velocità configurata uguale a quella di targa dei moduli."));
        }

        items.Add(new("bios.rebar", "Resizable BAR", ChecklistState.Check,
            "Permette alla CPU di accedere a tutta la memoria della scheda video: qualche FPS in più in molti giochi recenti (RTX 30 e successive, Radeon RX 6000 e successive, Intel Arc).",
            retail ? Menu("rebar") : null,
            "Abilita Above 4G Decoding e Re-Size BAR (richiede avvio UEFI, non CSM).",
            Risk.Low,
            "NVIDIA: Pannello di controllo NVIDIA > Informazioni di sistema > Resizable BAR: Sì. AMD: Adrenalin > Prestazioni > Smart Access Memory."));

        items.Add(new("bios.tpm-secureboot", "TPM e Avvio protetto", ChecklistState.Check,
            "Alcuni anti-cheat (Riot Vanguard, FACEIT su Windows 11, alcuni titoli recenti) richiedono TPM 2.0 e/o Secure Boot.",
            retail ? $"{Menu(intel ? "tpmIntel" : "tpmAmd")}; {Menu("secureBoot")}" : $"{_menus.Oem.GetValueOrDefault("tpm")} {_menus.Oem.GetValueOrDefault("secureBoot")}",
            profile.Board.SecureBootEnabled == true ? "Secure Boot è già attivo." : "Attivali se un gioco li richiede.",
            Risk.Low,
            "msinfo32 > Stato avvio protetto; tpm.msc per il TPM."));

        items.Add(new("bios.cstates", "Stati di risparmio della CPU (C-state)", ChecklistState.Info,
            "Disattivarli dal BIOS riduce leggermente la latenza di risveglio ma aumenta consumi e temperature e può ridurre il boost.",
            retail ? Menu("cstates") : null,
            "Lasciali attivi. Se vuoi provare, fallo dopo aver misurato e ripeti la misura di latenza: tieni la modifica solo se migliora.",
            Risk.Medium));

        if (retail)
        {
            items.Add(new("bios.fans", "Curve delle ventole", ChecklistState.Check,
                "Una curva troppo silenziosa lascia salire le temperature: con undervolt e overclock conta la temperatura stabile sotto carico.",
                Menu("fans"), "Imposta una curva che tenga la CPU sotto gli 85 °C durante il test di stabilità dell'app.", Risk.Low));
        }

        if (menus is null && !board.IsVirtualMachine)
        {
            items.Add(new("bios.oem", "BIOS di un PC di marca o portatile", ChecklistState.Info,
                "I BIOS dei PC di marca e dei portatili di solito non hanno opzioni di overclock né profili della RAM.",
                null, "Concentrati su aggiornamento del BIOS, temperature e impostazioni di Windows.", Risk.Low));
        }

        return items;
    }
}
