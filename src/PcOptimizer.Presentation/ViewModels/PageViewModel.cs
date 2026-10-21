using CommunityToolkit.Mvvm.ComponentModel;

namespace PcOptimizer.Presentation.ViewModels;

/// <summary>Pagina dell'interfaccia: titolo, icona (carattere Segoe Fluent Icons) e accesso allo stato condiviso.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    protected PageViewModel(AppState state, string title, string icon)
    {
        State = state;
        Title = title;
        Icon = icon;
        state.InspectionChanged += (_, _) => OnInspectionChanged();
    }

    public AppState State { get; }

    public string Title { get; }

    public string Icon { get; }

    /// <summary>Aggiorna i dati della pagina dopo una nuova analisi.</summary>
    protected virtual void OnInspectionChanged()
    {
    }
}

/// <summary>Riga etichetta/valore delle schede di riepilogo.</summary>
public sealed record InfoRow(string Label, string Value);

/// <summary>Osservazione mostrata in elenco, con il livello per il colore.</summary>
public sealed record FindingItem(string Severity, string Level, string Area, string Title, string Detail, string? Recommendation);
