using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Evaluation;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using static PcOptimizer.Analysis.Tests.Snapshots;

namespace PcOptimizer.Analysis.Tests;

public sealed class AnalyzerTests
{
    private static readonly SystemAnalyzer Analyzer = new(KnownSoftwareCatalog.Load());

    private static SystemReport Analyze(SystemSnapshot s, AnalysisContext? context = null) =>
        Analyzer.Analyze(s, context ?? Desktop());

    private static IReadOnlyList<string> Ids(SystemReport r) => r.Findings.Select(f => f.Id).ToList();

    private static Finding Single(SystemReport r, string id) => r.Findings.Single(f => f.Id == id);

    [Fact]
    public void Default_windows_configuration_produces_no_warnings_and_no_modified_settings()
    {
        var report = Analyze(Clean());

        Assert.Empty(report.ModifiedSettings);
        Assert.DoesNotContain(report.Findings, f => f.Severity >= FindingSeverity.Suggestion);
        Assert.Equal(PowerPlanKind.Balanced, report.Power.Kind);
        Assert.True(report.Gaming.GameMode);
        Assert.Equal(HagsState.On, report.Gaming.Hags);
        Assert.Equal(false, report.Timer.UsePlatformClock);
        Assert.Equal(true, report.Security.AntivirusActive);
        Assert.Equal(false, report.Security.WindowsUpdateDisabled);
        Assert.All(report.Interrupts, i => Assert.Equal(InterruptMode.Msi, i.Mode));
    }

    // --- Alimentazione ---

    [Fact]
    public void Power_saver_plan_is_a_warning_on_desktop_and_a_suggestion_on_laptop()
    {
        var saver = Clean() with { Power = Clean().Power with { ActiveScheme = PowerGuids.PowerSaver, ActiveSchemeName = "Risparmio di energia" } };

        Assert.Equal(FindingSeverity.Warning, Single(Analyze(saver), "power-plan-saver").Severity);
        Assert.Equal(FindingSeverity.Suggestion, Single(Analyze(saver, Desktop() with { IsLaptop = true }), "power-plan-saver").Severity);
    }

    [Fact]
    public void Capped_cpu_and_disabled_boost_are_warnings()
    {
        var ids = Ids(Analyze(Clean().WithPower(PowerSettingsCatalog.ProcessorMax, 80).WithPower(PowerSettingsCatalog.BoostMode, 0)));

        Assert.Contains("power-cpu-capped", ids);
        Assert.Contains("power-boost-off", ids);
    }

    [Fact]
    public void Efficiency_overlay_and_disabled_c_states_are_reported()
    {
        var s = Clean()
            .WithRegistry((RegistryProbes.OverlayAc.Id, "961cc777-2547-4f9d-8174-7d86181b8a7a"))
            .WithPower(PowerSettingsCatalog.IdleDisable, 1);

        var report = Analyze(s);

        Assert.Equal("Massima efficienza energetica", report.Power.OverlayMode);
        Assert.Contains("power-overlay-efficiency", Ids(report));
        Assert.Equal(FindingSeverity.Info, Single(report, "power-idle-disabled").Severity);
    }

    [Fact]
    public void Duplicated_ultimate_plan_is_recognized_by_name()
    {
        var custom = Guid.NewGuid();
        var s = Clean() with
        {
            Power = Clean().Power with
            {
                ActiveScheme = custom, ActiveSchemeName = "Prestazioni eccellenti",
                Schemes = [.. Clean().Power.Schemes, new PowerSchemeEntry(custom, "Prestazioni eccellenti")],
            },
        };

        var power = Analyze(s).Power;

        Assert.Equal(PowerPlanKind.UltimatePerformance, power.Kind);
        Assert.True(power.UltimatePerformanceAvailable);
        Assert.True(power.HighPerformanceAvailable);
    }

    // --- Timer ---

    [Theory]
    [InlineData("Yes")]
    [InlineData("Sì")]
    [InlineData("Ja")]
    public void Forced_hpet_is_a_warning_and_a_harmful_modification(string localizedYes)
    {
        var s = Clean() with
        {
            Timer = Clean().Timer with { BcdElements = new Dictionary<string, string> { ["useplatformclock"] = localizedYes } },
        };

        var report = Analyze(s);

        Assert.Equal(true, report.Timer.UsePlatformClock);
        Assert.Equal(FindingSeverity.Warning, Single(report, "timer-useplatformclock").Severity);
        Assert.Equal(TweakEffect.Harmful, report.ModifiedSettings.Single(m => m.Id == "bcd.useplatformclock").Effect);
        Assert.DoesNotContain(Ids(report), id => id.StartsWith("modified-harmful-bcd", StringComparison.Ordinal));
    }

    [Fact]
    public void Unreadable_boot_configuration_is_unknown_not_default()
    {
        var report = Analyze(Clean() with { Timer = Clean().Timer with { BcdReadable = false } });

        Assert.Null(report.Timer.UsePlatformClock);
        Assert.DoesNotContain("timer-useplatformclock", Ids(report));
    }

    [Fact]
    public void High_timer_resolution_at_idle_and_disabled_hpet_device_are_info()
    {
        var report = Analyze(Clean() with { Timer = Clean().Timer with { CurrentMs = 1.0, HpetDeviceEnabled = false } });

        Assert.Equal(FindingSeverity.Info, Single(report, "timer-high-resolution-idle").Severity);
        Assert.Equal(FindingSeverity.Info, Single(report, "timer-hpet-device-disabled").Severity);
    }

    // --- Interrupt ---

    [Fact]
    public void Line_based_gpu_sharing_an_irq_with_audio_is_reported_once()
    {
        var s = Clean() with
        {
            PciDevices =
            [
                Pci("NVIDIA GeForce GTX 1060", Display, 16),
                Pci("High Definition Audio Controller", Media, 16),
            ],
        };

        var report = Analyze(s);

        Assert.Equal(InterruptMode.LineBased, report.Interrupts.Single(i => i.Category == DeviceCategory.Gpu).Mode);
        Assert.Contains("irq-gpu-line-based", Ids(report));
        Assert.Single(report.Findings, f => f.Id == "irq-shared");
        Assert.Contains("NVIDIA GeForce GTX 1060", Single(report, "irq-shared").Title);
    }

    [Fact]
    public void Custom_interrupt_affinity_is_info()
    {
        var s = Clean() with { PciDevices = [Clean().PciDevices[0] with { AffinityPolicy = 4, AffinityMask = 0b100 }] };

        var finding = Single(Analyze(s), "irq-custom-affinity");

        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.EndsWith("su 1 dispositivo", finding.Title);
        Assert.StartsWith("NVIDIA GeForce RTX 4070", finding.Detail);
    }

    // --- Impostazioni di gioco ---

    [Fact]
    public void Disabled_game_mode_and_background_recording_are_suggestions()
    {
        var s = Clean().WithRegistry((RegistryProbes.GameMode.Id, "0"), (RegistryProbes.HistoricalCapture.Id, "1"));

        var ids = Ids(Analyze(s));

        Assert.Contains("gamemode-off", ids);
        Assert.Contains("gamedvr-background-recording", ids);
    }

    [Fact]
    public void Game_dvr_policy_disables_background_recording()
    {
        var s = Clean().WithRegistry((RegistryProbes.HistoricalCapture.Id, "1"), (RegistryProbes.GameDvrPolicy.Id, "0"));

        var report = Analyze(s);

        Assert.False(report.Gaming.BackgroundRecording);
        Assert.DoesNotContain("gamedvr-background-recording", Ids(report));
    }

    [Fact]
    public void Hags_state_comes_from_the_driver_or_falls_back_to_the_registry()
    {
        var off = Clean() with { GpuScheduling = [Clean().GpuScheduling[0] with { HwSchEnabled = false }] };
        Assert.Equal(HagsState.Off, Analyze(off).Gaming.Hags);
        Assert.Contains("hags-off", Ids(Analyze(off)));
        Assert.DoesNotContain("hags-off", Ids(Analyze(off, Desktop() with { WindowsBuild = 19045 })));

        var unsupported = Clean() with { GpuScheduling = [new GpuSchedulingEntry { AdapterName = "Microsoft Basic Render Driver" }] };
        Assert.Equal(HagsState.NotSupported, Analyze(unsupported).Gaming.Hags);

        var registryOnly = (Clean() with { GpuScheduling = [] }).WithRegistry((RegistryProbes.HwSchMode.Id, "2"));
        Assert.Equal(HagsState.On, Analyze(registryOnly).Gaming.Hags);
    }

    [Fact]
    public void Windowed_optimizations_are_flagged_only_when_explicitly_off_on_windows_11_22h2()
    {
        var off = Clean().WithRegistry((RegistryProbes.DirectXGlobal.Id, "SwapEffectUpgradeEnable=0;VRROptimizeEnable=0;"));

        Assert.Contains("windowed-optimizations-off", Ids(Analyze(off)));
        Assert.DoesNotContain("windowed-optimizations-off", Ids(Analyze(off, Desktop() with { WindowsBuild = 19045 })));
        Assert.Null(Analyze(Clean()).Gaming.WindowedOptimizations);
    }

    [Fact]
    public void Power_saving_gpu_preference_matters_only_with_two_gpus()
    {
        var s = Clean() with { GpuPreferences = [new GpuPreferenceEntry(@"C:\Riot Games\VALORANT\live\VALORANT.exe", 1)] };

        var finding = Single(Analyze(s, Desktop(gpus: 2) with { IsLaptop = true }), "gpu-power-saving-apps");

        Assert.Contains("VALORANT.exe", finding.Detail);
        Assert.DoesNotContain("gpu-power-saving-apps", Ids(Analyze(s)));
    }

    [Fact]
    public void Fso_overrides_and_mouse_acceleration_are_info()
    {
        var s = (Clean() with { FsoDisabledApps = [@"C:\Games\cs2.exe"] })
            .WithRegistry((RegistryProbes.MouseSpeed.Id, "1"), (RegistryProbes.FseBehavior.Id, "2"), (RegistryProbes.FseHonorUser.Id, "1"));

        var report = Analyze(s);

        Assert.Equal(["cs2.exe"], report.Gaming.FsoDisabledApps);
        Assert.Equal(FindingSeverity.Info, Single(report, "fso-disabled-apps").Severity);
        Assert.Equal(FindingSeverity.Info, Single(report, "mouse-acceleration").Severity);
        Assert.Contains(report.ModifiedSettings, m => m.Id == "gaming.fso.global");
    }

    // --- Sicurezza ---

    [Fact]
    public void No_active_antivirus_is_a_warning()
    {
        var s = Clean() with
        {
            Security = Clean().Security with { DefenderRealTimeProtection = false, AntivirusProducts = [new AntivirusEntry("Windows Defender", false)] },
        };

        Assert.Equal(FindingSeverity.Warning, Single(Analyze(s), "antivirus-none").Severity);
    }

    [Fact]
    public void Third_party_antivirus_with_passive_defender_is_fine()
    {
        var s = Clean() with
        {
            Security = Clean().Security with
            {
                DefenderRealTimeProtection = false,
                AntivirusProducts = [new AntivirusEntry("Windows Defender", false), new AntivirusEntry("ESET Security", true)],
            },
        };

        var report = Analyze(s);

        Assert.Equal(["ESET Security"], report.Security.ActiveAntivirus);
        Assert.DoesNotContain("antivirus-none", Ids(report));
    }

    [Fact]
    public void Disabled_windows_update_is_a_warning()
    {
        var byService = Clean() with { Services = [Service("wuauserv", ServiceStartValues.Disabled, "Stopped")] };
        var byPolicy = Clean().WithRegistry((RegistryProbes.NoAutoUpdate.Id, "1"));

        Assert.Contains("windows-update-disabled", Ids(Analyze(byService)));
        Assert.Contains("windows-update-disabled", Ids(Analyze(byPolicy)));
    }

    [Fact]
    public void Running_vbs_with_hvci_is_info_never_a_recommendation_to_disable()
    {
        var s = Clean() with { Security = Clean().Security with { VbsStatus = 2, SecurityServicesRunning = [2] } };

        var finding = Single(Analyze(s), "vbs-running");

        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Contains("integrità della memoria", finding.Title);
    }

    [Fact]
    public void Missing_tpm_is_reported_only_when_it_could_be_read()
    {
        var noTpm = Clean() with { Security = Clean().Security with { TpmPresent = false } };

        Assert.Contains("tpm-missing", Ids(Analyze(noTpm)));
        Assert.DoesNotContain("tpm-missing", Ids(Analyze(noTpm with { IsElevated = false })));
    }

    // --- Software ---

    [Fact]
    public void Anti_cheat_is_recognized_and_marked_never_touch()
    {
        var s = Clean() with
        {
            Services = [.. Clean().Services, Service("vgc", ServiceStartValues.Manual, "Stopped", "Riot Games, Inc.")],
            KernelDrivers = [new KernelDriverEntry { Name = "vgk", FilePath = @"C:\Program Files\Riot Vanguard\vgk.sys", State = "Running" }],
        };

        var report = Analyze(s);

        var vanguard = report.Software.Known.Single(k => k.Id == "vanguard");
        Assert.True(vanguard.NeverTouch);
        Assert.Contains("Riot Vanguard", Single(report, "anticheat-installed").Title);
    }

    [Fact]
    public void Multiple_running_overlays_and_recording_software_are_suggestions()
    {
        var s = Clean() with
        {
            Processes = [.. Clean().Processes, Process("Discord", 400), Process("steam", 300), Process("NVIDIA Overlay", 200), Process("obs64", 500)],
        };

        var report = Analyze(s);

        Assert.Contains("3 programmi con overlay", Single(report, "overlays-active").Title);
        Assert.Equal(FindingSeverity.Suggestion, Single(report, "recording-active").Severity);
    }

    [Fact]
    public void Other_optimizers_and_risky_software_are_warnings()
    {
        var s = Clean() with
        {
            Processes = [.. Clean().Processes, Process("ProcessLasso", 50)],
            Services = [.. Clean().Services, Service("NahimicService", ServiceStartValues.Automatic, "Running", "A-Volute")],
        };

        var ids = Ids(Analyze(s));

        Assert.Contains("other-optimizers", ids);
        Assert.Contains("software-nahimic", ids);
    }

    [Fact]
    public void Vulnerable_driver_is_a_security_warning()
    {
        var s = Clean() with
        {
            KernelDrivers = [new KernelDriverEntry { Name = "WinRing0_1_2_0", FilePath = @"C:\Program Files\FanTool\WinRing0x64.sys", State = "Running" }],
        };

        var report = Analyze(s);

        Assert.Equal(["WinRing0x64.sys (Driver WinRing0, caricato)"], report.Software.VulnerableDrivers);
        var finding = Single(report, "vulnerable-driver-WinRing0x64.sys");
        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Contains("CVE-2020-14979", finding.Detail);
    }

    [Fact]
    public void Many_third_party_startup_items_are_a_suggestion()
    {
        var startup = Enumerable.Range(1, 10)
            .Select(i => new StartupEntry { Name = $"App{i}", Source = StartupSource.RegistryUser, Company = "Contoso", Enabled = true })
            .Append(new StartupEntry { Name = "Discord", Source = StartupSource.RegistryUser, Command = "Discord.exe", Company = "Discord Inc.", Enabled = true });

        var finding = Single(Analyze(Clean() with { Startup = [.. startup] }), "startup-many");

        Assert.Contains("Discord", finding.Detail);
    }

    [Fact]
    public void Background_memory_threshold_depends_on_tier()
    {
        var heavy = Clean() with { Processes = [.. Clean().Processes, Process("chrome", 3 * 1024)] };
        var light = Clean() with { Processes = [.. Clean().Processes, Process("chrome", 1200)] };

        Assert.Equal(FindingSeverity.Suggestion, Single(Analyze(heavy, Desktop(ramGb: 16)), "background-memory").Severity);
        Assert.DoesNotContain("background-memory", Ids(Analyze(light, Desktop(ramGb: 16))));
        Assert.Equal(FindingSeverity.Warning, Single(Analyze(light, Desktop(HardwareTier.Low, ramGb: 8)), "background-memory").Severity);
    }

    [Fact]
    public void Disabled_page_file_is_a_warning()
    {
        var s = Clean() with { PageFile = new PageFileSnapshot { AutomaticManaged = false, Files = [] } };

        Assert.Equal(FindingSeverity.Warning, Single(Analyze(s), "pagefile-disabled").Severity);
    }

    // --- Modifiche rispetto ai default ---

    [Fact]
    public void Modified_settings_are_listed_with_their_effect()
    {
        var s = Clean().WithRegistry(
            ("mmcss.networkthrottling", "4294967295"),
            ("memory.largesystemcache", "1"),
            ("placebo.irq8priority", "1"));

        var report = Analyze(s);

        Assert.Equal("4294967295 (0xFFFFFFFF)", report.ModifiedSettings.Single(m => m.Id == "mmcss.networkthrottling").Current);
        Assert.Equal(TweakEffect.Placebo, report.ModifiedSettings.Single(m => m.Id == "placebo.irq8priority").Effect);
        Assert.Equal(FindingSeverity.Warning, Single(report, "modified-harmful-memory.largesystemcache").Severity);
        Assert.Contains("Una di queste non ha alcun effetto", Single(report, "modified-settings").Detail);
    }

    [Fact]
    public void Standard_ui_values_of_processor_scheduling_are_not_modifications()
    {
        Assert.Empty(Analyze(Clean().WithRegistry(("scheduler.priorityseparation", "38"))).ModifiedSettings);
        Assert.Single(Analyze(Clean().WithRegistry(("scheduler.priorityseparation", "42"))).ModifiedSettings);
    }

    [Fact]
    public void Nagle_settings_per_interface_are_listed()
    {
        var s = Clean() with { TcpInterfaces = [new TcpInterfaceEntry("{1234}", 1, 1, null)] };

        Assert.Contains(Analyze(s).ModifiedSettings, m => m.Id == "network.nagle.{1234}" && m.Effect == TweakEffect.Debatable);
    }

    // --- Raccolta ---

    [Fact]
    public void Virtual_machine_analysis_is_flagged()
    {
        Assert.Contains("analysis-virtual-machine", Ids(Analyze(Clean(), Desktop() with { IsVirtualMachine = true })));
        Assert.DoesNotContain("analysis-virtual-machine", Ids(Analyze(Clean())));
    }

    [Fact]
    public void Single_modified_setting_uses_singular_wording() =>
        Assert.StartsWith("1 impostazione diversa",
            Single(Analyze(Clean().WithRegistry(("placebo.irq8priority", "1"))), "modified-settings").Title);

    [Fact]
    public void Missing_privileges_and_errors_are_reported_and_findings_are_sorted()
    {
        var s = (Clean() with { IsElevated = false, Errors = ["Attività pianificate: accesso negato"] })
            .WithPower(PowerSettingsCatalog.BoostMode, 0)
            .WithRegistry((RegistryProbes.GameMode.Id, "0"));

        var report = Analyze(s);

        Assert.Contains("analysis-not-elevated", Ids(report));
        Assert.Contains("analysis-partial", Ids(report));
        var severities = report.Findings.Select(f => (int)f.Severity).ToList();
        Assert.Equal(severities.OrderByDescending(x => x), severities);
    }
}
