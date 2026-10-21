namespace PcOptimizer.Core.Model;

/// <summary>Gravità di un'osservazione, dal semplice dato informativo al problema da risolvere subito.</summary>
public enum FindingSeverity
{
    /// <summary>Dato utile da sapere, nessuna azione necessaria.</summary>
    Info,
    /// <summary>Miglioramento consigliato.</summary>
    Suggestion,
    /// <summary>Problema che peggiora prestazioni o latenza in modo misurabile.</summary>
    Warning,
    /// <summary>Problema di stabilità, sicurezza o integrità dei dati: da risolvere prima di ottimizzare.</summary>
    Critical,
}

/// <summary>
/// Un'osservazione sul sistema (es. "monitor a 60 Hz ma supporta 144 Hz").
/// A differenza di un intervento, non modifica nulla: spiega cosa è stato rilevato e cosa fare.
/// </summary>
/// <param name="Id">Identificativo stabile, utile per filtri e test.</param>
/// <param name="Area">Componente a cui si riferisce (CPU, GPU, RAM, Disco, Rete, Monitor, Sistema).</param>
public sealed record Finding(
    string Id,
    FindingSeverity Severity,
    string Area,
    string Title,
    string Detail,
    string? Recommendation = null);
