using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Presentation.Services;
using PcOptimizer.Tweaks.Context;

namespace PcOptimizer.Presentation.ViewModels;

/// <summary>
/// Stato condiviso fra le pagine: ultima analisi, ultima misura, operazione in corso e messaggi di avanzamento.
/// Una sola operazione lunga alla volta: misure e modifiche non devono sovrapporsi.
/// </summary>
public sealed partial class AppState(IOptimizerServices services, IDialogService dialogs) : ObservableObject
{
    public IOptimizerServices Services { get; } = services;

    public IDialogService Dialogs { get; } = dialogs;

    [ObservableProperty]
    private SystemInspection? _inspection;

    [ObservableProperty]
    private BenchmarkRun? _latestRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "Pronto";

    private CancellationTokenSource? _cancel;

    public bool IsIdle => !IsBusy;

    /// <summary>Messaggi di avanzamento dell'operazione in corso (gli ultimi 200).</summary>
    public ObservableCollection<string> Activity { get; } = [];

    public event EventHandler? InspectionChanged;

    public GameLibrary Games => Services.Games();

    partial void OnInspectionChanged(SystemInspection? value) => InspectionChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Esegue un'operazione lunga con avanzamento e annullamento; gli errori diventano un messaggio, non un crash.</summary>
    public async Task<T?> RunAsync<T>(string title, Func<IProgress<string>, CancellationToken, Task<T>> operation) where T : class
    {
        if (IsBusy)
        {
            Dialogs.Inform("Operazione in corso", "Attendi la fine dell'operazione in corso.");
            return null;
        }

        IsBusy = true;
        Status = title + "...";
        _cancel = new CancellationTokenSource();
        var progress = new Progress<string>(Report);
        Report(title);
        try
        {
            var result = await operation(progress, _cancel.Token);
            Status = "Completato: " + title.ToLowerInvariant();
            return result;
        }
        catch (OperationCanceledException)
        {
            Status = "Annullato";
            Report("Operazione annullata.");
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Status = "Errore: " + ex.Message;
            Report("Errore: " + ex.Message);
            Dialogs.Inform("Errore", ex.Message);
            return null;
        }
        finally
        {
            IsBusy = false;
            _cancel.Dispose();
            _cancel = null;
        }
    }

    public void Cancel() => _cancel?.Cancel();

    public async Task InspectAsync()
    {
        var result = await RunAsync("Analisi del sistema", (p, ct) => Services.InspectAsync(Services.Games(), p, ct));
        if (result is not null)
        {
            Inspection = result;
        }
    }

    public void Report(string message)
    {
        Activity.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        while (Activity.Count > 200)
        {
            Activity.RemoveAt(0);
        }
    }
}
