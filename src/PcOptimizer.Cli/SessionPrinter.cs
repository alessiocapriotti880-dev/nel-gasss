using PcOptimizer.Core.Journal;
using PcOptimizer.Optimization.Judging;
using PcOptimizer.Optimization.Planning;
using PcOptimizer.Optimization.Session;
using static PcOptimizer.Cli.ConsoleOutput;

namespace PcOptimizer.Cli;

/// <summary>Stampa di piano, sessione e giornale delle modifiche.</summary>
internal static class SessionPrinter
{
    public static void PrintPlan(OptimizationPlan plan)
    {
        Heading($"Interventi scelti ({plan.Immediate.Count + plan.AfterReboot.Count})");
        foreach (var t in plan.Immediate)
        {
            Console.WriteLine($"  - {t.Title} ({PlanPrinter.RiskName(t.Risk)}, {(t.IsMeasurable ? "verificato con la misura" : "non misurabile")})");
        }

        foreach (var t in plan.AfterReboot)
        {
            Console.WriteLine($"  - {t.Title} ({PlanPrinter.RiskName(t.Risk)}, dopo il riavvio)");
        }

        foreach (var t in plan.All.Where(t => t.AffectsSecurity))
        {
            Console.WriteLine($"\n  ATTENZIONE - {t.Title}: {t.SecurityWarning}");
        }

        foreach (var problem in plan.Problems)
        {
            Console.WriteLine($"  Problema: {problem}");
        }
    }

    public static void PrintSession(OptimizationSession s)
    {
        Heading($"Sessione {s.Id.ToString("N")[..8]} del {s.StartedAt:dd/MM/yyyy HH:mm} - {PhaseName(s.Phase)}");
        if (s.RestorePoint is { } rp)
        {
            Line("Ripristino", rp.Message);
        }

        if (s.AbortReason is not null)
        {
            Line("Interrotta", s.AbortReason);
        }

        foreach (var r in s.Results)
        {
            var outcome = r.Error is not null && !r.Applied ? "ERRORE"
                : r.Reverted ? "ANNULLATO"
                : r.Decision == Decision.Keep ? "TENUTO"
                : r.Decision is null && r.AfterReboot ? "DOPO RIAVVIO"
                : "APPLICATO";
            Console.WriteLine($"  {outcome,-13} {r.Title}");
            if (r.Explanation is not null)
            {
                Console.WriteLine($"                {r.Explanation}");
            }

            if (r.Error is not null)
            {
                Console.WriteLine($"                {r.Error}");
            }
        }

        if (s.Phase == SessionPhase.AwaitingReboot)
        {
            Console.WriteLine("\n  Riavvia il PC: dopo l'accesso la sessione riprende da sola (oppure esegui \"pcopt session resume\").");
        }

        if (s.RevertNeedsReboot)
        {
            Console.WriteLine("\n  Riavvia ancora una volta perché gli annullamenti abbiano effetto.");
        }
    }

    public static void PrintHistory(IReadOnlyList<JournalTransaction> transactions)
    {
        Heading(transactions.Count == 0 ? "Nessuna modifica registrata" : $"Giornale delle modifiche ({transactions.Count})");
        foreach (var t in transactions.OrderByDescending(t => t.Sequence))
        {
            var state = t.State switch
            {
                TransactionState.Active => "attivo",
                TransactionState.Reverted => "annullato",
                TransactionState.Aborted => "non applicato",
                _ => "interrotto",
            };
            Console.WriteLine($"  {t.StartedAt:dd/MM/yyyy HH:mm}  {state,-13} {t.Title}  [{t.TweakId}]");
            foreach (var setting in t.Settings)
            {
                Console.WriteLine($"        {setting.Target.Describe()}: {setting.Original} → {setting.Desired}");
            }
        }
    }

    private static string PhaseName(SessionPhase p) => p switch
    {
        SessionPhase.Started => "in corso",
        SessionPhase.AwaitingReboot => "in attesa del riavvio",
        SessionPhase.Completed => "completata",
        _ => "interrotta senza modifiche",
    };
}
