using PcOptimizer.Analysis.Model;
using static PcOptimizer.Cli.ConsoleOutput;

namespace PcOptimizer.Cli;

/// <summary>Stampa leggibile dell'analisi della configurazione di Windows.</summary>
internal static class SystemReportPrinter
{
    public static void Print(SystemSnapshot s, SystemReport r)
    {
        var p = r.Power;
        var overlay = p.OverlayMode is { } mode ? $" (modalità: {mode})" : "";
        Line("Alimentazione", $"{p.Name ?? "n/d"}{overlay}; CPU min {Percent(p.MinProcessorPercent)}, max {Percent(p.MaxProcessorPercent)}, " +
                              $"turbo {BoostName(p.BoostMode)}, C-state {(p.IdleDisabled == true ? "disattivati" : "attivi")}, " +
                              $"core parking min {Percent(p.CoreParkingMinCoresPercent)}");

        var t = r.Timer;
        Line("Timer", $"risoluzione attuale {Ms(t.CurrentMs)} (più fine {Ms(t.FinestMs)}); " +
                      $"HPET forzato: {YesNo(t.UsePlatformClock, "n/d (serve amministratore)")}; " +
                      $"dynamic tick disattivato: {YesNo(t.DisableDynamicTick, "n/d")}");

        foreach (var i in r.Interrupts.Where(i => i.Category is DeviceCategory.Gpu or DeviceCategory.Network or DeviceCategory.Audio))
        {
            var shared = i.SharesIrqWith.Count > 0 ? $", condiviso con {string.Join(", ", i.SharesIrqWith)}" : "";
            Line("Interrupt", $"{i.Name}: {ModeName(i.Mode)}{shared}{(i.CustomAffinity ? ", affinità personalizzata" : "")}");
        }

        var g = r.Gaming;
        Line("Gioco", $"modalità gioco: {YesNo(g.GameMode)}; HAGS: {HagsName(g.Hags)}; registrazione in background: {YesNo(g.BackgroundRecording)}; " +
                      $"ottimizzazioni giochi in finestra: {YesNo(g.WindowedOptimizations, "predefinito")}");

        var sec = r.Security;
        var hvci = sec.HvciRunning == true ? " (con integrità della memoria)" : "";
        Line("Sicurezza", $"VBS: {YesNo(sec.VbsRunning)}{hvci}; antivirus: {(sec.ActiveAntivirus.Count > 0 ? string.Join(", ", sec.ActiveAntivirus) : "nessuno rilevato")}; " +
                          $"Windows Update disattivato: {YesNo(sec.WindowsUpdateDisabled)}; TPM: {sec.TpmVersion ?? "n/d"}");

        var sw = r.Software;
        Line("Software", $"servizi {sw.ServicesTotal} (in esecuzione {sw.ServicesRunning}, di terze parti {sw.ThirdPartyServicesRunning}); " +
                         $"avvio automatico: {sw.StartupEnabled} attivi; attività pianificate: {sw.ScheduledTasksEnabled} attive " +
                         $"({sw.ThirdPartyTasksEnabled} di terze parti)");
        if (sw.BackgroundPrivateBytes > 0)
        {
            Line("", $"RAM dei programmi in background riconosciuti: {Num(sw.BackgroundPrivateBytes / GiB, "0.0")} GB");
        }

        foreach (var k in sw.Known)
        {
            Line(k.NeverTouch ? "Protetto" : "Rilevato", $"{k.Name}{(k.Running ? " [in esecuzione]" : "")}: {string.Join(", ", k.FoundIn)}");
        }

        if (r.ModifiedSettings.Count > 0)
        {
            Heading($"Impostazioni diverse dai valori predefiniti di Windows ({r.ModifiedSettings.Count})");
            foreach (var m in r.ModifiedSettings)
            {
                Console.WriteLine($"  - {m.Title}: {m.Current} (predefinito: {m.Default}) [{EffectName(m.Effect)}]");
            }
        }

        PrintFindings(r.Findings);

        if (!s.IsElevated)
        {
            Console.WriteLine("\n  Nota: per il quadro completo esegui \"pcopt analyze\" da un terminale come amministratore.");
        }
    }

    private static string Percent(uint? value) => value is { } v ? $"{v}%" : "n/d";

    private static string Ms(double? value) => value is { } v ? $"{Num(v, "0.###")} ms" : "n/d";

    private static string BoostName(uint? mode) => mode switch
    {
        null => "n/d",
        0 => "disattivato",
        _ => "attivo",
    };

    private static string ModeName(InterruptMode mode) => mode switch
    {
        InterruptMode.Msi => "MSI",
        InterruptMode.LineBased => "line-based",
        _ => "n/d",
    };

    private static string HagsName(HagsState state) => state switch
    {
        HagsState.On => "attivo",
        HagsState.Off => "disattivato",
        HagsState.NotSupported => "non supportato",
        _ => "n/d",
    };

    private static string EffectName(TweakEffect effect) => effect switch
    {
        TweakEffect.Placebo => "nessun effetto",
        TweakEffect.Harmful => "sfavorevole",
        TweakEffect.Debatable => "effetto da misurare",
        _ => "non riguarda le prestazioni",
    };
}
