using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Windows;

/// <summary>Piano energetico attivo (PowerGetActiveScheme / PowerSetActiveScheme, come powercfg /setactive).</summary>
[SupportedOSPlatform("windows")]
public sealed class ActivePowerSchemeProvider : ISettingProvider
{
    public bool CanHandle(SettingTarget target) => target is ActivePowerSchemeTarget;

    public SettingValue Read(SettingTarget target) => SettingValue.Of(PowerSettingProvider.ActiveScheme().ToString("D"));

    public void Write(SettingTarget target, SettingValue value)
    {
        if (!value.Exists || !Guid.TryParse(value.Data, out var scheme))
        {
            throw new InvalidOperationException("Piano energetico non valido.");
        }

        var error = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        if (error != 0)
        {
            throw new InvalidOperationException($"Impossibile attivare il piano energetico {scheme} (errore {error}): forse non esiste su questo PC.");
        }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid scheme);
}

/// <summary>
/// Modalità di alimentazione di Windows 10/11 (cursore "Modalità risparmio energia"), cioè l'overlay del piano
/// Bilanciato. Usa le funzioni di powrprof impiegate dalle Impostazioni di Windows; se mancano (Windows più vecchio)
/// l'intervento non è applicabile.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PowerOverlayProvider : ISettingProvider
{
    public static bool IsSupported =>
        NativeLibrary.TryLoad("powrprof.dll", out var lib) &&
        NativeLibrary.TryGetExport(lib, "PowerSetActiveOverlayScheme", out _) &&
        NativeLibrary.TryGetExport(lib, "PowerGetEffectiveOverlayScheme", out _);

    public bool CanHandle(SettingTarget target) => target is PowerOverlayTarget;

    public SettingValue Read(SettingTarget target)
    {
        var error = PowerGetEffectiveOverlayScheme(out var overlay);
        if (error != 0)
        {
            throw new InvalidOperationException($"Modalità di alimentazione non leggibile (errore {error}).");
        }

        return SettingValue.Of(overlay.ToString("D"));
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        if (!value.Exists || !Guid.TryParse(value.Data, out var overlay))
        {
            throw new InvalidOperationException("Modalità di alimentazione non valida.");
        }

        var error = PowerSetActiveOverlayScheme(overlay);
        if (error != 0)
        {
            throw new InvalidOperationException($"Impossibile impostare la modalità di alimentazione (errore {error}).");
        }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveOverlayScheme(Guid overlay);
}
