namespace PcOptimizer.Benchmark.Statistics;

/// <summary>
/// Campionamento a serbatoio (algoritmo R): tiene un campione uniforme di dimensione fissa da un flusso
/// di lunghezza ignota. Serve per i percentili su centinaia di migliaia di eventi DPC senza esaurire la
/// memoria; il massimo assoluto va tenuto a parte perché il campione può non contenerlo.
/// </summary>
internal sealed class Reservoir
{
    private readonly double[] _items;
    private readonly Random _random;
    private long _seen;

    public Reservoir(int capacity, int seed = 12345)
    {
        _items = new double[capacity];
        _random = new Random(seed);
    }

    public long Seen => _seen;

    public void Add(double value)
    {
        if (_seen < _items.Length)
        {
            _items[_seen] = value;
        }
        else
        {
            var j = _random.NextInt64(_seen + 1);
            if (j < _items.Length)
            {
                _items[j] = value;
            }
        }

        _seen++;
    }

    public IReadOnlyList<double> Items => new ArraySegment<double>(_items, 0, (int)Math.Min(_seen, _items.Length));
}
