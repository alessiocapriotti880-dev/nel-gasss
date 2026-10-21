using System.Globalization;

namespace PcOptimizer.Hardware.Assessment;

/// <summary>Funzioni di punteggio (0-100) e formattazione dei numeri per i testi in italiano.</summary>
internal static class ScoreMath
{
    /// <summary>Scala lineare: <paramref name="min"/> → 0, <paramref name="max"/> → 100.</summary>
    public static double Linear(double value, double min, double max) =>
        Math.Clamp((value - min) / (max - min), 0, 1) * 100;

    /// <summary>
    /// Scala logaritmica: <paramref name="low"/> → 0, <paramref name="high"/> → 100.
    /// Adatta alle GPU, le cui prestazioni variano di decine di volte tra i modelli.
    /// </summary>
    public static double Log(double value, double low, double high) =>
        value <= low ? 0 : Math.Clamp(Math.Log(value / low) / Math.Log(high / low), 0, 1) * 100;

    /// <summary>Interpolazione lineare a tratti su coppie [x, punti] ordinate per x.</summary>
    public static double Piecewise(double x, IReadOnlyList<double[]> points)
    {
        if (points.Count == 0)
        {
            return 0;
        }

        if (x <= points[0][0])
        {
            return points[0][1] * Math.Max(x, 0) / points[0][0];
        }

        for (var i = 1; i < points.Count; i++)
        {
            if (x <= points[i][0])
            {
                var (x0, y0, x1, y1) = (points[i - 1][0], points[i - 1][1], points[i][0], points[i][1]);
                return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
            }
        }

        return points[^1][1];
    }

    /// <summary>Numero con virgola decimale all'italiana, indipendente dalla cultura del sistema.</summary>
    public static string Fmt(double value, string format = "0.#") =>
        value.ToString(format, CultureInfo.InvariantCulture).Replace('.', ',');
}
