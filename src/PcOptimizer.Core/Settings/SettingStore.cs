namespace PcOptimizer.Core.Settings;

/// <summary>
/// Legge e scrive un tipo di impostazione. Le implementazioni Windows stanno in
/// <c>PcOptimizer.Core.Windows</c>; i test usano un'implementazione in memoria.
/// </summary>
public interface ISettingProvider
{
    bool CanHandle(SettingTarget target);

    SettingValue Read(SettingTarget target);

    /// <summary>Scrive il valore; con <see cref="SettingValue.Absent"/> lo rimuove (se il tipo lo consente).</summary>
    void Write(SettingTarget target, SettingValue value);
}

/// <summary>Instrada ogni lettura/scrittura al provider adatto al tipo di impostazione.</summary>
public sealed class SettingStore(IEnumerable<ISettingProvider> providers)
{
    private readonly IReadOnlyList<ISettingProvider> _providers = providers.ToList();

    public SettingValue Read(SettingTarget target) => ProviderFor(target).Read(target);

    public void Write(SettingTarget target, SettingValue value) => ProviderFor(target).Write(target, value);

    private ISettingProvider ProviderFor(SettingTarget target) =>
        _providers.FirstOrDefault(p => p.CanHandle(target))
        ?? throw new NotSupportedException($"Nessun provider per {target.GetType().Name}.");
}
