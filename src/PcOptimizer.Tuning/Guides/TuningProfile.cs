using System.Text.Json;
using System.Text.Json.Serialization;
using PcOptimizer.Core;
using PcOptimizer.Tuning.Stability;

namespace PcOptimizer.Tuning.Guides;

public enum StepOutcome
{
    /// <summary>Valore impostato, test non ancora concluso.</summary>
    Pending,
    Stable,
    /// <summary>Errori di calcolo, errori WHEA, artefatti o crash di un programma.</summary>
    Unstable,
    /// <summary>Il PC si è bloccato o riavviato durante il test (rilevato al riavvio successivo).</summary>
    Crashed,
    /// <summary>Test interrotto per temperatura: il valore non è giudicabile, il raffreddamento va migliorato.</summary>
    Aborted,
}

/// <summary>Un valore provato, con l'esito del test.</summary>
public sealed record TuningStep
{
    public double Value { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public StepOutcome Outcome { get; init; }

    public StabilityReport? Report { get; init; }

    /// <summary>Annotazione dell'utente (es. "crash in gioco dopo 20 minuti").</summary>
    public string? Note { get; init; }
}

/// <summary>Registro di una procedura guidata: valori provati ed esiti, salvato per riprendere dopo un riavvio.</summary>
public sealed record TuningProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string MethodId { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public IReadOnlyList<TuningStep> Steps { get; init; } = [];

    /// <summary>Procedura chiusa: si è trovato il limite o l'utente l'ha conclusa.</summary>
    public bool Completed { get; init; }

    public double? LastStable => Steps.LastOrDefault(s => s.Outcome == StepOutcome.Stable)?.Value;
}

/// <summary>Logica delle procedure a passi: valore successivo, gestione dei crash e valore finale con margine.</summary>
public static class TuningPlanner
{
    /// <summary>Valore da provare adesso, o null se la procedura è conclusa.</summary>
    public static double? NextValue(TuningProfile profile, TuningMethod method)
    {
        if (profile.Completed)
        {
            return null;
        }

        var limits = method.Limits;
        var last = profile.Steps.LastOrDefault();
        if (last is null)
        {
            return limits.Start;
        }

        return last.Outcome switch
        {
            StepOutcome.Pending => last.Value, // test ancora da completare con lo stesso valore
            StepOutcome.Aborted => null,       // prima va risolto il raffreddamento
            StepOutcome.Stable when limits.Step == 0 => null,
            StepOutcome.Stable => Beyond(last.Value + limits.Step, limits.Limit, limits.Step) ? null : last.Value + limits.Step,
            _ => null, // instabile o crash: si è trovato il limite
        };
    }

    /// <summary>
    /// Valore finale consigliato: l'ultimo stabile arretrato di un passo, per avere margine con temperature diverse,
    /// invecchiamento e carichi non coperti dal test. Null se nessun valore è risultato stabile.
    /// </summary>
    public static double? Recommended(TuningProfile profile, TuningMethod method)
    {
        if (profile.LastStable is not { } stable)
        {
            return null;
        }

        var step = method.Limits.Step;
        var hitLimit = profile.Steps.Any(s => s.Outcome is StepOutcome.Unstable or StepOutcome.Crashed);
        if (!hitLimit || step == 0)
        {
            return stable;
        }

        var backed = stable - step;
        // Arretrare oltre lo zero vorrebbe dire tornare alle impostazioni di fabbrica.
        return Math.Sign(backed) != Math.Sign(stable) ? 0 : backed;
    }

    /// <summary>All'avvio: un passo rimasto "in corso" significa che il PC si è bloccato o riavviato durante il test.</summary>
    public static TuningProfile MarkInterruptedAsCrashed(TuningProfile profile) =>
        profile.Steps.LastOrDefault() is { Outcome: StepOutcome.Pending, Report: null } pending && pending.StartedAt < DateTimeOffset.Now.AddMinutes(-1)
            ? profile with { Steps = [.. profile.Steps.SkipLast(1), pending with { Outcome = StepOutcome.Crashed, Note = "Il PC si è bloccato o riavviato durante il test." }] }
            : profile;

    public static TuningProfile Record(TuningProfile profile, TuningStep step) =>
        profile with { Steps = [.. profile.Steps.Where(s => !(s.Value == step.Value && s.Outcome == StepOutcome.Pending)), step] };

    private static bool Beyond(double value, double limit, double step) => step < 0 ? value < limit : value > limit;
}

/// <summary>Profili delle procedure in %ProgramData%\PcOptimizerPro\profiles\tuning.json.</summary>
public sealed class TuningProfileStore(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private string FilePath => Path.Combine(paths.Profiles, "tuning.json");

    public IReadOnlyList<TuningProfile> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<TuningProfile>>(File.ReadAllText(FilePath), JsonOptions) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Profilo aperto per una procedura (crea quello nuovo se serve), con i crash rilevati.</summary>
    public TuningProfile Open(string methodId)
    {
        var existing = Load().LastOrDefault(p => p.MethodId == methodId && !p.Completed);
        var profile = existing is null
            ? new TuningProfile { MethodId = methodId, StartedAt = DateTimeOffset.Now }
            : TuningPlanner.MarkInterruptedAsCrashed(existing);
        Save(profile);
        return profile;
    }

    public void Save(TuningProfile profile)
    {
        var all = Load().Where(p => p.Id != profile.Id).Append(profile).ToList();
        Directory.CreateDirectory(paths.Profiles);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }
}
