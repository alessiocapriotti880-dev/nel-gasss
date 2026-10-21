using PcOptimizer.Analysis.Model;

namespace PcOptimizer.Analysis;

/// <summary>Raccolta della configurazione del sistema. L'implementazione reale è <c>Windows.WindowsSystemCollector</c>.</summary>
public interface ISystemCollector
{
    Task<SystemSnapshot> CollectAsync(CancellationToken cancellationToken = default);
}
