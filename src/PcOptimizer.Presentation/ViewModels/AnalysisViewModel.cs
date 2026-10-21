using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using PcOptimizer.Analysis.Model;

namespace PcOptimizer.Presentation.ViewModels;

/// <summary>Analisi: hardware rilevato, configurazione di Windows, software riconosciuto e impostazioni modificate.</summary>
public sealed partial class AnalysisViewModel(AppState state) : PageViewModel(state, "Analisi", "")
{
    public ObservableCollection<InfoRow> Hardware { get; } = [];

    public ObservableCollection<InfoRow> System { get; } = [];

    public ObservableCollection<FindingItem> Findings { get; } = [];

    public ObservableCollection<InfoRow> ModifiedSettings { get; } = [];

    public ObservableCollection<InfoRow> Software { get; } = [];

    [RelayCommand]
    private Task AnalyzeAsync() => State.InspectAsync();

    protected override void OnInspectionChanged()
    {
        if (State.Inspection is not { } i)
        {
            return;
        }

        var p = i.Profile;
        var a = i.Assessment;
        Hardware.Clear();
        Hardware.Add(new InfoRow("Fascia", $"{Format.Tier(a.Tier.Tier)} ({Format.Num(a.Tier.Score, "0")}/100)"));
        foreach (var c in a.Tier.Components)
        {
            Hardware.Add(new InfoRow($"  {c.Component}", $"{Format.Num(c.Score, "0")}/100 - {c.Explanation}"));
        }

        Hardware.Add(new InfoRow("CPU", $"{p.Cpu.BrandString} ({a.Cpu.ArchitectureName}, {p.Cpu.PhysicalCores} core / {p.Cpu.LogicalProcessors} thread)"));
        foreach (var g in a.Gpus.Where(g => !g.IsVirtual))
        {
            Hardware.Add(new InfoRow("GPU", $"{g.Gpu.Name}{(g.Gpu.DedicatedMemoryBytes is { } vram ? $", {Format.Gb(vram)}" : "")}, driver {g.Gpu.VendorDriverVersion ?? g.Gpu.DriverVersion ?? "n/d"}"));
        }

        Hardware.Add(new InfoRow("RAM", $"{Format.Num(a.Memory.InstalledGB, "0")} GB {a.Memory.Type}, {a.Memory.ModuleCount} moduli, " +
                                        $"{a.Memory.ConfiguredMTs?.ToString() ?? "n/d"} MT/s (targa {a.Memory.RatedMTs?.ToString() ?? "n/d"}), canali {a.Memory.Channels?.ToString() ?? "n/d"}"));
        foreach (var d in p.Disks)
        {
            Hardware.Add(new InfoRow("Disco", $"{d.FriendlyName} ({d.MediaType}, {d.BusType}, {Format.Gb(d.SizeBytes)}){(d.IsSystemDisk ? " - sistema" : "")}"));
        }

        foreach (var m in p.Displays)
        {
            Hardware.Add(new InfoRow("Monitor", $"{m.Name}: {m.Width}x{m.Height} a {Format.Num(m.RefreshHz, "0")} Hz (max {m.MaxRefreshHz?.ToString() ?? "n/d"})"));
        }

        Hardware.Add(new InfoRow("Scheda madre", $"{p.Board.BoardManufacturer} {p.Board.BoardProduct}, BIOS {p.Board.BiosVersion}"));

        var r = i.Report;
        System.Clear();
        System.Add(new InfoRow("Alimentazione", $"{r.Power.Name ?? "n/d"}{(r.Power.OverlayMode is { } o ? $" ({o})" : "")}"));
        System.Add(new InfoRow("Timer", $"{(r.Timer.CurrentMs is { } t ? Format.Num(t, "0.###") + " ms" : "n/d")}; HPET forzato: {YesNo(r.Timer.UsePlatformClock)}"));
        System.Add(new InfoRow("Gioco", $"modalità gioco {YesNo(r.Gaming.GameMode)}, HAGS {r.Gaming.Hags}, registrazione in background {YesNo(r.Gaming.BackgroundRecording)}"));
        System.Add(new InfoRow("Sicurezza", $"VBS {YesNo(r.Security.VbsRunning)}, integrità della memoria {YesNo(r.Security.HvciRunning)}, " +
                                            $"antivirus: {(r.Security.ActiveAntivirus.Count > 0 ? string.Join(", ", r.Security.ActiveAntivirus) : "nessuno")}"));
        System.Add(new InfoRow("Software", $"servizi in esecuzione {r.Software.ServicesRunning} (di terze parti {r.Software.ThirdPartyServicesRunning}), " +
                                           $"avvio automatico {r.Software.StartupEnabled}, attività {r.Software.ScheduledTasksEnabled}"));
        foreach (var irq in r.Interrupts.Where(x => x.Category is DeviceCategory.Gpu or DeviceCategory.Network or DeviceCategory.Audio))
        {
            System.Add(new InfoRow("Interrupt", $"{irq.Name}: {(irq.Mode == InterruptMode.Msi ? "MSI" : irq.Mode == InterruptMode.LineBased ? "a linea" : "n/d")}"));
        }

        Findings.Clear();
        foreach (var f in a.Findings.Concat(r.Findings).OrderByDescending(f => f.Severity))
        {
            Findings.Add(new FindingItem(Format.Severity(f.Severity), f.Severity.ToString(), f.Area, f.Title, f.Detail, f.Recommendation));
        }

        ModifiedSettings.Clear();
        foreach (var m in r.ModifiedSettings)
        {
            ModifiedSettings.Add(new InfoRow(m.Title, $"{m.Current} (predefinito: {m.Default}) - {EffectName(m.Effect)}. {m.Note}"));
        }

        Software.Clear();
        foreach (var k in r.Software.Known)
        {
            Software.Add(new InfoRow(k.Name + (k.Running ? " (in esecuzione)" : ""), (k.NeverTouch ? "Protetto, non viene mai modificato. " : "") + string.Join(", ", k.FoundIn)));
        }
    }

    private static string YesNo(bool? value) => value switch { true => "sì", false => "no", _ => "n/d" };

    private static string EffectName(TweakEffect e) => e switch
    {
        TweakEffect.Placebo => "nessun effetto",
        TweakEffect.Debatable => "da misurare",
        TweakEffect.Harmful => "sfavorevole",
        _ => "non riguarda le prestazioni",
    };
}
