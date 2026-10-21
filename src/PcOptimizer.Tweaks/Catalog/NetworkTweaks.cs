using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Tweaks.Context;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Catalog;

/// <summary>
/// Proprietà avanzate del driver della scheda di rete principale (come in Gestione dispositivi > Avanzate) e Nagle.
/// Si scrivono solo parametri che il driver dichiara, con valori che il driver accetta.
/// </summary>
internal static class NetworkTweaks
{
    private static readonly string[] EeeKeywords = ["*EEE", "EEE", "EnableGreenEthernet", "EEELinkAdvertisement", "AdvancedEEE", "GigaLite"];
    private static readonly string[] PowerSavingKeywords = ["PowerSavingMode", "ULPMode", "*NicAutoPowerSaver", "AutoPowerSaveModeEnabled", "EnablePowerManagement"];

    /// <summary>PnPCapabilities = 0x18: "Consenti al computer di spegnere il dispositivo" disattivato.</summary>
    internal const uint NoPowerOff = 0x18;

    public static IEnumerable<CatalogTweak> Build(TweakContext context)
    {
        var adapter = context.Env().PrimaryAdapter;
        var desktop = !context.IsLaptop();
        string? NoAdapter(TweakContext _) => adapter is null ? "Scheda di rete principale non rilevata." : null;

        yield return Adapter(
            "network.adapter.interrupt-moderation-off", "Moderazione degli interrupt disattivata", adapter,
            ["*InterruptModeration"], "0",
            "Disattiva la moderazione degli interrupt della scheda di rete.",
            "Con la moderazione la scheda raggruppa i pacchetti e genera meno interrupt, aggiungendo fino a qualche centinaio di " +
            "microsecondi di ritardo ai pacchetti dei giochi.",
            "Pacchetti consegnati prima; in cambio più interrupt e più tempo CPU per la rete.",
            TweakEvidence.Debatable, recommended: false, Measures.Network);

        yield return Adapter(
            "network.adapter.eee-off", "Ethernet a basso consumo (EEE) disattivato", adapter,
            EeeKeywords, "0",
            "Disattiva Energy Efficient Ethernet / Green Ethernet sulla scheda di rete.",
            "Con EEE il collegamento si addormenta nelle pause e il risveglio aggiunge latenza; con alcuni switch causa anche disconnessioni brevi.",
            "Latenza di rete più costante; consumi trascurabilmente più alti.",
            TweakEvidence.Situational, recommended: desktop && adapter is { Wireless: false }, Measures.Network);

        yield return new CatalogTweak
        {
            Id = "network.adapter.power-saving-off",
            Title = "Risparmio energetico della scheda di rete disattivato",
            Category = TweakCategory.Network,
            Group = TweakGroup.Network,
            WhatChanges = "Disattiva le funzioni di risparmio energetico del driver di rete e impedisce a Windows di spegnere la scheda.",
            Why = "Il risparmio energetico della scheda può introdurre ritardi al risveglio e brevi cadute di connessione.",
            ExpectedBenefit = "Connessione più stabile; effetto sulla latenza piccolo.",
            Risk = Risk.Low,
            Evidence = TweakEvidence.Situational,
            MeasuredBy = Measures.Network,
            RequiresReboot = true,
            Recommended = desktop,
            NotApplicableReason = c => NoAdapter(c) ?? (c.IsLaptop() ? "Sconsigliato sui portatili: aumenta i consumi a batteria." : null),
            Plan = _ => adapter is null ? [] :
            [
                .. ParameterChanges(adapter, PowerSavingKeywords, "0"),
                Reg.Set(Reg.Machine(adapter.ClassKeyPath, "PnPCapabilities"), NoPowerOff),
            ],
        };

        yield return new CatalogTweak
        {
            Id = "network.adapter.buffers-max",
            Title = "Buffer di ricezione e trasmissione al massimo",
            Category = TweakCategory.Network,
            Group = TweakGroup.Network,
            WhatChanges = "Porta al valore massimo consentito dal driver i buffer di ricezione e trasmissione della scheda di rete.",
            Why = "Buffer piccoli possono perdere pacchetti nei picchi di traffico (download in corso mentre si gioca).",
            ExpectedBenefit = "Meno pacchetti persi sotto carico; poca RAM in più usata dal driver.",
            Risk = Risk.Low,
            Evidence = TweakEvidence.Debatable,
            MeasuredBy = Measures.Network,
            RequiresReboot = true,
            NotApplicableReason = c => NoAdapter(c) ?? (MaxBufferChanges(adapter!).Count == 0 ? "Il driver non espone buffer regolabili." : null),
            Plan = _ => adapter is null ? [] : MaxBufferChanges(adapter),
        };

        yield return Adapter(
            "network.adapter.roaming-off", "Ricerca di reti Wi-Fi in background ridotta", adapter,
            ["RoamAggressiveness", "*RoamAggressiveness", "RoamingPreferredBandType"], "0",
            "Imposta al minimo l'aggressività di roaming della scheda Wi-Fi.",
            "Con il roaming aggressivo la scheda interrompe periodicamente il traffico per cercare punti di accesso migliori: " +
            "picchi di ping e DPC lunghi del driver Wi-Fi.",
            "Meno picchi di ping su Wi-Fi in postazione fissa.",
            TweakEvidence.Situational, recommended: desktop && adapter is { Wireless: true }, Measures.Network,
            notApplicable: _ => adapter is { Wireless: false } ? "La scheda principale non è Wi-Fi." : null);

        yield return new CatalogTweak
        {
            Id = "network.nagle-off",
            Title = "Algoritmo di Nagle disattivato",
            Category = TweakCategory.Network,
            Group = TweakGroup.Network,
            WhatChanges = "Imposta TcpAckFrequency = 1 e TCPNoDelay = 1 sull'interfaccia di rete principale.",
            Why = "Nagle raggruppa i piccoli pacchetti TCP e ritarda le conferme: alcuni giochi che usano TCP rispondono più in fretta senza.",
            ExpectedBenefit = "Nessun effetto sulla maggior parte dei giochi competitivi, che usano UDP. Non misurabile con il ping.",
            Risk = Risk.Low,
            Evidence = TweakEvidence.Debatable,
            MeasuredBy = Measures.None,
            NotApplicableReason = c =>
                NoAdapter(c) ?? (c.Snapshot().TcpInterfaces.Any(t => Same(t.InterfaceId, adapter!.InterfaceId) && (t.TcpAckFrequency ?? t.TcpNoDelay) is not null)
                    ? "Già impostato (anche da un altro programma): il valore predefinito si ripristina dalla sezione Ripristino."
                    : null),
            Plan = _ => adapter is null ? [] :
            [
                Reg.Set(Reg.Machine(InterfaceKey(adapter.InterfaceId), "TcpAckFrequency"), 1),
                Reg.Set(Reg.Machine(InterfaceKey(adapter.InterfaceId), "TCPNoDelay"), 1),
            ],
        };
    }

    internal static string InterfaceKey(string interfaceId) =>
        $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{{{interfaceId.Trim('{', '}')}}}";

    private static bool Same(string a, string b) => a.Trim('{', '}').Equals(b.Trim('{', '}'), StringComparison.OrdinalIgnoreCase);

    private static CatalogTweak Adapter(
        string id, string title, NetworkAdapterConfig? adapter, string[] keywords, string value, string what, string why, string benefit,
        TweakEvidence evidence, bool recommended, IReadOnlyList<string> measuredBy, Func<TweakContext, string?>? notApplicable = null) => new()
    {
        Id = id,
        Title = title,
        Category = TweakCategory.Network,
        Group = TweakGroup.Network,
        WhatChanges = what + (adapter is null ? "" : $" Scheda: {adapter.Name}."),
        Why = why,
        ExpectedBenefit = benefit,
        Risk = Risk.Low,
        Evidence = evidence,
        MeasuredBy = measuredBy,
        RequiresReboot = true,
        Recommended = recommended && adapter is not null && ParameterChanges(adapter, keywords, value).Count > 0,
        Caution = "Il nuovo valore vale dal riavvio (o dopo aver disattivato e riattivato la scheda di rete).",
        NotApplicableReason = c =>
            adapter is null ? "Scheda di rete principale non rilevata."
            : notApplicable?.Invoke(c)
            ?? (ParameterChanges(adapter, keywords, value).Count == 0 ? "Il driver di questa scheda non espone questa impostazione." : null),
        Plan = _ => adapter is null ? [] : ParameterChanges(adapter, keywords, value),
    };

    /// <summary>Modifiche per i parametri esistenti che accettano il valore richiesto.</summary>
    internal static IReadOnlyList<SettingChange> ParameterChanges(NetworkAdapterConfig adapter, IEnumerable<string> keywords, string value) =>
        keywords
            .Select(adapter.Parameter)
            .Where(p => p is not null && p.Accepts(value))
            .Select(p => Reg.Set(Reg.Machine(adapter.ClassKeyPath, p!.Keyword, RegValueKind.String), value))
            .ToList();

    internal static IReadOnlyList<SettingChange> MaxBufferChanges(NetworkAdapterConfig adapter) =>
        new[] { "*ReceiveBuffers", "*TransmitBuffers" }
            .Select(adapter.Parameter)
            .Where(p => p?.Max is not null)
            .Select(p => Reg.Set(Reg.Machine(adapter.ClassKeyPath, p!.Keyword, RegValueKind.String), p.Max!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
}
