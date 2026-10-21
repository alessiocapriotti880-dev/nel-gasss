using PcOptimizer.Core.Settings;

namespace PcOptimizer.Tweaks.Context;

/// <summary>Parametro avanzato del driver di rete (Proprietà della scheda > Avanzate).</summary>
/// <param name="Keyword">Nome del parametro nel registro, es. "*InterruptModeration", "*EEE".</param>
/// <param name="Allowed">Valori ammessi per i parametri a elenco; vuoto per quelli numerici.</param>
public sealed record AdapterParameter(
    string Keyword,
    string? Description,
    string? Current,
    string? Default,
    string Type,
    IReadOnlyList<string> Allowed,
    long? Min,
    long? Max)
{
    public bool Accepts(string value) =>
        Allowed.Count > 0
            ? Allowed.Contains(value, StringComparer.OrdinalIgnoreCase)
            : long.TryParse(value, out var n) && (Min is null || n >= Min) && (Max is null || n <= Max);
}

/// <summary>Configurazione della scheda di rete principale nel registro dei driver.</summary>
/// <param name="ClassKeyPath">Chiave di HKLM con i parametri del driver (Control\Class\{4d36e972-...}\00XX).</param>
public sealed record NetworkAdapterConfig(
    string Name,
    string InterfaceId,
    string ClassKeyPath,
    bool Wireless,
    IReadOnlyList<AdapterParameter> Parameters)
{
    public AdapterParameter? Parameter(string keyword) =>
        Parameters.FirstOrDefault(p => p.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Frequenze disponibili di un monitor alla risoluzione attuale.</summary>
public sealed record DisplayModes(string DeviceName, int CurrentHz, IReadOnlyList<int> AvailableHz);

/// <summary>
/// Dati di sistema raccolti per gli interventi oltre a quelli dell'analisi (modulo 3).
/// <see cref="ActivePowerScheme"/> va aggiornato dalla sessione dopo aver cambiato piano.
/// </summary>
public sealed record TweakEnvironment
{
    public NetworkAdapterConfig? PrimaryAdapter { get; init; }

    /// <summary>BitLocker attivo sull'unità di sistema: le modifiche al BCD possono chiedere la chiave di ripristino.</summary>
    public bool? BitLockerOnSystemDrive { get; init; }

    public bool PowerOverlaySupported { get; init; }

    public Guid? ActivePowerScheme { get; init; }

    public IReadOnlyList<DisplayModes> Displays { get; init; } = [];

    /// <summary>Valori di registro letti per gli interventi (vedi <c>TweakProbes</c>).</summary>
    public IReadOnlyDictionary<string, SettingValue> Registry { get; init; } = new Dictionary<string, SettingValue>();

    /// <summary>Cartella dove creare gli script di avvio dei giochi (Desktop dell'utente).</summary>
    public string? LaunchScriptFolder { get; init; }

    public SettingValue Reg(string id) => Registry.TryGetValue(id, out var v) ? v : SettingValue.Absent;
}

/// <summary>Un gioco indicato dall'utente: gli interventi per gioco usano solo il percorso dell'eseguibile.</summary>
public sealed record GameEntry(string Name, string ExecutablePath)
{
    public string ExecutableName => Path.GetFileName(ExecutablePath.Replace('\\', '/'));
}

public sealed record GameLibrary(IReadOnlyList<GameEntry> Games)
{
    public static readonly GameLibrary Empty = new([]);
}
