namespace PcOptimizer.Core.Model;

/// <summary>Livello di rischio di un intervento, mostrato all'utente come badge.</summary>
public enum Risk
{
    /// <summary>Nessun effetto collaterale noto, annullabile senza riavvio o quasi.</summary>
    Low,
    /// <summary>Può avere effetti collaterali (consumi, compatibilità) o richiede riavvio.</summary>
    Medium,
    /// <summary>Tocca sicurezza o stabilità: richiede conferma esplicita.</summary>
    High,
}

/// <summary>Fascia hardware del PC, calcolata dal modulo Hardware.</summary>
public enum HardwareTier
{
    Low,
    Mid,
    High,
}

/// <summary>Insieme di fasce a cui un intervento si applica.</summary>
[Flags]
public enum TierMask
{
    None = 0,
    Low = 1 << HardwareTier.Low,
    Mid = 1 << HardwareTier.Mid,
    High = 1 << HardwareTier.High,
    All = Low | Mid | High,
}

public static class TierMaskExtensions
{
    public static bool Includes(this TierMask mask, HardwareTier tier) =>
        (mask & (TierMask)(1 << (int)tier)) != 0;
}

/// <summary>Modulo di ottimizzazione a cui appartiene un intervento.</summary>
public enum TweakCategory
{
    System,
    Latency,
    Gpu,
    Network,
    StorageMemory,
}

/// <summary>Stato rilevato di un intervento sul sistema corrente.</summary>
public enum TweakStatus
{
    /// <summary>Nessuna delle impostazioni ha il valore desiderato.</summary>
    NotApplied,
    /// <summary>Tutte le impostazioni hanno già il valore desiderato.</summary>
    Applied,
    /// <summary>Solo una parte delle impostazioni ha il valore desiderato.</summary>
    PartiallyApplied,
    /// <summary>L'intervento non ha senso su questo sistema (hardware/OS non compatibile).</summary>
    NotApplicable,
}
