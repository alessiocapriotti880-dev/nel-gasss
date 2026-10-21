using System.Text.Json.Serialization;

namespace PcOptimizer.Core.Settings;

/// <summary>
/// Identifica in modo univoco una singola impostazione di sistema (un valore di registro,
/// il tipo di avvio di un servizio, un'impostazione del piano energetico...).
/// <para>
/// Tutti gli interventi sono espressi come "porta questa impostazione a questo valore":
/// in questo modo cattura, applicazione e rollback sono generici e non dipendono dal
/// codice del singolo intervento. Il giornale salva i target in JSON, per cui ogni
/// nuovo tipo va registrato con <see cref="JsonDerivedTypeAttribute"/>.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(RegistryValueTarget), "registry")]
[JsonDerivedType(typeof(ServiceStartTarget), "service")]
[JsonDerivedType(typeof(PowerSettingTarget), "power")]
[JsonDerivedType(typeof(ScheduledTaskTarget), "task")]
[JsonDerivedType(typeof(BcdElementTarget), "bcd")]
[JsonDerivedType(typeof(ActivePowerSchemeTarget), "powerscheme")]
[JsonDerivedType(typeof(PowerOverlayTarget), "poweroverlay")]
[JsonDerivedType(typeof(DisplayRefreshTarget), "display")]
[JsonDerivedType(typeof(FileContentTarget), "file")]
public abstract record SettingTarget
{
    /// <summary>Descrizione leggibile, usata in log e interfaccia.</summary>
    public abstract string Describe();
}

/// <summary>Radice del registro. Solo le due usate dagli interventi.</summary>
public enum RegHive
{
    LocalMachine,
    CurrentUser,
}

/// <summary>Tipo di un valore di registro.</summary>
public enum RegValueKind
{
    DWord,
    QWord,
    String,
    ExpandString,
    MultiString,
    Binary,
}

/// <summary>Un valore di registro. <see cref="ValueName"/> vuoto indica il valore predefinito.</summary>
public sealed record RegistryValueTarget(RegHive Hive, string KeyPath, string ValueName, RegValueKind Kind)
    : SettingTarget
{
    public string HiveName => Hive == RegHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";

    public override string Describe() =>
        $@"{HiveName}\{KeyPath}\{(ValueName.Length == 0 ? "(Predefinito)" : ValueName)}";

    // Il registro di Windows non distingue maiuscole/minuscole nei percorsi e nei nomi.
    public bool Equals(RegistryValueTarget? other) =>
        other is not null &&
        Hive == other.Hive &&
        string.Equals(KeyPath, other.KeyPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ValueName, other.ValueName, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(
        Hive,
        StringComparer.OrdinalIgnoreCase.GetHashCode(KeyPath),
        StringComparer.OrdinalIgnoreCase.GetHashCode(ValueName));
}

/// <summary>
/// Tipo di avvio di un servizio. Valori ammessi (vedi <see cref="ServiceStartValues"/>):
/// Boot, System, Automatic, AutomaticDelayed, Manual, Disabled.
/// </summary>
public sealed record ServiceStartTarget(string ServiceName) : SettingTarget
{
    public override string Describe() => $"Servizio {ServiceName} (tipo di avvio)";

    public bool Equals(ServiceStartTarget? other) =>
        other is not null && string.Equals(ServiceName, other.ServiceName, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(ServiceName);
}

public static class ServiceStartValues
{
    public const string Boot = "Boot";
    public const string System = "System";
    public const string Automatic = "Automatic";
    public const string AutomaticDelayed = "AutomaticDelayed";
    public const string Manual = "Manual";
    public const string Disabled = "Disabled";
}

/// <summary>Alimentazione a cui si riferisce un'impostazione del piano energetico.</summary>
public enum PowerSource
{
    /// <summary>Corrente alternata (alimentatore).</summary>
    Ac,
    /// <summary>Batteria.</summary>
    Dc,
}

/// <summary>
/// Un'impostazione di un piano energetico (equivalente a powercfg /setacvalueindex).
/// Il piano è sempre esplicito: quello attivo viene risolto quando si pianifica l'intervento,
/// così il rollback agisce sullo stesso piano anche se nel frattempo l'utente lo ha cambiato.
/// </summary>
public sealed record PowerSettingTarget(Guid Scheme, Guid SubGroup, Guid Setting, PowerSource Source, string Label)
    : SettingTarget
{
    public override string Describe() => $"Piano energetico {Scheme}: {Label} ({Source})";

    // L'etichetta è solo descrittiva e non partecipa all'identità.
    public bool Equals(PowerSettingTarget? other) =>
        other is not null && Scheme == other.Scheme && SubGroup == other.SubGroup &&
        Setting == other.Setting && Source == other.Source;

    public override int GetHashCode() => HashCode.Combine(Scheme, SubGroup, Setting, Source);
}

/// <summary>Stato di un'attività pianificata: valori <see cref="ScheduledTaskValues"/>; assente se l'attività non esiste.</summary>
public sealed record ScheduledTaskTarget(string TaskPath) : SettingTarget
{
    public override string Describe() => $"Attività pianificata {TaskPath}";

    public bool Equals(ScheduledTaskTarget? other) =>
        other is not null && string.Equals(TaskPath, other.TaskPath, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(TaskPath);
}

public static class ScheduledTaskValues
{
    public const string Enabled = "Enabled";
    public const string Disabled = "Disabled";
}

/// <summary>
/// Elemento della voce di avvio corrente ({current}) nella configurazione di avvio (BCD), scritto con bcdedit.
/// Booleani come "Yes"/"No"; assente = elemento non impostato (vale il default di Windows).
/// </summary>
public sealed record BcdElementTarget(string Element) : SettingTarget
{
    public override string Describe() => $"Configurazione di avvio: {Element}";

    public bool Equals(BcdElementTarget? other) =>
        other is not null && string.Equals(Element, other.Element, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Element);
}

/// <summary>Piano energetico attivo; il valore è il GUID del piano.</summary>
public sealed record ActivePowerSchemeTarget : SettingTarget
{
    public override string Describe() => "Piano energetico attivo";
}

/// <summary>
/// Modalità di alimentazione (overlay del piano Bilanciato: "Massime prestazioni" ecc.) quando il PC è collegato
/// all'alimentatore. Il valore è il GUID dell'overlay; quello nullo indica la modalità bilanciata predefinita.
/// </summary>
public sealed record PowerOverlayTarget : SettingTarget
{
    public override string Describe() => "Modalità di alimentazione (con alimentatore)";
}

/// <summary>Frequenza di aggiornamento di un monitor (nome GDI, es. \\.\DISPLAY1); il valore è in Hz.</summary>
public sealed record DisplayRefreshTarget(string DeviceName) : SettingTarget
{
    public override string Describe() => $"Frequenza di aggiornamento di {DeviceName}";

    public bool Equals(DisplayRefreshTarget? other) =>
        other is not null && string.Equals(DeviceName, other.DeviceName, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(DeviceName);
}

/// <summary>
/// Un file di testo creato dall'app (solo script .cmd di avvio dei giochi): il rollback lo cancella.
/// Il valore è il contenuto; assente = il file non esiste.
/// </summary>
public sealed record FileContentTarget(string FilePath) : SettingTarget
{
    public override string Describe() => $"File {FilePath}";

    public bool Equals(FileContentTarget? other) =>
        other is not null && string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(FilePath);
}
