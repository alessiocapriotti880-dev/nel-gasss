using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Optimization.Planning;

/// <summary>
/// Interventi scelti dall'utente, divisi in quelli che hanno effetto subito (misurati uno alla volta) e quelli che
/// richiedono il riavvio (applicati insieme e misurati dopo il riavvio).
/// </summary>
public sealed record OptimizationPlan(
    IReadOnlyList<CatalogTweak> Immediate,
    IReadOnlyList<CatalogTweak> AfterReboot,
    IReadOnlyList<string> Problems)
{
    public bool IsValid => Problems.Count == 0 && Immediate.Count + AfterReboot.Count > 0;

    public bool RequiresReboot => AfterReboot.Count > 0;

    public IEnumerable<CatalogTweak> All => Immediate.Concat(AfterReboot);
}

public static class PlanBuilder
{
    /// <param name="confirmedSecurity">Interventi che riducono la sicurezza confermati esplicitamente dall'utente.</param>
    public static OptimizationPlan Build(
        IReadOnlyList<CatalogTweak> catalog, IEnumerable<string> selectedIds, TweakContext context, IEnumerable<string>? confirmedSecurity = null)
    {
        var problems = new List<string>();
        var confirmed = new HashSet<string>(confirmedSecurity ?? [], StringComparer.OrdinalIgnoreCase);
        var byId = catalog.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var selected = new List<CatalogTweak>();

        foreach (var id in selectedIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!byId.TryGetValue(id, out var tweak))
            {
                problems.Add($"Intervento sconosciuto: {id}.");
                continue;
            }

            if (!tweak.IsApplicable(context, out var reason))
            {
                problems.Add($"\"{tweak.Title}\" non è applicabile: {reason}");
                continue;
            }

            if (tweak.AffectsSecurity && !confirmed.Contains(tweak.Id))
            {
                problems.Add($"\"{tweak.Title}\" riduce una protezione di sicurezza e richiede una conferma esplicita. {tweak.SecurityWarning}");
                continue;
            }

            selected.Add(tweak);
        }

        problems.AddRange(Conflicts(selected, context));
        var ordered = selected.OrderBy(t => t.Order).ThenBy(t => catalog.ToList().IndexOf(t)).ToList();
        return new OptimizationPlan(
            ordered.Where(t => !t.RequiresReboot).ToList(),
            ordered.Where(t => t.RequiresReboot).ToList(),
            problems);
    }

    /// <summary>Due interventi che vogliono valori diversi per la stessa impostazione non possono essere scelti insieme.</summary>
    private static IEnumerable<string> Conflicts(IReadOnlyList<CatalogTweak> selected, TweakContext context)
    {
        var owners = new Dictionary<SettingTarget, (CatalogTweak Tweak, SettingValue Desired)>();
        foreach (var tweak in selected)
        {
            foreach (var change in tweak.PlanChanges(context))
            {
                if (owners.TryGetValue(change.Target, out var other) && other.Desired != change.Desired && other.Tweak != tweak)
                {
                    yield return $"\"{tweak.Title}\" e \"{other.Tweak.Title}\" impostano in modo diverso {change.Target.Describe()}: scegline uno.";
                }
                else
                {
                    owners[change.Target] = (tweak, change.Desired);
                }
            }
        }
    }
}
