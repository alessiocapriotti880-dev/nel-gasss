using PcOptimizer.Core.Model;

namespace PcOptimizer.Core.Tweaks;

/// <summary>
/// Informazioni sul sistema a disposizione degli interventi per decidere se e come applicarsi.
/// I moduli successivi (Hardware, Analisi) aggiungono i propri dati come "facet" tipizzati,
/// senza che il Core debba conoscerli.
/// </summary>
public sealed class TweakContext
{
    private readonly Dictionary<Type, object> _facets = [];

    public TweakContext(HardwareTier tier, int windowsBuild)
    {
        Tier = tier;
        WindowsBuild = windowsBuild;
    }

    public HardwareTier Tier { get; }

    /// <summary>Numero di build di Windows (es. 19045 = Win10 22H2, 22000+ = Windows 11).</summary>
    public int WindowsBuild { get; }

    public bool IsWindows11 => WindowsBuild >= 22000;

    public TweakContext With<T>(T facet) where T : class
    {
        _facets[typeof(T)] = facet;
        return this;
    }

    public T? Get<T>() where T : class => _facets.TryGetValue(typeof(T), out var value) ? (T)value : null;

    public T Require<T>() where T : class =>
        Get<T>() ?? throw new InvalidOperationException($"Dato di contesto mancante: {typeof(T).Name}.");
}
