using System.Globalization;

namespace PcOptimizer.Core.Settings;

/// <summary>
/// Valore di un'impostazione, serializzato come stringa per essere salvabile nel giornale.
/// <list type="bullet">
/// <item>DWord/QWord: numero decimale senza segno.</item>
/// <item>String/ExpandString: testo così com'è.</item>
/// <item>MultiString: elementi separati dal carattere NUL (\0).</item>
/// <item>Binary: esadecimale maiuscolo senza separatori.</item>
/// <item>Servizi: uno dei nomi in <see cref="ServiceStartValues"/>.</item>
/// <item>Piano energetico: numero decimale.</item>
/// </list>
/// <see cref="Exists"/> = false significa "il valore non esiste": il rollback lo cancella.
/// </summary>
public sealed record SettingValue(bool Exists, string? Data)
{
    public static SettingValue Absent { get; } = new(false, null);

    public static SettingValue Of(string data) => new(true, data);

    public static SettingValue Of(uint data) => new(true, data.ToString(CultureInfo.InvariantCulture));

    public static SettingValue Of(ulong data) => new(true, data.ToString(CultureInfo.InvariantCulture));

    public static SettingValue OfMulti(IEnumerable<string> items) => new(true, string.Join('\0', items));

    public static SettingValue OfBinary(byte[] data) => new(true, Convert.ToHexString(data));

    public uint AsUInt32() => uint.Parse(RequireData(), NumberStyles.None, CultureInfo.InvariantCulture);

    public ulong AsUInt64() => ulong.Parse(RequireData(), NumberStyles.None, CultureInfo.InvariantCulture);

    public string[] AsMulti() => RequireData().Length == 0 ? [] : RequireData().Split('\0');

    public byte[] AsBinary() => Convert.FromHexString(RequireData());

    private string RequireData() =>
        Exists && Data is not null ? Data : throw new InvalidOperationException("Il valore non esiste.");

    public override string ToString() => Exists ? Data ?? "" : "(assente)";
}

/// <summary>Una modifica richiesta: porta <see cref="Target"/> al valore <see cref="Desired"/>.</summary>
public sealed record SettingChange(SettingTarget Target, SettingValue Desired);

/// <summary>
/// Una modifica catturata prima dell'applicazione: il valore originale serve al rollback,
/// quello desiderato a capire chi "possiede" l'impostazione quando più interventi la toccano.
/// </summary>
public sealed record CapturedSetting(SettingTarget Target, SettingValue Original, SettingValue Desired);
