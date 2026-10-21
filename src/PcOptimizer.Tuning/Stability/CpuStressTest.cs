using System.Numerics;
using System.Security.Cryptography;
using PcOptimizer.Tuning.Guides;

namespace PcOptimizer.Tuning.Stability;

/// <summary>
/// Test di stabilità della CPU: su tutti i processori logici esegue calcoli deterministici (interi, virgola mobile
/// vettoriale AVX/SSE, SHA-256) e confronta ogni risultato con quello calcolato all'inizio. Con undervolt o
/// overclock instabili i core sbagliano i calcoli prima di bloccarsi: ogni differenza è un errore.
/// </summary>
public sealed class CpuStressTest(int? threads = null, int rounds = 20_000) : IStressWorkload
{
    private const int Seeds = 16;
    private static readonly byte[] HashInput = Enumerable.Range(0, 64 * 1024).Select(i => (byte)(i * 31 + 7)).ToArray();

    public StabilityTestKind Kind => StabilityTestKind.Cpu;

    public int Threads { get; } = threads ?? Environment.ProcessorCount;

    public async Task<(long WorkUnits, long Errors, IReadOnlyList<string> Notes)> RunAsync(CancellationToken cancellationToken)
    {
        // Riferimenti calcolati una volta (prima che il carico scaldi la CPU).
        var reference = Enumerable.Range(0, Seeds).Select(s => Workload((ulong)s + 1, rounds)).ToArray();
        var expectedHash = SHA256.HashData(HashInput);
        long units = 0, errors = 0;
        var notes = new List<string>();

        var workers = Enumerable.Range(0, Threads).Select(t => Task.Factory.StartNew(() =>
        {
            var k = t;
            while (!cancellationToken.IsCancellationRequested)
            {
                var seed = k % Seeds;
                var ok = Workload((ulong)seed + 1, rounds) == reference[seed];
                if (k % 8 == 0)
                {
                    ok &= SHA256.HashData(HashInput).AsSpan().SequenceEqual(expectedHash);
                }

                Interlocked.Increment(ref units);
                if (!ok)
                {
                    Interlocked.Increment(ref errors);
                    lock (notes)
                    {
                        if (notes.Count < 20)
                        {
                            notes.Add($"Risultato errato sul thread {t} (unità {k}).");
                        }
                    }
                }

                k += Threads;
            }
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException)
        {
        }

        return (units, errors, notes);
    }

    /// <summary>
    /// Calcolo deterministico misto: interi (xorshift/moltiplicazioni), virgola mobile vettoriale (moltiplica-somma su
    /// <see cref="Vector{T}"/>, che usa AVX2/AVX-512 se disponibili) e divisioni. Stesso input, stesso risultato bit per bit.
    /// </summary>
    internal static ulong Workload(ulong seed, int rounds)
    {
        var x = seed * 0x9E3779B97F4A7C15UL;
        var a = new Vector<double>(1.0 + seed * 1e-3);
        var b = new Vector<double>(0.999999);
        var c = new Vector<double>(1e-6);
        double div = 1.0 + seed;
        for (var i = 0; i < rounds; i++)
        {
            x ^= x << 13;
            x ^= x >> 7;
            x ^= x << 17;
            x *= 0xD6E8FEB86659FD93UL;
            a = a * b + c;
            if ((i & 15) == 0)
            {
                div = div / 1.0000013 + 0.5;
            }
        }

        var bits = BitConverter.DoubleToInt64Bits(Vector.Dot(a, Vector<double>.One)) ^ BitConverter.DoubleToInt64Bits(div);
        return x ^ (ulong)bits;
    }
}
