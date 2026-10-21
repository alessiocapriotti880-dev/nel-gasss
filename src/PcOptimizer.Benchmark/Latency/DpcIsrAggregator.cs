using PcOptimizer.Benchmark.Catalog;
using PcOptimizer.Benchmark.Model;
using PcOptimizer.Benchmark.Statistics;

namespace PcOptimizer.Benchmark.Latency;

/// <summary>
/// Accumula gli eventi DPC/ISR del kernel in statistiche per secondo e per driver.
/// Pura (nessuna dipendenza da ETW) così si prova con eventi sintetici; gli indirizzi delle routine si
/// risolvono alla fine, perché gli eventi di caricamento dei moduli possono arrivare dopo i primi DPC.
/// </summary>
internal sealed class DpcIsrAggregator
{
    /// <summary>Durate oltre 1 s sono letture corrotte (eventi spezzati o persi), non latenze reali.</summary>
    private const double MaxPlausibleUs = 1_000_000;
    private const double HighLatencyUs = 500;

    private readonly double _startMs;
    private readonly double _windowMs;
    private readonly int _logicalProcessors;
    private readonly Reservoir _dpcSample = new(50_000, seed: 1);
    private readonly Reservoir _isrSample = new(50_000, seed: 2);
    private readonly List<double> _windowMaxDpc = [];
    private readonly List<double> _windowMaxIsr = [];
    private readonly List<double> _windowBusyUs = [];
    private readonly Dictionary<ulong, RoutineStats> _routines = [];
    private double _maxDpc;
    private double _maxIsr;
    private int _lost;

    /// <param name="startMs">Gli eventi precedenti sono ignorati (avvio della sessione ETW, che genera un picco di eventi).</param>
    public DpcIsrAggregator(double startMs, int logicalProcessors, double windowSeconds = 1)
    {
        _startMs = startMs;
        _windowMs = windowSeconds * 1000;
        _logicalProcessors = Math.Max(1, logicalProcessors);
    }

    public KernelModuleMap Modules { get; } = new();

    public void AddDpc(double timeMs, double durationUs, ulong routine) => Add(timeMs, durationUs, routine, isr: false);

    public void AddIsr(double timeMs, double durationUs, ulong routine) => Add(timeMs, durationUs, routine, isr: true);

    public void AddLost(int count) => _lost += count;

    private void Add(double timeMs, double durationUs, ulong routine, bool isr)
    {
        if (timeMs < _startMs || durationUs < 0 || durationUs > MaxPlausibleUs || double.IsNaN(durationUs))
        {
            return;
        }

        var window = (int)((timeMs - _startMs) / _windowMs);
        while (_windowMaxDpc.Count <= window)
        {
            _windowMaxDpc.Add(0);
            _windowMaxIsr.Add(0);
            _windowBusyUs.Add(0);
        }

        _windowBusyUs[window] += durationUs;
        if (isr)
        {
            _isrSample.Add(durationUs);
            _maxIsr = Math.Max(_maxIsr, durationUs);
            _windowMaxIsr[window] = Math.Max(_windowMaxIsr[window], durationUs);
        }
        else
        {
            _dpcSample.Add(durationUs);
            _maxDpc = Math.Max(_maxDpc, durationUs);
            _windowMaxDpc[window] = Math.Max(_windowMaxDpc[window], durationUs);
        }

        if (!_routines.TryGetValue(routine, out var stats))
        {
            stats = new RoutineStats();
            _routines[routine] = stats;
        }

        stats.Add(durationUs, isr);
    }

    /// <param name="durationSeconds">Durata effettiva della misura (dopo l'avvio): fissa il numero di finestre.</param>
    public DpcIsrResult Build(double durationSeconds, KnownDriverCatalog? drivers = null)
    {
        var windows = Math.Max(1, (int)Math.Floor(durationSeconds * 1000 / _windowMs));
        var capacityUs = _windowMs * 1000 * _logicalProcessors;

        var perModule = new Dictionary<string, RoutineStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var (routine, stats) in _routines)
        {
            var name = Modules.Resolve(routine) ?? "sconosciuto";
            if (!perModule.TryGetValue(name, out var total))
            {
                total = new RoutineStats();
                perModule[name] = total;
            }

            total.Merge(stats);
        }

        return new DpcIsrResult
        {
            DurationSeconds = durationSeconds,
            DpcCount = _dpcSample.Seen,
            IsrCount = _isrSample.Seen,
            MaxDpcUs = _maxDpc,
            MaxIsrUs = _maxIsr,
            Dpc = Descriptive.Summarize(_dpcSample.Items),
            Isr = Descriptive.Summarize(_isrSample.Items),
            WindowMaxDpcUs = Fit(_windowMaxDpc, windows),
            WindowMaxIsrUs = Fit(_windowMaxIsr, windows),
            WindowLoadPercent = Fit(_windowBusyUs, windows).Select(us => us / capacityUs * 100).ToArray(),
            Drivers = perModule
                .Select(kv => new DriverLatency(
                    kv.Key, drivers?.Find(kv.Key)?.Name, kv.Value.DpcCount, kv.Value.IsrCount,
                    kv.Value.MaxDpcUs, kv.Value.MaxIsrUs, kv.Value.TotalUs / 1000, kv.Value.Over500))
                .OrderByDescending(d => Math.Max(d.MaxDpcUs, d.MaxIsrUs))
                .ToList(),
            LostEvents = _lost,
        };
    }

    /// <summary>Porta la serie a esattamente <paramref name="count"/> finestre: i secondi senza eventi valgono 0.</summary>
    private static double[] Fit(List<double> values, int count)
    {
        var result = new double[count];
        for (var i = 0; i < Math.Min(count, values.Count); i++)
        {
            result[i] = values[i];
        }

        return result;
    }

    private sealed class RoutineStats
    {
        public long DpcCount;
        public long IsrCount;
        public double MaxDpcUs;
        public double MaxIsrUs;
        public double TotalUs;
        public long Over500;

        public void Add(double us, bool isr)
        {
            if (isr)
            {
                IsrCount++;
                MaxIsrUs = Math.Max(MaxIsrUs, us);
            }
            else
            {
                DpcCount++;
                MaxDpcUs = Math.Max(MaxDpcUs, us);
            }

            TotalUs += us;
            if (us > HighLatencyUs)
            {
                Over500++;
            }
        }

        public void Merge(RoutineStats other)
        {
            DpcCount += other.DpcCount;
            IsrCount += other.IsrCount;
            MaxDpcUs = Math.Max(MaxDpcUs, other.MaxDpcUs);
            MaxIsrUs = Math.Max(MaxIsrUs, other.MaxIsrUs);
            TotalUs += other.TotalUs;
            Over500 += other.Over500;
        }
    }
}
