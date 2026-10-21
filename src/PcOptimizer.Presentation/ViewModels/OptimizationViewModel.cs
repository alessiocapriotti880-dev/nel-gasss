using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOptimizer.Core.Journal;
using PcOptimizer.Core.Model;
using PcOptimizer.Optimization.Judging;
using PcOptimizer.Optimization.Planning;
using PcOptimizer.Optimization.Session;
using PcOptimizer.Presentation.Services;
using PcOptimizer.Tweaks;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Presentation.ViewModels;

/// <summary>Un intervento con interruttore, badge di rischio e dettagli.</summary>
public sealed partial class TweakItemViewModel : ObservableObject
{
    public TweakItemViewModel(CatalogTweak tweak, TweakStatus status, bool applicable, string? reason, bool satisfied, bool recommended, bool active)
    {
        Tweak = tweak;
        Status = satisfied ? "Già a posto" : Format.Status(status);
        IsApplicable = applicable;
        Reason = reason;
        IsRecommended = recommended;
        IsActive = active;
    }

    public CatalogTweak Tweak { get; }

    public string Id => Tweak.Id;

    public string Title => Tweak.Title;

    public string Status { get; }

    public bool IsApplicable { get; }

    public string? Reason { get; }

    public bool IsRecommended { get; }

    /// <summary>Applicato da questa app e ancora attivo: si può annullare singolarmente.</summary>
    public bool IsActive { get; }

    public string RiskText => Format.Risk(Tweak.Risk);

    /// <summary>"Low", "Medium" o "High": la vista sceglie il colore del badge.</summary>
    public string RiskLevel => Tweak.Risk.ToString();

    public string EvidenceText => Format.Evidence(Tweak.Evidence);

    public string Flags => string.Join(" · ", new[]
    {
        Tweak.RequiresReboot ? "richiede riavvio" : null,
        Tweak.IsMeasurable ? "verificato con la misura" : "non misurabile",
        Tweak.AffectsSecurity ? "riduce la sicurezza" : null,
    }.Where(f => f is not null));

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Conferma esplicita richiesta per gli interventi che riducono una protezione.</summary>
    [ObservableProperty]
    private bool _securityConfirmed;

    [ObservableProperty]
    private bool _isExpanded;
}

public sealed record TweakGroupViewModel(string Name, IReadOnlyList<TweakItemViewModel> Items);

public sealed record SessionResultRow(string Outcome, string Title, string? Explanation);

/// <summary>Azione di manutenzione con anteprima obbligatoria prima dell'esecuzione.</summary>
public sealed partial class MaintenanceItemViewModel(MaintenanceInfo info, AppState state) : ObservableObject
{
    public MaintenanceInfo Info { get; } = info;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _previewed;

    [ObservableProperty]
    private string _result = "";

    [RelayCommand]
    private async Task PreviewAsync()
    {
        Result = await state.RunAsync("Anteprima: " + Info.Title, (_, ct) => state.Services.PreviewMaintenanceAsync(Info.Id, ct)) ?? Result;
        Previewed = true;
    }

    [RelayCommand(CanExecute = nameof(Previewed))]
    private async Task RunAsync()
    {
        if (!state.Dialogs.Confirm(Info.Title, $"{Info.Description}\n\n{Result}\n\nOperazione non annullabile. Procedere?"))
        {
            return;
        }

        Result = await state.RunAsync(Info.Title, (_, ct) => state.Services.RunMaintenanceAsync(Info.Id, ct)) ?? Result;
        Previewed = false;
    }
}

/// <summary>
/// Ottimizzazione: interventi per sezione con interruttori, profilo consigliato per la fascia, applicazione con
/// sessione misurata, ripristino singolo e totale, indicazioni guidate, manutenzione e giochi.
/// </summary>
public sealed partial class OptimizationViewModel : PageViewModel
{
    public OptimizationViewModel(AppState state) : base(state, "Ottimizzazione", "")
    {
        foreach (var m in state.Services.Maintenance())
        {
            Maintenance.Add(new MaintenanceItemViewModel(m, state));
        }

        RefreshGames();
        LoadSession(state.Services.CurrentSession());
    }

    public ObservableCollection<TweakGroupViewModel> Groups { get; } = [];

    public ObservableCollection<InfoRow> Guidance { get; } = [];

    public ObservableCollection<MaintenanceItemViewModel> Maintenance { get; } = [];

    public ObservableCollection<SessionResultRow> SessionResults { get; } = [];

    public ObservableCollection<string> SessionEvents { get; } = [];

    public ObservableCollection<InfoRow> Games { get; } = [];

    [ObservableProperty]
    private string _profileDescription = "Avvia l'analisi per vedere gli interventi adatti a questo PC.";

    [ObservableProperty]
    private string _selectionSummary = "";

    [ObservableProperty]
    private string _sessionSummary = "";

    [ObservableProperty]
    private bool _measure = true;

    [ObservableProperty]
    private int _measureSeconds = 60;

    [ObservableProperty]
    private bool _showNotApplicable;

    [ObservableProperty]
    private bool _canResume;

    public IEnumerable<TweakItemViewModel> AllItems => Groups.SelectMany(g => g.Items);

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var item in AllItems)
        {
            item.IsSelected = item.IsRecommended && item.IsApplicable;
        }

        UpdateSummary();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in AllItems)
        {
            item.IsSelected = false;
        }

        UpdateSummary();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (State.Inspection is not { } inspection)
        {
            State.Dialogs.Inform("Analisi necessaria", "Esegui prima l'analisi del sistema.");
            return;
        }

        var selected = AllItems.Where(i => i.IsSelected).ToList();
        var plan = PlanBuilder.Build(inspection.Tweaks, selected.Select(i => i.Id), inspection.Context,
            selected.Where(i => i.Tweak.AffectsSecurity && i.SecurityConfirmed).Select(i => i.Id));
        if (!plan.IsValid)
        {
            State.Dialogs.Inform("Selezione da correggere", plan.Problems.Count > 0 ? string.Join("\n\n", plan.Problems) : "Nessun intervento selezionato.");
            return;
        }

        var message = $"Verranno applicati {plan.Immediate.Count + plan.AfterReboot.Count} interventi" +
                      (plan.RequiresReboot ? $" ({plan.AfterReboot.Count} dopo il riavvio)" : "") + ".\n\n" +
                      "Prima viene creato un punto di ripristino: se non è possibile, non viene modificato nulla.\n" +
                      (Measure
                          ? $"Il sistema verrà misurato prima e dopo ogni intervento (circa {MeasureSeconds} s per misura): non usare il PC. " +
                            "Gli interventi senza miglioramento misurabile vengono annullati automaticamente."
                          : "Applicazione SENZA misure: nessun annullamento automatico.") +
                      string.Concat(plan.All.Where(t => t.AffectsSecurity).Select(t => $"\n\nATTENZIONE - {t.Title}: {t.SecurityWarning}"));
        if (!State.Dialogs.Confirm("Applicare le ottimizzazioni?", message))
        {
            return;
        }

        SessionEvents.Clear();
        var options = new SessionOptions { Measure = Measure };
        var session = await State.RunAsync("Ottimizzazione in corso", (p, ct) =>
            State.Services.ApplyAsync(plan, inspection.Context, options, new Progress<string>(m =>
            {
                SessionEvents.Add(m);
                p.Report(m);
            }), ct));
        LoadSession(session ?? State.Services.CurrentSession());
        await State.InspectAsync();
    }

    [RelayCommand]
    private async Task ResumeAsync()
    {
        if (State.Inspection is not { } inspection)
        {
            await State.InspectAsync();
            inspection = State.Inspection;
        }

        if (inspection is null)
        {
            return;
        }

        var session = await State.RunAsync("Ripresa della sessione dopo il riavvio", (p, ct) => State.Services.ResumeSessionAsync(inspection.Tweaks, p, ct));
        LoadSession(session ?? State.Services.CurrentSession());
        await State.InspectAsync();
    }

    [RelayCommand]
    private async Task RevertAllAsync()
    {
        if (!State.Dialogs.Confirm("Ripristina tutto", "Tutti gli interventi applicati dall'app verranno annullati, dal più recente. Procedere?"))
        {
            return;
        }

        var report = State.Services.RevertAll();
        State.Dialogs.Inform("Ripristina tutto",
            $"Annullati {report.Reverted.Count} interventi." +
            (report.Failed.Count > 0 ? $"\n{report.Failed.Count} non annullati: usa il punto di ripristino o i backup .reg." : "") +
            "\nRiavvia il PC se alcuni interventi richiedevano il riavvio.");
        await State.InspectAsync();
    }

    [RelayCommand]
    private async Task RevertItemAsync(TweakItemViewModel? item)
    {
        if (item is null || !State.Dialogs.Confirm("Annulla intervento", $"Annullare \"{item.Title}\"?"))
        {
            return;
        }

        if (!State.Services.RevertTweak(item.Id))
        {
            State.Dialogs.Inform("Annulla intervento", "Nessuna modifica attiva da annullare per questo intervento.");
        }

        await State.InspectAsync();
    }

    [RelayCommand]
    private async Task AddGameAsync()
    {
        if (State.Dialogs.PickExecutable() is not { } path)
        {
            return;
        }

        State.Services.AddGame(path, null);
        RefreshGames();
        await State.InspectAsync();
    }

    [RelayCommand]
    private async Task RemoveGameAsync(InfoRow? game)
    {
        if (game is null)
        {
            return;
        }

        State.Services.RemoveGame(game.Value);
        RefreshGames();
        await State.InspectAsync();
    }

    partial void OnShowNotApplicableChanged(bool value) => OnInspectionChanged();

    protected override void OnInspectionChanged()
    {
        if (State.Inspection is not { } inspection)
        {
            return;
        }

        var selected = AllItems.Where(i => i.IsSelected).Select(i => i.Id).ToHashSet();
        var context = inspection.Context;
        var recommended = TweakCatalog.Recommended(inspection.Tweaks, context).Select(t => t.Id).ToHashSet();
        var active = State.Services.Journal().Where(t => t.State == TransactionState.Active).Select(t => t.TweakId).ToHashSet();
        ProfileDescription = TierProfiles.Describe(context.Tier);

        Groups.Clear();
        foreach (var group in inspection.Tweaks.GroupBy(t => t.Group).OrderBy(g => g.Key))
        {
            var items = group.Select(t =>
                {
                    var applicable = t.IsApplicable(context, out var reason);
                    var satisfied = t.IsSatisfied(context, out var satisfiedMessage);
                    var status = inspection.Statuses.GetValueOrDefault(t.Id, TweakStatus.NotApplicable);
                    return new TweakItemViewModel(t, status, applicable && status != TweakStatus.Applied, satisfied ? satisfiedMessage : reason,
                        satisfied, recommended.Contains(t.Id), active.Contains(t.Id))
                    {
                        IsSelected = selected.Contains(t.Id),
                    };
                })
                .Where(i => ShowNotApplicable || i.IsApplicable || i.IsActive || i.Status == "Già a posto")
                .ToList();
            if (items.Count == 0)
            {
                continue;
            }

            foreach (var item in items)
            {
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(TweakItemViewModel.IsSelected))
                    {
                        UpdateSummary();
                    }
                };
            }

            Groups.Add(new TweakGroupViewModel(Format.Group(group.Key), items));
        }

        Guidance.Clear();
        foreach (var g in inspection.Guidance)
        {
            Guidance.Add(new InfoRow(g.Title, g.Summary + "\n" + string.Join("\n", g.Steps.Select((s, i) => $"{i + 1}. {s}")) + (g.Warning is null ? "" : $"\nAttenzione: {g.Warning}")));
        }

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var selected = AllItems.Where(i => i.IsSelected).ToList();
        SelectionSummary = selected.Count == 0
            ? "Nessun intervento selezionato."
            : $"{selected.Count} selezionati: {selected.Count(i => i.Tweak.Risk == Risk.Low)} a rischio basso, {selected.Count(i => i.Tweak.Risk == Risk.Medium)} medio, " +
              $"{selected.Count(i => i.Tweak.Risk == Risk.High)} alto; {selected.Count(i => i.Tweak.RequiresReboot)} richiedono il riavvio.";
    }

    private void RefreshGames()
    {
        Games.Clear();
        foreach (var g in State.Services.Games().Games)
        {
            Games.Add(new InfoRow(g.Name, g.ExecutablePath));
        }
    }

    internal void LoadSession(OptimizationSession? session)
    {
        SessionResults.Clear();
        CanResume = session?.Phase == SessionPhase.AwaitingReboot;
        if (session is null)
        {
            SessionSummary = "";
            return;
        }

        SessionSummary = session.Phase switch
        {
            SessionPhase.AwaitingReboot => "In attesa del riavvio: dopo l'accesso la sessione riprende da sola (o premi Riprendi).",
            SessionPhase.Aborted => "Sessione interrotta senza modifiche: " + session.AbortReason,
            SessionPhase.Completed => $"Sessione completata il {session.EndedAt:dd/MM/yyyy HH:mm}." + (session.RevertNeedsReboot ? " Riavvia perché gli annullamenti abbiano effetto." : ""),
            _ => "Sessione in corso.",
        };
        if (session.RestorePoint is { } rp)
        {
            SessionSummary += $"\nPunto di ripristino: {rp.Message}";
        }

        foreach (var r in session.Results)
        {
            var outcome = r.Error is not null && !r.Applied ? "Errore"
                : r.Reverted ? "Annullato"
                : r.Decision == Decision.Keep ? "Tenuto"
                : r.AfterReboot && r.Decision is null ? "Dopo il riavvio"
                : "Applicato";
            SessionResults.Add(new SessionResultRow(outcome, r.Title, r.Explanation ?? r.Error));
        }
    }
}
