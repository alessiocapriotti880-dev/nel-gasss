using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using PcOptimizer.Core.Journal;

namespace PcOptimizer.Presentation.ViewModels;

public sealed record JournalRow(string When, string State, string Title, string TweakId, string Changes);

/// <summary>Registro: giornale delle modifiche (con valori originali e nuovi), attività recenti e log dell'app.</summary>
public sealed partial class LogViewModel : PageViewModel
{
    public LogViewModel(AppState state) : base(state, "Registro", "") => Refresh();

    public ObservableCollection<JournalRow> Journal { get; } = [];

    public ObservableCollection<string> LogLines { get; } = [];

    public string DataFolder => State.Services.DataFolder;

    [RelayCommand]
    public void Refresh()
    {
        Journal.Clear();
        foreach (var t in State.Services.Journal().OrderByDescending(t => t.Sequence))
        {
            Journal.Add(new JournalRow(
                t.StartedAt.ToString("dd/MM/yyyy HH:mm"),
                t.State switch
                {
                    TransactionState.Active => "Attivo",
                    TransactionState.Reverted => "Annullato",
                    TransactionState.Aborted => "Non applicato",
                    _ => "Interrotto",
                },
                t.Title,
                t.TweakId,
                string.Join("\n", t.Settings.Select(s => $"{s.Target.Describe()}: {s.Original} → {s.Desired}"))));
        }

        LogLines.Clear();
        foreach (var line in State.Services.LogTail(300))
        {
            LogLines.Add(line);
        }
    }

    [RelayCommand]
    private void OpenDataFolder() => State.Dialogs.OpenFolder(DataFolder);

    protected override void OnInspectionChanged() => Refresh();
}
