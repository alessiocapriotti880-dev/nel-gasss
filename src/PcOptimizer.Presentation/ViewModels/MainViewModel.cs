using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOptimizer.Optimization.Session;
using PcOptimizer.Presentation.Services;

namespace PcOptimizer.Presentation.ViewModels;

/// <summary>Finestra principale: navigazione fra le pagine, stato e avvio (analisi iniziale, ripresa dopo il riavvio).</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(IOptimizerServices services, IDialogService dialogs)
    {
        State = new AppState(services, dialogs);
        Benchmark = new BenchmarkViewModel(State);
        Optimization = new OptimizationViewModel(State);
        Dashboard = new DashboardViewModel(State, page => SelectedPage = page, () => Benchmark, () => Optimization);
        Analysis = new AnalysisViewModel(State);
        Tuning = new TuningViewModel(State);
        Log = new LogViewModel(State);
        Pages = [Dashboard, Analysis, Benchmark, Optimization, Tuning, Log];
        _selectedPage = Dashboard;
    }

    public AppState State { get; }

    public DashboardViewModel Dashboard { get; }

    public AnalysisViewModel Analysis { get; }

    public BenchmarkViewModel Benchmark { get; }

    public OptimizationViewModel Optimization { get; }

    public TuningViewModel Tuning { get; }

    public LogViewModel Log { get; }

    public IReadOnlyList<PageViewModel> Pages { get; }

    [ObservableProperty]
    private PageViewModel _selectedPage;

    public string ElevationText => State.Services.IsElevated
        ? "Amministratore"
        : "Senza privilegi di amministratore: misure DPC e modifiche non disponibili";

    /// <summary>All'avvio: analisi e, se richiesto (avvio dopo il riavvio) o in attesa, ripresa della sessione.</summary>
    public async Task InitializeAsync(bool resume)
    {
        Dashboard.Refresh();
        await State.InspectAsync();
        if (State.Services.CurrentSession() is { Phase: SessionPhase.AwaitingReboot })
        {
            SelectedPage = Optimization;
            if (resume)
            {
                await Optimization.ResumeCommand.ExecuteAsync(null);
            }
        }
    }

    [RelayCommand]
    private void Cancel() => State.Cancel();
}
