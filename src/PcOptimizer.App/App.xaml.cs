using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PcOptimizer.App.Services;
using PcOptimizer.Core;
using PcOptimizer.Core.Logging;
using PcOptimizer.Presentation.Services;
using PcOptimizer.Presentation.ViewModels;

namespace PcOptimizer.App;

/// <summary>
/// Avvio: log, servizi, view model e finestra principale. Argomenti:
/// <c>--resume</c> riprende la sessione dopo il riavvio (usato dall'attività pianificata);
/// <c>--smoke-test &lt;cartella&gt;</c> apre ogni pagina, salva un'immagine e gli errori di binding, poi esce (CI).
/// </summary>
public partial class App : Application
{
    private ILoggerFactory _loggers = NullLoggerFactory.Instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        var smokeIndex = Array.FindIndex(args, a => a.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase));
        var smokeFolder = smokeIndex >= 0 && smokeIndex + 1 < args.Length ? Path.GetFullPath(args[smokeIndex + 1]) : null;

        var paths = new AppPaths();
        try
        {
            _loggers = AppLogging.CreateFactory(paths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _loggers = NullLoggerFactory.Instance;
        }

        var log = _loggers.CreateLogger<App>();
        DispatcherUnhandledException += (_, ev) =>
        {
            log.LogError(ev.Exception, "Errore non gestito nell'interfaccia");
            if (smokeFolder is null)
            {
                MessageBox.Show(ev.Exception.Message, "PC Optimizer Pro - errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            ev.Handled = smokeFolder is null;
        };

        var exe = Environment.ProcessPath ?? "PcOptimizerPro.exe";
        var dataDirs = new[] { Path.Combine(AppContext.BaseDirectory, "data") };
        var services = new WindowsOptimizerServices(_loggers, paths, dataDirs, exe, "--resume");
        IDialogService dialogs = smokeFolder is null ? new WpfDialogService() : new SilentDialogService();
        var main = new MainViewModel(services, dialogs);
        var window = new MainWindow { DataContext = main };
        MainWindow = window;

        if (smokeFolder is not null)
        {
            await SmokeTestAsync(window, main, smokeFolder, (SilentDialogService)dialogs);
            return;
        }

        window.Show();
        await main.InitializeAsync(args.Contains("--resume", StringComparer.OrdinalIgnoreCase));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _loggers.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Prova dell'interfaccia senza interazione: analisi reale, poi ogni pagina viene mostrata, salvata come immagine e
    /// controllata per gli errori di binding (scritti in bindings.txt). Codice di uscita 1 se qualcosa non va.
    /// </summary>
    private async Task SmokeTestAsync(MainWindow window, MainViewModel main, string folder, SilentDialogService dialogs)
    {
        Directory.CreateDirectory(folder);
        var bindingErrors = new BindingErrorListener();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        var exitCode = 0;
        try
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Width = 1440;
            window.Height = 900;
            window.ShowActivated = false;
            window.Show();
            await main.InitializeAsync(resume: false);
            main.Benchmark.Refresh();

            foreach (var page in main.Pages)
            {
                main.SelectedPage = page;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                Save(window, Path.Combine(folder, $"{main.Pages.ToList().IndexOf(page) + 1}-{Slug(page.Title)}.jpg"));
            }

            // Espande i dettagli del primo intervento per provare anche quella parte della pagina.
            main.SelectedPage = main.Optimization;
            if (main.Optimization.AllItems.FirstOrDefault() is { } item)
            {
                item.IsExpanded = true;
                item.IsSelected = true;
            }

            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Save(window, Path.Combine(folder, "7-ottimizzazione-dettagli.jpg"));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString());
            exitCode = 1;
        }

        var report = new StringBuilder();
        report.AppendLine($"Errori di binding: {bindingErrors.Errors.Count}");
        foreach (var error in bindingErrors.Errors.Distinct())
        {
            report.AppendLine(error);
        }

        report.AppendLine($"Messaggi dell'interfaccia: {dialogs.Messages.Count}");
        foreach (var m in dialogs.Messages)
        {
            report.AppendLine(m);
        }

        report.AppendLine($"Stato: {main.State.Status}");
        report.AppendLine($"Interventi mostrati: {main.Optimization.AllItems.Count()}, gruppi: {main.Optimization.Groups.Count}");
        File.WriteAllText(Path.Combine(folder, "bindings.txt"), report.ToString());
        if (bindingErrors.Errors.Count > 0)
        {
            exitCode = 1;
        }

        Shutdown(exitCode);
    }

    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var width = (int)Math.Max(1, content.ActualWidth);
        var height = (int)Math.Max(1, content.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        // Ridotta per poterla allegare ai log della CI.
        var scaled = new TransformedBitmap(bitmap, new ScaleTransform(0.6, 0.6));
        var encoder = new JpegBitmapEncoder { QualityLevel = 70 };
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static string Slug(string text) =>
        string.Join('-', new string(text.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray())
            .Split('-', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Raccoglie gli errori di binding di WPF (normalmente visibili solo nel debugger).</summary>
    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = [];

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message is not null)
            {
                Errors.Add(message);
            }
        }
    }
}
