namespace PcOptimizer.Benchmark.Latency;

/// <summary>Associa un indirizzo del kernel (routine DPC/ISR) al driver che lo contiene.</summary>
internal sealed class KernelModuleMap
{
    private readonly List<(ulong Start, ulong End, string Name)> _modules = [];
    private bool _sorted = true;

    public int Count => _modules.Count;

    public void Add(ulong imageBase, ulong size, string path)
    {
        if (imageBase == 0 || size == 0)
        {
            return;
        }

        _modules.Add((imageBase, imageBase + size, FileName(path)));
        _sorted = false;
    }

    public string? Resolve(ulong address)
    {
        if (!_sorted)
        {
            _modules.Sort((a, b) => a.Start.CompareTo(b.Start));
            _sorted = true;
        }

        int lo = 0, hi = _modules.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var m = _modules[mid];
            if (address < m.Start)
            {
                hi = mid - 1;
            }
            else if (address >= m.End)
            {
                lo = mid + 1;
            }
            else
            {
                return m.Name;
            }
        }

        return null;
    }

    /// <summary>Nome del file da un percorso del kernel (\SystemRoot\system32\drivers\ndis.sys).</summary>
    internal static string FileName(string path)
    {
        var trimmed = path.Trim().TrimEnd('\0');
        var i = trimmed.LastIndexOfAny(['\\', '/']);
        return i >= 0 ? trimmed[(i + 1)..] : trimmed;
    }
}
