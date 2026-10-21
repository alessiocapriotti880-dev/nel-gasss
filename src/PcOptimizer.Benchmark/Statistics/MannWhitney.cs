namespace PcOptimizer.Benchmark.Statistics;

/// <summary>Risultato del test di Mann-Whitney (somma dei ranghi di Wilcoxon) a due code.</summary>
/// <param name="U">Statistica U del primo campione.</param>
/// <param name="PValue">Probabilità di osservare una differenza almeno così grande se le distribuzioni fossero uguali.</param>
/// <param name="Exact">true se il p-value è esatto (campioni piccoli senza pari merito), false se da approssimazione normale.</param>
public sealed record MannWhitneyResult(double U, double PValue, bool Exact);

/// <summary>
/// Test non parametrico per due campioni indipendenti: non presuppone distribuzioni normali, adatto alle
/// latenze (code lunghe, valori anomali). Per campioni piccoli senza pari merito usa la distribuzione esatta,
/// altrimenti l'approssimazione normale con correzione per i pari merito e di continuità.
/// </summary>
public static class MannWhitney
{
    private const int ExactLimit = 30;

    public static MannWhitneyResult Test(IReadOnlyList<double> first, IReadOnlyList<double> second)
    {
        var n1 = first.Count;
        var n2 = second.Count;
        if (n1 == 0 || n2 == 0)
        {
            return new MannWhitneyResult(double.NaN, 1, false);
        }

        var combined = first.Select(v => (Value: v, Group: 0))
            .Concat(second.Select(v => (Value: v, Group: 1)))
            .OrderBy(x => x.Value)
            .ToArray();

        // Ranghi medi per i pari merito; tieTerm = somma(t^3 - t) per la correzione della varianza.
        var ranks = new double[combined.Length];
        double tieTerm = 0;
        for (var i = 0; i < combined.Length;)
        {
            var j = i;
            while (j + 1 < combined.Length && combined[j + 1].Value == combined[i].Value)
            {
                j++;
            }

            var rank = (i + j) / 2.0 + 1;
            for (var k = i; k <= j; k++)
            {
                ranks[k] = rank;
            }

            var t = j - i + 1;
            tieTerm += (double)t * t * t - t;
            i = j + 1;
        }

        double rankSum1 = 0;
        for (var i = 0; i < combined.Length; i++)
        {
            if (combined[i].Group == 0)
            {
                rankSum1 += ranks[i];
            }
        }

        var u1 = rankSum1 - n1 * (n1 + 1) / 2.0;
        if (tieTerm == 0 && n1 + n2 <= ExactLimit)
        {
            return new MannWhitneyResult(u1, ExactPValue(n1, n2, u1), true);
        }

        var n = (double)n1 + n2;
        var mean = n1 * (double)n2 / 2;
        var variance = n1 * (double)n2 / 12 * (n + 1 - tieTerm / (n * (n - 1)));
        if (variance <= 0)
        {
            return new MannWhitneyResult(u1, 1, false);
        }

        var z = (Math.Abs(u1 - mean) - 0.5) / Math.Sqrt(variance);
        var p = 2 * NormalUpperTail(Math.Max(z, 0));
        return new MannWhitneyResult(u1, Math.Min(1, p), false);
    }

    /// <summary>p-value esatto a due code: conta le disposizioni dei ranghi con U uguale o più estremo.</summary>
    internal static double ExactPValue(int n1, int n2, double u)
    {
        var counts = UDistribution(n1, n2);
        double total = 0;
        foreach (var c in counts)
        {
            total += c;
        }

        var uInt = (int)Math.Round(u);
        double lower = 0;
        double upper = 0;
        for (var k = 0; k < counts.Length; k++)
        {
            if (k <= uInt)
            {
                lower += counts[k];
            }

            if (k >= uInt)
            {
                upper += counts[k];
            }
        }

        return Math.Min(1, 2 * Math.Min(lower, upper) / total);
    }

    /// <summary>Frequenze di U per n1, n2 (ricorrenza di Mann-Whitney: f(n1,n2,u) = f(n1-1,n2,u-n2) + f(n1,n2-1,u)).</summary>
    private static double[] UDistribution(int n1, int n2)
    {
        // table[a][b] = distribuzione per a elementi del primo gruppo e b del secondo.
        var table = new double[n1 + 1, n2 + 1][];
        for (var a = 0; a <= n1; a++)
        {
            for (var b = 0; b <= n2; b++)
            {
                var dist = new double[a * b + 1];
                if (a == 0 || b == 0)
                {
                    dist[0] = 1;
                }
                else
                {
                    var withoutA = table[a - 1, b];
                    var withoutB = table[a, b - 1];
                    for (var u = 0; u < dist.Length; u++)
                    {
                        var fromA = u - b >= 0 && u - b < withoutA.Length ? withoutA[u - b] : 0;
                        var fromB = u < withoutB.Length ? withoutB[u] : 0;
                        dist[u] = fromA + fromB;
                    }
                }

                table[a, b] = dist;
            }
        }

        return table[n1, n2];
    }

    /// <summary>P(Z &gt; z) per la normale standard.</summary>
    public static double NormalUpperTail(double z) => 0.5 * Erfc(z / Math.Sqrt(2));

    /// <summary>Funzione d'errore complementare (Numerical Recipes, errore relativo &lt; 1.2e-7).</summary>
    internal static double Erfc(double x)
    {
        var z = Math.Abs(x);
        var t = 1 / (1 + 0.5 * z);
        var r = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 +
                t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 +
                t * (-0.82215223 + t * 0.17087277)))))))));
        return x >= 0 ? r : 2 - r;
    }
}
