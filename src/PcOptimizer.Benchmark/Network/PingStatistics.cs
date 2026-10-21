using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Statistics;

namespace PcOptimizer.Benchmark.Network;

/// <summary>Statistiche di una serie di ping: perdita, tempi e jitter.</summary>
public static class PingStatistics
{
    /// <param name="rttMs">Un valore per ogni ping inviato, null se perso.</param>
    public static PingTargetResult Compute(string target, string kind, IReadOnlyList<double?> rttMs)
    {
        var received = rttMs.Where(r => r.HasValue).Select(r => r!.Value).ToArray();
        // Jitter come media delle differenze assolute fra risposte consecutive (variazione del ritardo, RFC 3550/4689).
        var jitter = new double[Math.Max(0, received.Length - 1)];
        for (var i = 1; i < received.Length; i++)
        {
            jitter[i - 1] = Math.Abs(received[i] - received[i - 1]);
        }

        return new PingTargetResult
        {
            Target = target,
            Kind = kind,
            Sent = rttMs.Count,
            Received = received.Length,
            LossPercent = rttMs.Count == 0 ? 0 : 100.0 * (rttMs.Count - received.Length) / rttMs.Count,
            RttMs = Descriptive.Summarize(received),
            JitterMs = jitter.Length > 0 ? jitter.Average() : null,
            RttSamplesMs = received,
            JitterSamplesMs = jitter,
        };
    }
}
