using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using PcOptimizer.Presentation.Services;

namespace PcOptimizer.App.Services;

/// <summary>Finestre di dialogo standard di Windows.</summary>
public sealed class WpfDialogService : IDialogService
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    public void Inform(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow!, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public string? PickExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Scegli l'eseguibile del gioco",
            Filter = "Programmi (*.exe)|*.exe",
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void OpenFolder(string path)
    {
        if (System.IO.Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }
}

/// <summary>Finestre di dialogo per la prova automatica dell'interfaccia: nessuna interazione, sempre "no".</summary>
public sealed class SilentDialogService : IDialogService
{
    public List<string> Messages { get; } = [];

    public bool Confirm(string title, string message)
    {
        Messages.Add($"[conferma] {title}: {message}");
        return false;
    }

    public void Inform(string title, string message) => Messages.Add($"[info] {title}: {message}");

    public string? PickExecutable() => null;

    public void OpenFolder(string path)
    {
    }
}
