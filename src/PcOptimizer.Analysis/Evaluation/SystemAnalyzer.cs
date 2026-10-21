using System.Globalization;
using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Analysis.Parsing;
using PcOptimizer.Core.Settings;

namespace PcOptimizer.Analysis.Evaluation;

/// <summary>
/// Interpreta la configurazione raccolta: riepiloghi per la dashboard, modifiche rispetto ai
/// valori predefiniti di Windows e osservazioni. Non accede al sistema.
/// </summary>
public sealed class SystemAnalyzer(KnownSoftwareCatalog catalog)
{
    // Elementi BCD che riguardano il timer: assenti in un'installazione standard di Windows.
    internal static readonly string[] TimerBcdElements = ["useplatformclock", "useplatformtick", "disabledynamictick", "tscsyncpolicy"];

    // Categorie di programmi che, se aperti mentre si gioca, occupano RAM e CPU in background.
    private static readonly HashSet<SoftwareCategory> BackgroundCategories =
    [
        SoftwareCategory.Overlay, SoftwareCategory.Recording, SoftwareCategory.Launcher, SoftwareCategory.Chat,
        SoftwareCategory.Rgb, SoftwareCategory.Monitoring, SoftwareCategory.Updater, SoftwareCategory.Cloud,
        SoftwareCategory.Media, SoftwareCategory.Optimizer, SoftwareCategory.Browser,
    ];

    public SystemReport Analyze(SystemSnapshot snapshot, AnalysisContext context)
    {
        var power = BuildPower(snapshot);
        var timer = BuildTimer(snapshot);
        var interrupts = BuildInterrupts(snapshot);
        var gaming = BuildGaming(snapshot);
        var security = BuildSecurity(snapshot);
        var software = BuildSoftware(snapshot);
        var modified = BuildModifiedSettings(snapshot, gaming);
        var findings = AnalysisFindings.Build(new AnalysisFindings.Input(
            snapshot, context, power, timer, interrupts, gaming, security, software, modified));

        return new SystemReport
        {
            Power = power,
            Timer = timer,
            Interrupts = interrupts,
            Gaming = gaming,
            Security = security,
            Software = software,
            ModifiedSettings = modified,
            Findings = findings,
        };
    }

    internal static PowerReport BuildPower(SystemSnapshot s)
    {
        var p = s.Power;
        var kind = PowerPlanParser.Kind(p.ActiveScheme, p.ActiveSchemeName);
        return new PowerReport
        {
            Kind = kind,
            Name = p.ActiveSchemeName,
            HighPerformanceAvailable = p.Schemes.Any(x => PowerPlanParser.Kind(x.Id, x.Name) == PowerPlanKind.HighPerformance),
            UltimatePerformanceAvailable = p.Schemes.Any(x => PowerPlanParser.Kind(x.Id, x.Name) == PowerPlanKind.UltimatePerformance),
            // Il cursore della modalità di alimentazione esiste solo sopra il piano Bilanciato.
            OverlayMode = kind == PowerPlanKind.Balanced ? PowerPlanParser.OverlayName(s.Reg(RegistryProbes.OverlayAc.Id).Data) : null,
            MinProcessorPercent = p.Ac(PowerSettingsCatalog.ProcessorMin),
            MaxProcessorPercent = p.Ac(PowerSettingsCatalog.ProcessorMax),
            CoreParkingMinCoresPercent = p.Ac(PowerSettingsCatalog.CoreParkingMinCores),
            BoostMode = p.Ac(PowerSettingsCatalog.BoostMode),
            IdleDisabled = p.Ac(PowerSettingsCatalog.IdleDisable) is { } idle ? idle != 0 : null,
            UsbSelectiveSuspend = p.Ac(PowerSettingsCatalog.UsbSelectiveSuspend) is { } usb ? usb != 0 : null,
            PcieAspm = p.Ac(PowerSettingsCatalog.PcieAspm),
        };
    }

    internal static TimerReport BuildTimer(SystemSnapshot s)
    {
        var t = s.Timer;

        // Elemento assente = valore predefinito di Windows (falso); senza lettura del BCD = sconosciuto.
        bool? Element(string name) => t.BcdElements.TryGetValue(name, out var raw) ? BcdParser.ToBool(raw)
            : t.BcdReadable ? false
            : null;

        return new TimerReport
        {
            CurrentMs = t.CurrentMs,
            FinestMs = t.FinestMs,
            GlobalResolutionRequests = s.Reg("timer.globalrequests").Data == "1",
            BcdReadable = t.BcdReadable,
            UsePlatformClock = Element("useplatformclock"),
            UsePlatformTick = Element("useplatformtick"),
            DisableDynamicTick = Element("disabledynamictick"),
            TscSyncPolicy = t.BcdElements.TryGetValue("tscsyncpolicy", out var tsc) ? tsc : null,
            HpetDeviceEnabled = t.HpetDeviceEnabled,
        };
    }

    internal static IReadOnlyList<InterruptReport> BuildInterrupts(SystemSnapshot s)
    {
        var devices = s.PciDevices
            .Select(d => (Device: d, Category: DeviceClassParser.Category(d.ClassGuid)))
            .Where(x => x.Category != DeviceCategory.Other)
            .ToList();

        // IRQ line-based usati da più dispositivi rilevanti per la latenza.
        var lineIrqOwners = devices
            .SelectMany(x => x.Device.Irqs.Where(i => i < IrqParser.MsiThreshold).Select(i => (Irq: i, x.Device.Name)))
            .GroupBy(x => x.Irq)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name).Distinct().ToList());

        return devices.Select(x =>
        {
            var d = x.Device;
            var shares = d.Irqs
                .Where(i => i < IrqParser.MsiThreshold && lineIrqOwners.TryGetValue(i, out var owners) && owners.Count > 1)
                .SelectMany(i => lineIrqOwners[i])
                .Where(name => name != d.Name)
                .Distinct()
                .ToList();

            return new InterruptReport
            {
                Name = d.Name,
                Category = x.Category,
                Mode = IrqParser.Mode(d.Irqs),
                MsiSupportedSetting = d.MsiSupported,
                CustomAffinity = d.AffinityPolicy is > 0 || d.DevicePriority is > 0 || d.AffinityMask is not null,
                SharesIrqWith = shares,
            };
        }).ToList();
    }

    internal static GamingReport BuildGaming(SystemSnapshot s)
    {
        var dvrPolicyOff = s.Reg(RegistryProbes.GameDvrPolicy.Id).Data == "0";
        var gameDvr = !dvrPolicyOff && s.Reg(RegistryProbes.GameDvrEnabled.Id).Data != "0";
        var directX = DirectXSettingsParser.Parse(s.Reg(RegistryProbes.DirectXGlobal.Id).Data);
        var (hags, hagsDefault) = ResolveHags(s);

        return new GamingReport
        {
            // Valore assente = modalità gioco attiva (predefinito da Windows 10 1903).
            GameMode = s.Reg(RegistryProbes.GameMode.Id).Data != "0",
            Hags = hags,
            HagsEnabledByDefault = hagsDefault,
            GameDvr = gameDvr,
            BackgroundRecording = gameDvr && s.Reg(RegistryProbes.HistoricalCapture.Id).Data == "1",
            FsoGloballyDisabled = s.Reg(RegistryProbes.FseBehavior.Id).Data == "2" && s.Reg(RegistryProbes.FseHonorUser.Id).Data == "1",
            FsoDisabledApps = s.FsoDisabledApps.Select(FileNameOf).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            WindowedOptimizations = DirectXSettingsParser.Flag(directX, "SwapEffectUpgradeEnable"),
            VrrOptimizations = DirectXSettingsParser.Flag(directX, "VRROptimizeEnable"),
            PowerSavingGpuApps = s.GpuPreferences.Where(g => g.Preference == 1).Select(g => FileNameOf(g.Application)).ToList(),
            MpoDisabled = s.Reg("graphics.mpo").Data == "5",
            MouseAcceleration = MiscParsers.MouseAcceleration(s.Reg(RegistryProbes.MouseSpeed.Id).Data),
        };
    }

    /// <summary>
    /// HAGS dal driver (D3DKMT) quando disponibile: è lo stato reale, mentre il registro indica
    /// solo la preferenza; senza dati dal driver si usa il registro (2 = attivo, 1 = disattivo).
    /// </summary>
    private static (HagsState State, bool? EnabledByDefault) ResolveHags(SystemSnapshot s)
    {
        var adapters = s.GpuScheduling;
        if (adapters.Count > 0)
        {
            var supported = adapters.Where(a => a.HwSchSupported).ToList();
            return supported.Count == 0
                ? (HagsState.NotSupported, null)
                : (supported.Any(a => a.HwSchEnabled) ? HagsState.On : HagsState.Off,
                   supported.Any(a => a.HwSchEnabledByDefault));
        }

        return s.Reg(RegistryProbes.HwSchMode.Id).Data switch
        {
            "2" => (HagsState.On, null),
            "1" => (HagsState.Off, null),
            _ => (HagsState.Unknown, null),
        };
    }

    internal static SecurityReport BuildSecurity(SystemSnapshot s)
    {
        var sec = s.Security;
        var active = sec.AntivirusProducts.Where(a => a.Enabled).Select(a => a.Name).ToList();
        if (sec.DefenderRealTimeProtection == true && !active.Any(a => a.Contains("Defender", StringComparison.OrdinalIgnoreCase)))
        {
            active.Add("Microsoft Defender");
        }

        bool? antivirusActive = active.Count > 0 ? true
            : sec.AntivirusProducts.Count > 0 || sec.DefenderRealTimeProtection is not null ? false
            : null;

        var wu = s.Services.FirstOrDefault(x => x.Name.Equals("wuauserv", StringComparison.OrdinalIgnoreCase));
        bool? updateDisabled = s.Reg(RegistryProbes.NoAutoUpdate.Id).Data == "1" ||
                               s.Reg(RegistryProbes.DisableWindowsUpdateAccess.Id).Data == "1" ||
                               wu?.StartMode == ServiceStartValues.Disabled
            ? true
            : wu is null ? null : false;

        return new SecurityReport
        {
            VbsRunning = sec.VbsStatus is { } vbs ? vbs == 2 : null,
            HvciRunning = sec.VbsStatus is not null ? sec.SecurityServicesRunning.Contains(2) : null,
            HypervisorPresent = sec.HypervisorPresent,
            AntivirusActive = antivirusActive,
            ActiveAntivirus = active,
            WindowsUpdateDisabled = updateDisabled,
            TpmVersion = sec.TpmPresent switch
            {
                true => sec.TpmSpecVersion?.Split(',')[0].Trim(),
                false => "assente",
                _ => null,
            },
        };
    }

    internal SoftwareReport BuildSoftware(SystemSnapshot s)
    {
        var matches = new Dictionary<string, MatchBuilder>(StringComparer.OrdinalIgnoreCase);
        MatchBuilder For(KnownSoftwareEntry entry) =>
            matches.TryGetValue(entry.Id, out var b) ? b : matches[entry.Id] = new MatchBuilder(entry);

        foreach (var p in s.Processes)
        {
            if (catalog.MatchProcess(p.Name) is { } entry)
            {
                var b = For(entry);
                b.Add($"processo {p.Name}.exe");
                b.Running = true;
                b.PrivateBytes += p.PrivateBytes;
            }
        }

        foreach (var svc in s.Services)
        {
            if (catalog.MatchService(svc.Name) is { } entry)
            {
                var b = For(entry);
                b.Add($"servizio {svc.Name} ({(svc.IsRunning ? "in esecuzione" : "fermo")}, avvio {StartModeName(svc.StartMode)})");
                b.Running |= svc.IsRunning;
            }
        }

        foreach (var drv in s.KernelDrivers)
        {
            if (catalog.MatchDriver(drv.Name, drv.FileName) is { } entry)
            {
                var b = For(entry);
                b.Add($"driver {drv.FileName}{(drv.IsRunning ? " (caricato)" : "")}");
                b.Running |= drv.IsRunning;
            }
        }

        foreach (var st in s.Startup)
        {
            if (catalog.MatchStartup(st.Name, st.Command) is { } entry)
            {
                For(entry).Add(st.Enabled ? "avvio automatico" : "avvio automatico (disattivato)");
            }
        }

        foreach (var task in s.ScheduledTasks.Where(t => t.Enabled))
        {
            if (catalog.MatchTask(task.Path, task.Executable) is { } entry)
            {
                For(entry).Add($"attività pianificata {FileNameOf(task.Path)}");
            }
        }

        var known = matches.Values
            .Select(b => b.Build())
            .OrderBy(m => m.Category)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var vulnerable = s.KernelDrivers
            .Select(d => (Driver: d, Entry: catalog.MatchDriver(d.Name, d.FileName)))
            .Where(x => x.Entry?.Risk == SoftwareRisk.Vulnerable)
            .Select(x => $"{x.Driver.FileName} ({x.Entry!.Name}{(x.Driver.IsRunning ? ", caricato" : "")})")
            .ToList();

        return new SoftwareReport
        {
            ServicesTotal = s.Services.Count,
            ServicesRunning = s.Services.Count(x => x.IsRunning),
            // Produttore sconosciuto non conta come terze parti: evita di includere servizi di Windows non leggibili.
            ThirdPartyServicesRunning = s.Services.Count(x => x.IsRunning && x.Company is not null && !x.IsMicrosoft),
            StartupEnabled = s.Startup.Count(x => x.Enabled),
            ScheduledTasksEnabled = s.ScheduledTasks.Count(t => t.Enabled),
            ThirdPartyTasksEnabled = s.ScheduledTasks.Count(t => t.Enabled && !t.IsMicrosoft),
            Known = known,
            BackgroundPrivateBytes = known.Where(m => m.Running && BackgroundCategories.Contains(m.Category)).Sum(m => m.PrivateBytes),
            VulnerableDrivers = vulnerable,
            UnsignedDrivers = s.DeviceDrivers.Where(d => d.IsSigned == false).Select(d => d.DeviceName).Distinct().ToList(),
            PageFileEnabled = s.PageFile.AutomaticManaged is null ? null
                : s.PageFile.Files.Count > 0 || s.PageFile.AutomaticManaged == true,
        };
    }

    internal static IReadOnlyList<ModifiedSetting> BuildModifiedSettings(SystemSnapshot s, GamingReport gaming)
    {
        var result = new List<ModifiedSetting>();
        foreach (var tweak in RegistryProbes.KnownTweaks)
        {
            var value = s.Reg(tweak.Probe.Id);
            if (!tweak.IsDefault(value))
            {
                result.Add(new ModifiedSetting(tweak.Probe.Id, tweak.Title, Display(value),
                    string.Join(" o ", tweak.Defaults.Select(d => d ?? "assente")), tweak.Effect, tweak.Note));
            }
        }

        foreach (var (name, raw) in s.Timer.BcdElements)
        {
            var (title, effect, note) = name switch
            {
                "useplatformclock" => ("HPET forzato come sorgente del tempo (useplatformclock)", TweakEffect.Harmful,
                    "Obbliga Windows a leggere il tempo dal timer HPET, molto più lento del TSC della CPU: " +
                    "rende più costose le misure di tempo che giochi e driver fanno di continuo."),
                "useplatformtick" => ("Tick di piattaforma forzato (useplatformtick)", TweakEffect.Debatable,
                    "Usa il timer di piattaforma per il tick di sistema: effetto sulla latenza da misurare."),
                "disabledynamictick" => ("Dynamic tick disattivato (disabledynamictick)", TweakEffect.Debatable,
                    "Il tick di sistema non si ferma mai quando la CPU è inattiva: consumi più alti, effetto sulla latenza da misurare."),
                _ => ("Sincronizzazione TSC (tscsyncpolicy)", TweakEffect.Debatable,
                    "Criterio di sincronizzazione del TSC tra i core: Windows sceglie già quello adatto."),
            };
            result.Add(new ModifiedSetting($"bcd.{name}", title, raw, "non impostato", effect, note));
        }

        foreach (var tcp in s.TcpInterfaces.Where(t => t.TcpAckFrequency is not null || t.TcpNoDelay is not null))
        {
            result.Add(new ModifiedSetting($"network.nagle.{tcp.InterfaceId}",
                $"Algoritmo di Nagle disattivato (interfaccia {tcp.InterfaceId})",
                $"TcpAckFrequency={tcp.TcpAckFrequency?.ToString() ?? "-"}, TCPNoDelay={tcp.TcpNoDelay?.ToString() ?? "-"}",
                "assenti", TweakEffect.Debatable,
                "Riguarda solo il traffico TCP: la maggior parte dei giochi online usa UDP, quindi spesso non cambia nulla."));
        }

        if (gaming.FsoGloballyDisabled)
        {
            result.Add(new ModifiedSetting("gaming.fso.global", "Ottimizzazioni a schermo intero disattivate globalmente",
                "sì", "no", TweakEffect.Debatable,
                "Con le ottimizzazioni attive i giochi a schermo intero usano il modello di presentazione moderno; " +
                "disattivarle per tutti i giochi ha effetti diversi da titolo a titolo."));
        }

        return result;
    }

    internal static string Display(SettingValue value)
    {
        if (!value.Exists)
        {
            return "assente";
        }

        return ulong.TryParse(value.Data, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 255
            ? $"{n} (0x{n:X})"
            : value.Data ?? "";
    }

    internal static string StartModeName(string startMode) => startMode switch
    {
        ServiceStartValues.Automatic => "automatico",
        ServiceStartValues.AutomaticDelayed => "automatico ritardato",
        ServiceStartValues.Manual => "manuale",
        ServiceStartValues.Disabled => "disabilitato",
        _ => startMode.ToLowerInvariant(),
    };

    // I percorsi arrivano in formato Windows anche quando i test girano su Linux: non si usa Path.
    internal static string FileNameOf(string path)
    {
        var i = path.LastIndexOfAny(['\\', '/']);
        return i >= 0 ? path[(i + 1)..] : path;
    }

    private sealed class MatchBuilder(KnownSoftwareEntry entry)
    {
        private readonly List<string> _foundIn = [];

        public bool Running { get; set; }

        public long PrivateBytes { get; set; }

        public void Add(string where)
        {
            if (!_foundIn.Contains(where, StringComparer.OrdinalIgnoreCase))
            {
                _foundIn.Add(where);
            }
        }

        public SoftwareMatch Build() => new()
        {
            Id = entry.Id,
            Name = entry.Name,
            Category = entry.Category,
            Risk = entry.Risk,
            Overlay = entry.Overlay,
            NeverTouch = entry.NeverTouch,
            Note = entry.Note,
            FoundIn = _foundIn,
            Running = Running,
            PrivateBytes = PrivateBytes,
        };
    }
}
