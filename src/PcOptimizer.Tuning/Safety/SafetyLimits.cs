using PcOptimizer.Benchmark.Model;
using PcOptimizer.Core.Model;

namespace PcOptimizer.Tuning.Safety;

/// <summary>
/// Limiti fissi, non modificabili dall'interfaccia. Un test di stabilità si interrompe appena uno è superato;
/// le guide non propongono mai valori oltre i limiti di passo e di ampiezza.
/// </summary>
public static class SafetyLimits
{
    /// <summary>CPU: interruzione immediata (Intel TjMax 100 °C; con un margine per le letture lente).</summary>
    public const double CpuTempHardC = 100;

    /// <summary>CPU: interruzione se si resta a questa temperatura per più di <see cref="SustainedSeconds"/> secondi.</summary>
    public const double CpuTempSustainedC = 95;

    public const int SustainedSeconds = 10;

    public const double GpuTempC = 90;

    public const double GpuHotspotC = 105;

    /// <summary>Errori hardware corretti (WHEA) durante un test: con undervolt e overclock sono il primo segnale di instabilità.</summary>
    public const int MaxWheaErrors = 0;

    /// <summary>Verifica una serie di letture: restituisce il motivo dell'interruzione, o null se tutto è nei limiti.</summary>
    public static string? Check(IReadOnlyList<SensorSample> samples)
    {
        if (samples.Count == 0)
        {
            return null;
        }

        var last = samples[^1];
        if (last.CpuTempC >= CpuTempHardC)
        {
            return $"Temperatura della CPU {last.CpuTempC:0} °C (limite {CpuTempHardC:0} °C).";
        }

        var recent = samples.Skip(Math.Max(0, samples.Count - SustainedSeconds)).ToList();
        if (recent.Count >= SustainedSeconds && recent.All(s => s.CpuTempC >= CpuTempSustainedC))
        {
            return $"CPU sopra {CpuTempSustainedC:0} °C per più di {SustainedSeconds} secondi: raffreddamento insufficiente per questo carico.";
        }

        if (last.GpuTempC >= GpuTempC)
        {
            return $"Temperatura della GPU {last.GpuTempC:0} °C (limite {GpuTempC:0} °C).";
        }

        if (last.GpuHotspotC >= GpuHotspotC)
        {
            return $"Hotspot della GPU {last.GpuHotspotC:0} °C (limite {GpuHotspotC:0} °C).";
        }

        return null;
    }
}

/// <summary>Passo e ampiezza massima di una procedura guidata.</summary>
/// <param name="Start">Primo valore da provare.</param>
/// <param name="Step">Variazione a ogni passo (con segno).</param>
/// <param name="Limit">Valore oltre il quale la guida non va (stesso segno del passo).</param>
public sealed record StepLimits(double Start, double Step, double Limit, string Unit, Risk Risk);
