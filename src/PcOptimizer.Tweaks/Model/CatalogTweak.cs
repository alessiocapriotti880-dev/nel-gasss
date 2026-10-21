using PcOptimizer.Benchmark.Comparison;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;

namespace PcOptimizer.Tweaks.Model;

/// <summary>Quanto è solida la base di un intervento: l'interfaccia lo mostra accanto al rischio.</summary>
public enum TweakEvidence
{
    /// <summary>Comportamento documentato da Microsoft o dai produttori, con effetto prevedibile.</summary>
    Documented,
    /// <summary>Aiuta solo in configurazioni specifiche (disco meccanico, poca RAM, certi driver).</summary>
    Situational,
    /// <summary>Risultati contrastanti: si tiene solo se la misura mostra un miglioramento.</summary>
    Debatable,
    /// <summary>Riporta al valore predefinito di Windows un'impostazione cambiata da altri tweak.</summary>
    RestoreDefault,
    /// <summary>Riguarda la privacy o la pulizia del sistema, non le prestazioni.</summary>
    Privacy,
}

/// <summary>Sezione dell'interfaccia in cui compare l'intervento.</summary>
public enum TweakGroup
{
    Power,
    Services,
    Startup,
    Tasks,
    Privacy,
    Visual,
    Gaming,
    Gpu,
    Latency,
    Network,
    Storage,
    Security,
    Defaults,
    Games,
}

/// <summary>Gruppi di grandezze del modulo Benchmark usate per giudicare un intervento.</summary>
public static class Measures
{
    public static readonly IReadOnlyList<string> Latency =
        [MetricCatalog.DpcMax, MetricCatalog.IsrMax, MetricCatalog.DpcIsrLoad, MetricCatalog.TimerP99, MetricCatalog.TimerMean];

    public static readonly IReadOnlyList<string> Background = [MetricCatalog.CpuBusy, MetricCatalog.DpcMax, MetricCatalog.TimerP99];

    public static readonly IReadOnlyList<string> Boot = [MetricCatalog.BootTime, MetricCatalog.CpuBusy];

    public static readonly IReadOnlyList<string> Frames = [MetricCatalog.FrameMean, MetricCatalog.FrameP99];

    public static readonly IReadOnlyList<string> Network = [MetricCatalog.PingGateway, MetricCatalog.JitterGateway, MetricCatalog.DpcIsrLoad];

    public static readonly IReadOnlyList<string> LatencyAndFrames = [.. Latency, .. Frames];

    /// <summary>Nessuna grandezza misurabile: l'intervento si tiene se l'utente lo sceglie (es. privacy, frequenza del monitor).</summary>
    public static readonly IReadOnlyList<string> None = [];
}

/// <summary>
/// Intervento del catalogo: metadati per l'interfaccia (rischio, evidenza, grandezze di verifica) più la logica
/// dichiarativa (applicabilità e modifiche). Le funzioni non leggono il sistema: lavorano sui dati del contesto,
/// così ogni intervento si prova con dati simulati.
/// </summary>
public sealed class CatalogTweak : ITweak
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required TweakCategory Category { get; init; }

    public required TweakGroup Group { get; init; }

    public required string WhatChanges { get; init; }

    public required string Why { get; init; }

    public required string ExpectedBenefit { get; init; }

    public Risk Risk { get; init; } = Risk.Low;

    public TierMask AppliesTo { get; init; } = TierMask.All;

    public bool RequiresReboot { get; init; }

    public bool AffectsSecurity { get; init; }

    public string? SecurityWarning { get; init; }

    public string HowToRevert { get; init; } =
        "Ripristino automatico dei valori originali dal giornale delle modifiche (rollback per singolo intervento o \"Ripristina tutto\").";

    public TweakEvidence Evidence { get; init; }

    /// <summary>Grandezze del Benchmark con cui si verifica l'effetto; vuoto = non misurabile.</summary>
    public IReadOnlyList<string> MeasuredBy { get; init; } = Measures.None;

    /// <summary>Preselezionato nel profilo della fascia di questo PC.</summary>
    public bool Recommended { get; init; }

    /// <summary>Ordine di applicazione: prima il piano energetico, poi le sue impostazioni, poi il resto.</summary>
    public int Order { get; init; } = 100;

    /// <summary>Avvertenza aggiuntiva mostrata prima di applicare (es. "le luci RGB tornano al profilo di base").</summary>
    public string? Caution { get; init; }

    /// <summary>Motivo per cui non si applica a questo sistema, o null se applicabile.</summary>
    public Func<TweakContext, string?> NotApplicableReason { get; init; } = _ => null;

    public required Func<TweakContext, IReadOnlyList<SettingChange>> Plan { get; init; }

    public bool IsMeasurable => MeasuredBy.Count > 0;

    bool ITweak.AffectsSecurity => AffectsSecurity;

    string? ITweak.SecurityWarning => SecurityWarning;

    string ITweak.HowToRevert => HowToRevert;

    public bool IsApplicable(TweakContext context, out string? reason)
    {
        reason = NotApplicableReason(context);
        return reason is null;
    }

    public IReadOnlyList<SettingChange> PlanChanges(TweakContext context) => Plan(context);

    public override string ToString() => $"{Id} ({Title})";
}
