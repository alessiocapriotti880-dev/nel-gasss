using System.Globalization;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Tweaks.Context;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Catalog;

/// <summary>
/// Interventi per singolo gioco indicato dall'utente. Sono tutte impostazioni standard di Windows applicate dal sistema
/// all'avvio del gioco: l'app non apre né modifica il processo del gioco e non interagisce con l'anti-cheat.
/// </summary>
internal static class GameTweaks
{
    private const string GpuPreferences = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string CompatLayers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string Ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    internal const string DisableFso = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    /// <summary>CpuPriorityClass di IFEO: 3 = Alta.</summary>
    internal const uint HighPriorityClass = 3;

    public static IEnumerable<CatalogTweak> Build(TweakContext context)
    {
        var profile = context.Profile();
        var hybridGraphics = profile.Gpus.Count(g => g.IsPhysicalPciDevice) >= 2 ||
                             (profile.Gpus.Any(g => g.Vendor == GpuVendor.Intel) && profile.Gpus.Any(g => g.Vendor is GpuVendor.Nvidia or GpuVendor.Amd));
        var cpu = profile.Cpu;

        foreach (var game in context.Games().Games)
        {
            var slug = ServiceTweaks.Slug(game.ExecutableName);

            yield return new CatalogTweak
            {
                Id = $"games.{slug}.gpu-high-performance",
                Title = $"{game.Name}: scheda video dedicata",
                Category = TweakCategory.Gpu,
                Group = TweakGroup.Games,
                WhatChanges = $"Imposta \"Prestazioni elevate\" come preferenza grafica per {game.ExecutableName} (Impostazioni > Schermo > Grafica).",
                Why = "Sui PC con grafica integrata e dedicata Windows può far partire il gioco sulla GPU integrata, molto più lenta.",
                ExpectedBenefit = "Il gioco usa sempre la scheda video dedicata.",
                Evidence = TweakEvidence.Documented,
                MeasuredBy = Measures.Frames,
                Recommended = hybridGraphics,
                NotApplicableReason = _ => hybridGraphics ? null : "Su questo PC c'è una sola scheda video.",
                Plan = _ => [Reg.Set(Reg.User(GpuPreferences, game.ExecutablePath, RegValueKind.String), "GpuPreference=2;")],
            };

            var currentLayers = context.Env().Reg(GameProbe(game)).Data;
            yield return new CatalogTweak
            {
                Id = $"games.{slug}.fso-off",
                Title = $"{game.Name}: ottimizzazioni a schermo intero disattivate",
                Category = TweakCategory.Gpu,
                Group = TweakGroup.Games,
                WhatChanges = $"Spunta \"Disabilita ottimizzazioni a schermo intero\" nelle proprietà di compatibilità di {game.ExecutableName}.",
                Why = "Alcuni giochi DirectX 9/11 in schermo intero rispondono meglio con lo schermo intero esclusivo \"vecchio stile\"; " +
                      "su Windows 10/11 recenti spesso è indifferente o peggiora.",
                ExpectedBenefit = "Variabile da gioco a gioco: si tiene solo se la misura con PresentMon migliora.",
                Risk = Risk.Low,
                Evidence = TweakEvidence.Debatable,
                MeasuredBy = Measures.Frames,
                Plan = _ => [Reg.Set(Reg.User(CompatLayers, game.ExecutablePath, RegValueKind.String), WithLayer(currentLayers, DisableFso))],
            };

            yield return new CatalogTweak
            {
                Id = $"games.{slug}.high-priority",
                Title = $"{game.Name}: priorità CPU alta all'avvio",
                Category = TweakCategory.Latency,
                Group = TweakGroup.Games,
                WhatChanges = $"Windows avvierà {game.ExecutableName} con priorità \"Alta\" (Image File Execution Options > PerfOptions > CpuPriorityClass).",
                Why = "Con priorità alta i thread del gioco vengono preferiti a quelli dei programmi in background quando la CPU è contesa.",
                ExpectedBenefit = "Utile solo con CPU sature da attività in background; altrimenti nessuna differenza.",
                Risk = Risk.Medium,
                Evidence = TweakEvidence.Debatable,
                MeasuredBy = Measures.Frames,
                Caution = "Impostazione standard di Windows applicata all'avvio: l'app non tocca il processo né l'anti-cheat. " +
                          "Con priorità alta audio e chat vocale possono avere meno CPU nei momenti di carico.",
                Plan = _ => [Reg.Set(Reg.Machine($@"{Ifeo}\{game.ExecutableName}\PerfOptions", "CpuPriorityClass"), HighPriorityClass)],
            };

            if (cpu.IsHybrid && context.Env().LaunchScriptFolder is { } folder)
            {
                var mask = PerformanceCoreMask(cpu);
                var script = Path.Combine(folder, $"{game.Name} (solo P-core).cmd");
                yield return new CatalogTweak
                {
                    Id = $"games.{slug}.p-cores-only",
                    Title = $"{game.Name}: avvio solo sui core P",
                    Category = TweakCategory.Latency,
                    Group = TweakGroup.Games,
                    WhatChanges = $"Crea sul desktop lo script \"{Path.GetFileName(script)}\" che avvia il gioco con affinità sui soli core ad alte prestazioni " +
                                  $"(maschera 0x{mask:X}).",
                    Why = "Sulle CPU ibride Intel il gioco può finire sui core E, più lenti: soprattutto su Windows 10, che non usa Thread Director.",
                    ExpectedBenefit = "Frame time più regolari se il gioco veniva spostato sui core E; su Windows 11 spesso nessuna differenza.",
                    Risk = Risk.Low,
                    Evidence = context.IsWindows11 ? TweakEvidence.Debatable : TweakEvidence.Situational,
                    MeasuredBy = Measures.Frames,
                    Caution = "Funziona solo se avvii il gioco da questo script e se il gioco non viene riavviato da un launcher o dall'anti-cheat.",
                    NotApplicableReason = _ => mask == 0 ? "Topologia della CPU non rilevata." : null,
                    Plan = _ => [new(new FileContentTarget(script), SettingValue.Of(LaunchScript(game, mask)))],
                };
            }
        }
    }

    /// <summary>Id della sonda di registro con i livelli di compatibilità attuali dell'eseguibile.</summary>
    public static string GameProbe(GameEntry game) => "game.layers." + game.ExecutablePath.ToLowerInvariant();

    public static RegistryValueTarget LayersTarget(GameEntry game) => Reg.User(CompatLayers, game.ExecutablePath, RegValueKind.String);

    /// <summary>Aggiunge un livello di compatibilità ("~ A B") senza perdere quelli già impostati.</summary>
    internal static string WithLayer(string? current, string layer)
    {
        var parts = (current ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p != "~").ToList();
        if (!parts.Contains(layer, StringComparer.OrdinalIgnoreCase))
        {
            parts.Add(layer);
        }

        return "~ " + string.Join(' ', parts);
    }

    /// <summary>
    /// Processori logici dei core P: Windows numera prima i core P (con i due thread di ciascuno se c'è l'Hyper-Threading),
    /// poi i core E.
    /// </summary>
    internal static ulong PerformanceCoreMask(CpuInfo cpu)
    {
        var threads = cpu.PerformanceCores * (cpu.HasSmt ? 2 : 1);
        return threads is <= 0 or > 64 ? 0 : threads == 64 ? ulong.MaxValue : (1UL << threads) - 1;
    }

    internal static string LaunchScript(GameEntry game, ulong mask) =>
        "@echo off\r\n" +
        "rem Creato da PC Optimizer Pro: avvia il gioco sui soli core ad alte prestazioni.\r\n" +
        $"start \"\" /affinity {mask.ToString("X", CultureInfo.InvariantCulture)} \"{game.ExecutablePath}\"\r\n";
}
