using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOptimizer.Tuning.Bios;
using PcOptimizer.Tuning.Guides;
using PcOptimizer.Tuning.Safety;
using PcOptimizer.Tuning.Stability;

namespace PcOptimizer.Presentation.ViewModels;

public sealed record BiosItemViewModel(string State, string Level, string Title, string Why, string? MenuPath, string Recommendation, string Risk, string? Verify);

/// <summary>Procedura guidata con il valore da impostare, la cronologia e i comandi per test ed esiti.</summary>
public sealed partial class TuningMethodViewModel : ObservableObject
{
    private readonly AppState _state;

    public TuningMethodViewModel(TuningMethod method, AppState state)
    {
        Method = method;
        _state = state;
        Reload();
    }

    public TuningMethod Method { get; }

    public string Title => Method.Title;

    public string Where => Method.Where;

    public string? Blocked => Method.Blocked;

    public bool IsAvailable => Method.Blocked is null;

    public string StepsText => string.Join("\n", Method.Steps.Select((s, i) => $"{i + 1}. {s}"));

    public string LimitsText => $"Passi di {Value(Method.Limits.Step)} da {Value(Method.Limits.Start)} fino a {Value(Method.Limits.Limit)} (limite fisso). " +
                                $"Test: {string.Join(", ", Method.Tests)} per {Method.TestDuration.TotalMinutes:0} minuti.";

    public ObservableCollection<InfoRow> History { get; } = [];

    [ObservableProperty]
    private string _nextText = "";

    [ObservableProperty]
    private double? _nextValue;

    private TuningProfile Profile => _state.Services.OpenTuningProfile(Method.Id);

    [RelayCommand]
    private async Task TestCurrentValueAsync()
    {
        if (NextValue is not { } value)
        {
            return;
        }

        if (!_state.Dialogs.Confirm(Method.Title, $"Hai impostato {Value(value)} ({Method.Where}) e riavviato se necessario?\n\nIl test dura circa " +
                                                  $"{Method.TestDuration.TotalMinutes * Method.Tests.Count:0} minuti: non usare il PC."))
        {
            return;
        }

        // Il passo si registra prima del test: se il PC si blocca risulterà "crash" alla riapertura.
        var profile = TuningPlanner.Record(Profile, new TuningStep { Value = value, StartedAt = DateTimeOffset.Now, Outcome = StepOutcome.Pending });
        _state.Services.SaveTuningProfile(profile);

        StabilityReport? last = null;
        foreach (var kind in Method.Tests)
        {
            last = await _state.RunAsync($"Test di stabilità {kind}", (p, ct) => _state.Services.RunStabilityTestAsync(kind, Method.TestDuration, p, ct));
            if (last is null || !last.Passed)
            {
                break;
            }
        }

        if (last is not null)
        {
            _state.Services.SaveTuningProfile(TuningPlanner.Record(profile, new TuningStep { Value = value, StartedAt = DateTimeOffset.Now, Outcome = last.Outcome, Report = last }));
            _state.Dialogs.Inform(Method.Title, last.Outcome switch
            {
                StepOutcome.Stable => "Stabile. Usa il PC normalmente per un po' prima del passo successivo.",
                StepOutcome.Aborted => $"Test interrotto per sicurezza: {last.AbortReason} Torna all'ultimo valore stabile e migliora il raffreddamento.",
                StepOutcome.Pending => "Test incompleto: il valore resta da verificare.",
                _ => $"INSTABILE ({last.Errors} errori, {last.WheaErrors} errori WHEA): torna subito all'ultimo valore stabile.",
            });
        }

        Reload();
    }

    [RelayCommand]
    private void MarkUnstable()
    {
        var value = NextValue ?? Profile.Steps.LastOrDefault()?.Value;
        if (value is null || !_state.Dialogs.Confirm(Method.Title, $"Segnare {Value(value.Value)} come instabile (crash o artefatti in gioco)?"))
        {
            return;
        }

        _state.Services.SaveTuningProfile(TuningPlanner.Record(Profile, new TuningStep { Value = value.Value, StartedAt = DateTimeOffset.Now, Outcome = StepOutcome.Unstable, Note = "Segnalato dall'utente" }));
        Reload();
    }

    [RelayCommand]
    private void Restart()
    {
        if (_state.Dialogs.Confirm(Method.Title, "Chiudere la procedura attuale e ricominciare da capo?"))
        {
            _state.Services.SaveTuningProfile(Profile with { Completed = true });
            Reload();
        }
    }

    private void Reload()
    {
        History.Clear();
        if (Method.Blocked is not null)
        {
            NextValue = null;
            NextText = "Non disponibile: " + Method.Blocked;
            return;
        }

        var profile = _state.Services.TuningProfiles().LastOrDefault(p => p.MethodId == Method.Id && !p.Completed);
        foreach (var step in profile?.Steps ?? [])
        {
            History.Add(new InfoRow(Value(step.Value), step.Outcome switch
            {
                StepOutcome.Stable => "stabile",
                StepOutcome.Unstable => "instabile",
                StepOutcome.Crashed => "crash",
                StepOutcome.Aborted => "interrotto per temperatura",
                _ => "da verificare",
            } + (step.Report is { } r ? $" ({r.Duration.TotalMinutes:0} min, {r.Errors} errori)" : "") + (step.Note is null ? "" : $" - {step.Note}")));
        }

        var current = profile ?? new TuningProfile { MethodId = Method.Id };
        NextValue = TuningPlanner.NextValue(current, Method);
        var recommended = TuningPlanner.Recommended(current, Method);
        NextText = NextValue is { } next
            ? $"Valore da impostare ora: {Value(next)}"
            : recommended is { } r2 ? $"Procedura conclusa. Valore finale consigliato: {Value(r2)}"
            : "Procedura conclusa senza valori stabili: lascia le impostazioni di fabbrica.";
    }

    private string Value(double v) => $"{(v > 0 && Method.Component != TuningComponent.Memory ? "+" : "")}{Format.Num(v)} {Method.Limits.Unit}";
}

/// <summary>Overclock e undervolt guidati: checklist del BIOS, procedure a passi e test di stabilità.</summary>
public sealed partial class TuningViewModel(AppState state) : PageViewModel(state, "Overclock e undervolt", "")
{
    public ObservableCollection<BiosItemViewModel> Bios { get; } = [];

    public ObservableCollection<TuningMethodViewModel> Methods { get; } = [];

    public IReadOnlyList<StabilityTestKind> TestKinds { get; } = Enum.GetValues<StabilityTestKind>();

    public string SafetyText { get; } =
        $"Limiti fissi: il test si ferma oltre {SafetyLimits.CpuTempHardC:0} °C di CPU (o {SafetyLimits.CpuTempSustainedC:0} °C per {SafetyLimits.SustainedSeconds} s), " +
        $"{SafetyLimits.GpuTempC:0} °C di GPU, {SafetyLimits.GpuHotspotC:0} °C di hotspot. L'app non scrive mai nel BIOS né cambia frequenze o tensioni.";

    [ObservableProperty]
    private StabilityTestKind _testKind = StabilityTestKind.Cpu;

    [ObservableProperty]
    private int _testMinutes = 10;

    [ObservableProperty]
    private string _testResult = "";

    [RelayCommand]
    private async Task RunTestAsync()
    {
        var report = await State.RunAsync($"Test di stabilità {TestKind}", (p, ct) =>
            State.Services.RunStabilityTestAsync(TestKind, TimeSpan.FromMinutes(Math.Clamp(TestMinutes, 1, 120)), p, ct));
        if (report is not null)
        {
            var cpu = report.Sensors.Select(s => s.CpuTempC).Max();
            var gpu = report.Sensors.Select(s => s.GpuTempC).Max();
            TestResult = $"{(report.Passed ? "STABILE" : report.AbortReason is not null ? "INTERROTTO" : "INSTABILE")}: {report.WorkUnits:N0} unità verificate, " +
                         $"{report.Errors} errori, {report.WheaErrors} errori WHEA in {report.Duration.TotalMinutes:0.#} min. " +
                         $"CPU max {(cpu is { } c ? Format.Num(c, "0") + " °C" : "n/d")}, GPU max {(gpu is { } g ? Format.Num(g, "0") + " °C" : "n/d")}." +
                         (report.AbortReason is null ? "" : $" {report.AbortReason}");
        }
    }

    [RelayCommand]
    private void CancelTest() => State.Cancel();

    protected override void OnInspectionChanged()
    {
        if (State.Inspection is not { } i)
        {
            return;
        }

        Bios.Clear();
        foreach (var b in i.BiosChecklist)
        {
            Bios.Add(new BiosItemViewModel(b.State switch
                {
                    ChecklistState.Action => "Da fare",
                    ChecklistState.Done => "A posto",
                    ChecklistState.Check => "Da verificare",
                    ChecklistState.Blocked => "Bloccato",
                    _ => "Info",
                },
                b.State.ToString(), b.Title, b.Why, b.MenuPath, b.Recommendation, Format.Risk(b.Risk), b.Verify));
        }

        Methods.Clear();
        foreach (var m in i.TuningMethods)
        {
            Methods.Add(new TuningMethodViewModel(m, State));
        }
    }
}
