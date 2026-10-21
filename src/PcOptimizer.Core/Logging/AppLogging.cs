using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace PcOptimizer.Core.Logging;

/// <summary>
/// Configura il log su file: un file al giorno in <see cref="AppPaths.Logs"/>, conservati 30 giorni.
/// Ogni modifica al sistema viene loggata con valore precedente e nuovo (vedi <c>TweakEngine</c>);
/// il giornale delle modifiche resta comunque la fonte autorevole per il rollback.
/// </summary>
public static class AppLogging
{
    public static ILoggerFactory CreateFactory(AppPaths paths, bool verbose = false)
    {
        Directory.CreateDirectory(paths.Logs);
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Information)
            .Enrich.WithProperty("App", "PcOptimizerPro")
            .WriteTo.File(
                Path.Combine(paths.Logs, "pcoptimizer-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        return LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: true));
    }
}
