using System.Globalization;
using PcOptimizer.Core.Model;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Presentation.ViewModels;

/// <summary>Formattazione dei valori per l'interfaccia, in italiano e indipendente dalla lingua di sistema.</summary>
public static class Format
{
    public static string Num(double value, string format = "0.#") =>
        value.ToString(format, CultureInfo.InvariantCulture).Replace('.', ',');

    public static string Gb(long bytes) => $"{Num(bytes / 1024.0 / 1024 / 1024, "0.#")} GB";

    public static string Risk(Risk r) => r switch
    {
        Core.Model.Risk.Low => "Rischio basso",
        Core.Model.Risk.Medium => "Rischio medio",
        _ => "Rischio alto",
    };

    public static string Evidence(TweakEvidence e) => e switch
    {
        TweakEvidence.Documented => "Documentato",
        TweakEvidence.Situational => "Dipende dal sistema",
        TweakEvidence.Debatable => "Da verificare con la misura",
        TweakEvidence.RestoreDefault => "Ripristino",
        _ => "Privacy",
    };

    public static string Status(TweakStatus s) => s switch
    {
        TweakStatus.Applied => "Già a posto",
        TweakStatus.PartiallyApplied => "Parziale",
        TweakStatus.NotApplied => "Da applicare",
        _ => "Non applicabile",
    };

    public static string Severity(FindingSeverity s) => s switch
    {
        FindingSeverity.Critical => "Critico",
        FindingSeverity.Warning => "Attenzione",
        FindingSeverity.Suggestion => "Consiglio",
        _ => "Info",
    };

    public static string Group(TweakGroup g) => g switch
    {
        TweakGroup.Power => "Alimentazione",
        TweakGroup.Services => "Servizi",
        TweakGroup.Startup => "Avvio automatico",
        TweakGroup.Tasks => "Attività pianificate",
        TweakGroup.Privacy => "Telemetria e privacy",
        TweakGroup.Visual => "Effetti visivi",
        TweakGroup.Gaming => "Impostazioni di gioco",
        TweakGroup.Gpu => "GPU e monitor",
        TweakGroup.Latency => "Latenza",
        TweakGroup.Network => "Rete",
        TweakGroup.Storage => "Archiviazione e memoria",
        TweakGroup.Security => "Sicurezza",
        TweakGroup.Defaults => "Ripristino dei valori predefiniti",
        _ => "Giochi",
    };

    public static string Tier(HardwareTier t) => t switch
    {
        HardwareTier.Low => "Bassa",
        HardwareTier.Mid => "Media",
        _ => "Alta",
    };
}
