using System.Text.Json;
using System.Text.Json.Serialization;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Catalog;

public sealed record ServiceRule
{
    public string Name { get; init; } = "";

    public string Title { get; init; } = "";

    public string StartMode { get; init; } = ServiceStartValues.Manual;

    public TweakEvidence Evidence { get; init; }

    public string Why { get; init; } = "";

    public string Benefit { get; init; } = "";

    public string? Caution { get; init; }

    public IReadOnlyList<string> When { get; init; } = [];

    public IReadOnlyList<HardwareTier> RecommendedTiers { get; init; } = [];
}

public sealed record ServiceRuleFile
{
    public IReadOnlyList<ServiceRule> Services { get; init; } = [];

    public IReadOnlyList<string> Protected { get; init; } = [];
}

/// <summary>
/// Tipo di avvio dei servizi: una lista curata di servizi di Windows (Data/services.json) e i servizi di terze parti
/// riconosciuti dal catalogo del software (aggiornamento, telemetria, RGB, "ottimizzatori" di rete).
/// Il servizio non viene fermato subito: il nuovo tipo di avvio vale dal riavvio successivo.
/// </summary>
public static class ServiceTweaks
{
    public const string FileName = "services.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Lazy<ServiceRuleFile> Embedded = new(() =>
    {
        using var stream = typeof(ServiceTweaks).Assembly.GetManifestResourceStream("Data." + FileName)
            ?? throw new InvalidOperationException($"Risorsa incorporata mancante: {FileName}.");
        return JsonSerializer.Deserialize<ServiceRuleFile>(stream, JsonOptions) ?? new ServiceRuleFile();
    });

    public static ServiceRuleFile Rules => Embedded.Value;

    /// <summary>true se il servizio è nella lista di quelli da non toccare mai.</summary>
    public static bool IsProtected(string serviceName) =>
        Rules.Protected.Any(p => p.EndsWith('*')
            ? serviceName.StartsWith(p[..^1], StringComparison.OrdinalIgnoreCase)
            : serviceName.Equals(p, StringComparison.OrdinalIgnoreCase));

    internal static IEnumerable<CatalogTweak> Build(TweakContext context)
    {
        var services = context.Snapshot().Services;
        foreach (var rule in Rules.Services)
        {
            if (IsProtected(rule.Name))
            {
                continue; // difesa in profondità: un errore nel file dati non può toccare un servizio protetto
            }

            var service = services.FirstOrDefault(s => s.Name.Equals(rule.Name, StringComparison.OrdinalIgnoreCase));
            yield return new CatalogTweak
            {
                Id = $"services.windows.{rule.Name.ToLowerInvariant()}",
                Title = rule.Title,
                Category = TweakCategory.System,
                Group = rule.Evidence == TweakEvidence.Privacy ? TweakGroup.Privacy : TweakGroup.Services,
                WhatChanges = $"Tipo di avvio del servizio {rule.Name}: {StartName(rule.StartMode)} (dal prossimo riavvio).",
                Why = rule.Why,
                ExpectedBenefit = rule.Benefit,
                Risk = Risk.Low,
                Evidence = rule.Evidence,
                MeasuredBy = rule.Evidence == TweakEvidence.Privacy ? Measures.None : [.. Measures.Background, .. Measures.Boot],
                RequiresReboot = true,
                Recommended = rule.RecommendedTiers.Contains(context.Tier) && ConditionsMet(rule, context),
                Caution = rule.Caution,
                NotApplicableReason = c =>
                    service is null ? "Servizio non presente su questo PC."
                    : !ConditionsMet(rule, c) ? ConditionText(rule)
                    : null,
                Plan = _ => [new(new ServiceStartTarget(rule.Name), SettingValue.Of(rule.StartMode))],
            };
        }

        foreach (var tweak in ThirdParty(context))
        {
            yield return tweak;
        }
    }

    private static IEnumerable<CatalogTweak> ThirdParty(TweakContext context)
    {
        if (context.Software() is not { } catalog)
        {
            yield break;
        }

        foreach (var service in context.Snapshot().Services)
        {
            if (IsProtected(service.Name) ||
                Rules.Services.Any(r => r.Name.Equals(service.Name, StringComparison.OrdinalIgnoreCase)) ||
                service.StartMode is not (ServiceStartValues.Automatic or ServiceStartValues.AutomaticDelayed))
            {
                continue;
            }

            var entry = catalog.MatchService(service.Name);
            if (entry is null || entry.NeverTouch || entry.Category is SoftwareCategory.AntiCheat or SoftwareCategory.Security
                    or SoftwareCategory.Launcher or SoftwareCategory.Xbox or SoftwareCategory.Browser)
            {
                continue;
            }

            // Software segnalato per latenza, instabilità o conflitti: disattivato. Gli altri restano avviabili a richiesta.
            var harmful = entry.Risk is SoftwareRisk.Latency or SoftwareRisk.Stability or SoftwareRisk.Conflict;
            var mode = harmful ? ServiceStartValues.Disabled : ServiceStartValues.Manual;
            var recommended = harmful || entry.Category is SoftwareCategory.Updater or SoftwareCategory.Telemetry;
            yield return new CatalogTweak
            {
                Id = $"services.thirdparty.{Slug(service.Name)}",
                Title = $"{entry.Name}: servizio {service.DisplayName}",
                Category = TweakCategory.System,
                Group = TweakGroup.Services,
                WhatChanges = $"Tipo di avvio del servizio \"{service.DisplayName}\" ({service.Name}): da {StartName(service.StartMode)} a {StartName(mode)}.",
                Why = harmful
                    ? $"{entry.Name} è noto per aggiungere latenza o instabilità{(entry.Note is null ? "" : $": {entry.Note}")}"
                    : $"Servizio di {CategoryName(entry.Category)} avviato a ogni accensione anche quando non serve.",
                ExpectedBenefit = "Meno processi e attività in background; avvio più rapido.",
                Risk = entry.Category is SoftwareCategory.Rgb or SoftwareCategory.Audio or SoftwareCategory.Network ? Risk.Medium : Risk.Low,
                Evidence = harmful ? TweakEvidence.Situational : TweakEvidence.Documented,
                MeasuredBy = [.. Measures.Background, .. Measures.Boot],
                RequiresReboot = true,
                Recommended = recommended,
                Caution = entry.Category switch
                {
                    SoftwareCategory.Rgb => "Le luci RGB e i profili delle periferiche potrebbero tornare a quelli predefiniti finché non apri il programma.",
                    SoftwareCategory.Audio => "Gli effetti audio del programma non saranno attivi.",
                    SoftwareCategory.Network => "Le funzioni di priorità del traffico del programma non saranno attive (di solito è un vantaggio).",
                    _ => null,
                },
                Plan = _ => [new(new ServiceStartTarget(service.Name), SettingValue.Of(mode))],
            };
        }
    }

    private static bool ConditionsMet(ServiceRule rule, TweakContext c) => rule.When.All(w => w switch
    {
        "hdd" => c.SystemOnHdd(),
        "ssd" => !c.SystemOnHdd() && c.HasSsd(),
        "lowRam" => c.RamGb() <= 8.5,
        "desktop" => !c.IsLaptop(),
        _ => false,
    });

    private static string ConditionText(ServiceRule rule) => rule.When.FirstOrDefault() switch
    {
        "hdd" => "Utile solo con Windows installato su un disco meccanico.",
        "ssd" => "Proposto solo con Windows installato su SSD.",
        "lowRam" => "Utile solo con 8 GB di RAM o meno.",
        "desktop" => "Non adatto ai portatili.",
        _ => "Condizioni non soddisfatte su questo PC.",
    };

    internal static string StartName(string mode) => mode switch
    {
        ServiceStartValues.Automatic => "automatico",
        ServiceStartValues.AutomaticDelayed => "automatico ritardato",
        ServiceStartValues.Manual => "manuale",
        ServiceStartValues.Disabled => "disabilitato",
        _ => mode,
    };

    internal static string CategoryName(SoftwareCategory c) => c switch
    {
        SoftwareCategory.Updater => "aggiornamento automatico",
        SoftwareCategory.Telemetry => "telemetria",
        SoftwareCategory.Rgb => "illuminazione RGB e periferiche",
        SoftwareCategory.Monitoring => "monitoraggio",
        SoftwareCategory.Cloud => "sincronizzazione cloud",
        SoftwareCategory.Optimizer => "ottimizzazione",
        SoftwareCategory.Network => "gestione della rete",
        SoftwareCategory.Audio => "elaborazione audio",
        _ => "supporto",
    };

    internal static string Slug(string text) =>
        string.Join('-', new string(text.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray())
            .Split('-', StringSplitOptions.RemoveEmptyEntries));
}
