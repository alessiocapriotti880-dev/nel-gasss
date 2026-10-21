using PcOptimizer.Tuning.Guides;

namespace PcOptimizer.Tuning.Stability;

/// <summary>
/// Test della RAM in Windows: occupa una parte della memoria libera con blocchi da 64 MB, scrive schemi diversi
/// (indirizzo nell'indirizzo, bit che scorrono, valori pseudocasuali, inversi) e li rilegge. Trova gli errori più
/// evidenti di un profilo XMP/EXPO instabile; non sostituisce test dedicati che girano fuori da Windows (MemTest86)
/// o più lunghi (TestMem5, Karhu).
/// </summary>
public sealed class MemoryTest(long bytes, int? threads = null) : IStressWorkload
{
    public const int BlockBytes = 64 * 1024 * 1024;
    private const int Patterns = 4;

    public StabilityTestKind Kind => StabilityTestKind.Memory;

    public long Bytes { get; } = Math.Max(BlockBytes, bytes / BlockBytes * BlockBytes);

    public async Task<(long WorkUnits, long Errors, IReadOnlyList<string> Notes)> RunAsync(CancellationToken cancellationToken)
    {
        var blocks = (int)(Bytes / BlockBytes);
        var data = new ulong[blocks][];
        for (var i = 0; i < blocks; i++)
        {
            data[i] = GC.AllocateUninitializedArray<ulong>(BlockBytes / sizeof(ulong));
        }

        var workers = Math.Max(1, Math.Min(threads ?? Environment.ProcessorCount, blocks));
        long passes = 0, errors = 0;
        var notes = new List<string>();
        try
        {
            for (var pass = 0; !cancellationToken.IsCancellationRequested; pass++)
            {
                var pattern = pass % Patterns;
                var seed = (ulong)pass * 0x9E3779B97F4A7C15UL;
                // Prima si scrive tutto, poi si verifica: tra scrittura e lettura i dati restano in RAM, non nelle cache.
                await Parallel.ForAsync(0, blocks, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = cancellationToken }, (b, _) =>
                {
                    Fill(data[b], b, pattern, seed);
                    return ValueTask.CompletedTask;
                });
                await Parallel.ForAsync(0, blocks, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = cancellationToken }, (b, _) =>
                {
                    var bad = Verify(data[b], b, pattern, seed);
                    if (bad > 0)
                    {
                        Interlocked.Add(ref errors, bad);
                        lock (notes)
                        {
                            if (notes.Count < 20)
                            {
                                notes.Add($"{bad} valori errati nel blocco {b} (schema {PatternName(pattern)}, passaggio {pass + 1}).");
                            }
                        }
                    }

                    return ValueTask.CompletedTask;
                });
                passes++;
            }
        }
        catch (OperationCanceledException)
        {
        }

        return (passes, errors, notes);
    }

    internal static ulong Expected(int pattern, int block, long index, ulong seed) => pattern switch
    {
        0 => ((ulong)block << 40) ^ (ulong)index ^ seed,              // indirizzo nell'indirizzo
        1 => 1UL << (int)((index + block) & 63),                       // bit che scorre
        2 => SplitMix((ulong)block << 32 ^ (ulong)index ^ seed),       // pseudocasuale
        _ => ~(((ulong)block << 40) ^ (ulong)index ^ seed),            // inverso del primo
    };

    internal static void Fill(ulong[] block, int blockIndex, int pattern, ulong seed)
    {
        for (long i = 0; i < block.Length; i++)
        {
            block[i] = Expected(pattern, blockIndex, i, seed);
        }
    }

    internal static long Verify(ulong[] block, int blockIndex, int pattern, ulong seed)
    {
        long bad = 0;
        for (long i = 0; i < block.Length; i++)
        {
            if (block[i] != Expected(pattern, blockIndex, i, seed))
            {
                bad++;
            }
        }

        return bad;
    }

    private static ulong SplitMix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static string PatternName(int pattern) => pattern switch
    {
        0 => "indirizzo",
        1 => "bit che scorre",
        2 => "pseudocasuale",
        _ => "inverso",
    };
}
