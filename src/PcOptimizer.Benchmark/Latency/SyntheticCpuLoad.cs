namespace PcOptimizer.Benchmark.Latency;

/// <summary>
/// Carico CPU controllato per misurare la latenza "sotto carico" in modo ripetibile: thread a priorità bassa che
/// eseguono calcoli in virgola mobile finché l'oggetto non viene rilasciato. Non tocca altri processi.
/// </summary>
public sealed class SyntheticCpuLoad : IDisposable
{
    private readonly List<Thread> _threads = [];
    private volatile bool _stop;

    public SyntheticCpuLoad(int threads)
    {
        for (var i = 0; i < Math.Max(1, threads); i++)
        {
            var thread = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = $"Carico sintetico {i}" };
            _threads.Add(thread);
            thread.Start(i);
        }
    }

    /// <summary>Metà dei processori logici: lascia spazio al sistema e alla misura.</summary>
    public static int DefaultThreads => Math.Max(1, Environment.ProcessorCount / 2);

    private void Work(object? seed)
    {
        var x = 1.0 + (int)seed! * 1e-3;
        while (!_stop)
        {
            for (var i = 0; i < 100_000; i++)
            {
                x = Math.Sqrt(x * 1.0000001 + 0.5);
            }
        }

        GC.KeepAlive(x);
    }

    public void Dispose()
    {
        _stop = true;
        foreach (var thread in _threads)
        {
            thread.Join();
        }
    }
}
