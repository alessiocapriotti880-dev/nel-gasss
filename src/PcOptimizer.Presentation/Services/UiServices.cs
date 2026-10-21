namespace PcOptimizer.Presentation.Services;

/// <summary>Finestre di dialogo dell'interfaccia (implementate da WPF, simulate nei test).</summary>
public interface IDialogService
{
    bool Confirm(string title, string message);

    void Inform(string title, string message);

    /// <summary>Scelta di un eseguibile di gioco; null se annullata.</summary>
    string? PickExecutable();

    void OpenFolder(string path);
}
