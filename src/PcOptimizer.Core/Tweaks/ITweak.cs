using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Tweaks;

/// <summary>
/// Un intervento di ottimizzazione reversibile.
/// <para>
/// Un intervento non scrive mai direttamente sul sistema: dichiara quali impostazioni
/// vuole cambiare (<see cref="PlanChanges"/>) e il <see cref="TweakEngine"/> si occupa di
/// catturare i valori originali, registrarli nel giornale, applicare e, se serve, annullare.
/// Così ogni intervento è annullabile per costruzione.
/// </para>
/// </summary>
public interface ITweak
{
    /// <summary>Identificativo stabile (es. "system.power.latency-plan"), usato nel giornale.</summary>
    string Id { get; }

    string Title { get; }

    TweakCategory Category { get; }

    /// <summary>Cosa cambia, in termini concreti.</summary>
    string WhatChanges { get; }

    /// <summary>Perché dovrebbe aiutare (meccanismo tecnico).</summary>
    string Why { get; }

    /// <summary>Beneficio atteso, senza promesse: il risultato vero lo dice la misura.</summary>
    string ExpectedBenefit { get; }

    Risk Risk { get; }

    /// <summary>Fasce hardware per cui l'intervento è consigliato.</summary>
    TierMask AppliesTo { get; }

    bool RequiresReboot { get; }

    /// <summary>
    /// True se l'intervento riduce una protezione di sicurezza (Defender, VBS, Windows Update...):
    /// l'interfaccia mostra <see cref="SecurityWarning"/> e chiede conferma esplicita.
    /// </summary>
    bool AffectsSecurity => false;

    string? SecurityWarning => null;

    /// <summary>Come si annulla, a parole (il rollback vero è automatico).</summary>
    string HowToRevert => "Ripristino automatico dei valori originali dal giornale delle modifiche (rollback per singolo intervento o \"Ripristina tutto\").";

    /// <summary>Verifica se l'intervento ha senso su questo sistema. Sola lettura.</summary>
    bool IsApplicable(TweakContext context, out string? reason);

    /// <summary>Elenco delle impostazioni da cambiare. Sola lettura: non deve scrivere nulla.</summary>
    IReadOnlyList<SettingChange> PlanChanges(TweakContext context);
}

/// <summary>
/// Operazione di manutenzione NON reversibile (es. pulizia file temporanei, svuotamento
/// della standby list). Tenuta separata da <see cref="ITweak"/> proprio perché non ha rollback:
/// l'interfaccia lo dichiara esplicitamente e chiede un'anteprima prima di eseguirla.
/// </summary>
public interface IMaintenanceAction
{
    string Id { get; }

    string Title { get; }

    string Description { get; }

    Risk Risk { get; }

    /// <summary>Anteprima di cosa verrebbe fatto (es. file e spazio liberato), senza modificare nulla.</summary>
    Task<string> PreviewAsync(CancellationToken cancellationToken);

    Task<string> RunAsync(CancellationToken cancellationToken);
}
