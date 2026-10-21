using PcOptimizer.Core.Settings;

namespace PcOptimizer.Core.Tests.Fakes;

/// <summary>Sistema finto in memoria: gestisce qualsiasi tipo di impostazione.</summary>
internal sealed class InMemorySettingProvider : ISettingProvider
{
    public Dictionary<SettingTarget, SettingValue> Values { get; } = [];

    /// <summary>Target su cui la scrittura fallisce (simula un "accesso negato").</summary>
    public HashSet<SettingTarget> FailOnWrite { get; } = [];

    public List<(SettingTarget Target, SettingValue Value)> Writes { get; } = [];

    public bool CanHandle(SettingTarget target) => true;

    public SettingValue Read(SettingTarget target) =>
        Values.TryGetValue(target, out var v) ? v : SettingValue.Absent;

    public void Write(SettingTarget target, SettingValue value)
    {
        if (FailOnWrite.Contains(target))
        {
            throw new UnauthorizedAccessException("Accesso negato (simulato).");
        }

        Writes.Add((target, value));
        if (value.Exists)
        {
            Values[target] = value;
        }
        else
        {
            Values.Remove(target);
        }
    }
}
