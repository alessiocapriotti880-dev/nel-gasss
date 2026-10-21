using PcOptimizer.Analysis.Catalog;
using PcOptimizer.Analysis.Model;
using PcOptimizer.Core.Model;
using PcOptimizer.Core.Settings;
using PcOptimizer.Core.Tweaks;
using PcOptimizer.Hardware.Model;
using PcOptimizer.Tweaks.Catalog;
using PcOptimizer.Tweaks.Context;
using PcOptimizer.Tweaks.Model;

namespace PcOptimizer.Tweaks.Tests;

public class AreaTests
{
    private static CatalogTweak Find(TweakContext c, string id) => TweakCatalog.Build(c).Single(t => t.Id == id);

    private static string? Reason(TweakContext c, string id)
    {
        Find(c, id).IsApplicable(c, out var reason);
        return reason;
    }

    private static SettingChange Single(TweakContext c, string id) => Assert.Single(Find(c, id).PlanChanges(c));

    // --- Alimentazione ---

    [Fact]
    public void PowerPlan_SwitchesToHighPerformanceOnDesktop()
    {
        var c = Fixtures.Context();
        var t = Find(c, PowerTweaks.PlanId);

        Assert.True(t.Recommended);
        Assert.Equal(Risk.Low, t.Risk);
        var change = Assert.Single(t.PlanChanges(c));
        Assert.IsType<ActivePowerSchemeTarget>(change.Target);
        Assert.Equal(PowerGuids.HighPerformance.ToString("D"), change.Desired.Data);
        Assert.Equal(0, t.Order);
    }

    [Fact]
    public void PowerPlan_UnavailableOnModernStandbyUsesOverlay()
    {
        var snapshot = Fixtures.Snapshot() with
        {
            Power = new PowerSnapshot { ActiveScheme = PowerGuids.Balanced, Schemes = [new(PowerGuids.Balanced, "Bilanciato")] },
        };
        var c = Fixtures.Context(snapshot: snapshot);

        Assert.NotNull(Reason(c, PowerTweaks.PlanId));
        Assert.Null(Reason(c, "power.overlay.best-performance"));
        Assert.True(Find(c, "power.overlay.best-performance").Recommended);
        Assert.Equal(PowerGuids.OverlayBestPerformance.ToString("D"), Single(c, "power.overlay.best-performance").Desired.Data);
    }

    [Fact]
    public void ProcessorSettings_FollowTheSchemeActiveAfterThePlanChange()
    {
        var c = Fixtures.Context(environment: Fixtures.Environment(scheme: PowerGuids.HighPerformance));
        var target = Assert.IsType<PowerSettingTarget>(Single(c, "latency.power.usb-suspend-off").Target);

        Assert.Equal(PowerGuids.HighPerformance, target.Scheme);
        Assert.Equal(PowerGuids.UsbSelectiveSuspend, target.Setting);
        Assert.Equal(PowerSource.Ac, target.Source);
        // Con il piano ad alte prestazioni l'overlay non esiste.
        Assert.NotNull(Reason(c, "power.overlay.best-performance"));
    }

    [Theory]
    [InlineData("AMD Ryzen 9 7950X3D 16-Core Processor", true)]
    [InlineData("AMD Ryzen 9 9900X3D 12-Core Processor", true)]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", false)]
    [InlineData("Intel(R) Core(TM) i9-14900K", false)]
    public void CoreParking_NotForDualCcdX3d(string cpu, bool blocked)
    {
        var c = Fixtures.Context(profile: Fixtures.Profile(cpu: cpu));
        Assert.Equal(blocked, Reason(c, "latency.power.core-parking-off") is not null);
    }

    [Fact]
    public void CStatesAndAspm_NotOfferedOnLaptops()
    {
        var c = Fixtures.Context(tier: HardwareTier.High, profile: Fixtures.Profile(laptop: true));

        Assert.NotNull(Reason(c, "latency.power.cstates-off"));
        Assert.NotNull(Reason(c, "latency.power.pcie-aspm-off"));
        Assert.Equal(Risk.Medium, Find(c, PowerTweaks.PlanId).Risk);
        Assert.False(Find(c, PowerTweaks.PlanId).Recommended);
        Assert.Equal(Risk.High, Find(c, "latency.power.cstates-off").Risk);
    }

    [Fact]
    public void DiskIdle_OnlyWithHdd()
    {
        Assert.NotNull(Reason(Fixtures.Context(), "storage.power.disk-idle-never"));
        var hdd = Fixtures.Context(profile: Fixtures.Profile(systemDisk: DiskMediaType.Hdd));
        Assert.Null(Reason(hdd, "storage.power.disk-idle-never"));
        Assert.True(Find(hdd, "storage.power.disk-idle-never").Recommended);
    }

    // --- Servizi, avvio, attività ---

    [Fact]
    public void WindowsServices_FollowDiskConditions()
    {
        var ssd = Fixtures.Context(tier: HardwareTier.Low);
        Assert.NotNull(Reason(ssd, "services.windows.wsearch"));
        Assert.Null(Reason(ssd, "services.windows.sysmain"));
        Assert.True(Find(ssd, "services.windows.diagtrack").Recommended);
        Assert.Equal(ServiceStartValues.Disabled, Single(ssd, "services.windows.diagtrack").Desired.Data);

        var hdd = Fixtures.Context(tier: HardwareTier.Low, profile: Fixtures.Profile(systemDisk: DiskMediaType.Hdd));
        Assert.Null(Reason(hdd, "services.windows.wsearch"));
        Assert.True(Find(hdd, "services.windows.wsearch").Recommended);
        Assert.NotNull(Reason(hdd, "services.windows.sysmain"));

        // Servizio non installato.
        Assert.NotNull(Reason(ssd, "services.windows.fax"));
    }

    [Fact]
    public void ThirdPartyServices_HarmfulDisabledOthersManual()
    {
        var snapshot = Fixtures.Snapshot() with
        {
            Services =
            [
                Fixtures.Service("SmartByte Network Service x64", ServiceStartValues.Automatic),
                Fixtures.Service("LightingService", ServiceStartValues.Automatic, "ASUS Lighting"),
                Fixtures.Service("AdobeARMservice", ServiceStartValues.Automatic),
                Fixtures.Service("CorsairService", ServiceStartValues.Manual),
            ],
        };
        var tweaks = TweakCatalog.Build(Fixtures.Context(snapshot: snapshot)).Where(t => t.Id.StartsWith("services.thirdparty.", StringComparison.Ordinal)).ToList();

        Assert.Equal(3, tweaks.Count); // CorsairService è già manuale
        var smartByte = tweaks.Single(t => t.Title.Contains("SmartByte"));
        Assert.True(smartByte.Recommended);
        Assert.Equal(ServiceStartValues.Disabled, smartByte.PlanChanges(Fixtures.Context()).Single().Desired.Data);
        var rgb = tweaks.Single(t => t.Title.Contains("Armoury"));
        Assert.False(rgb.Recommended);
        Assert.NotNull(rgb.Caution);
        Assert.Equal(Risk.Medium, rgb.Risk);
        Assert.True(tweaks.Single(t => t.Title.Contains("Adobe")).Recommended);
    }

    [Fact]
    public void Startup_UsesTaskManagerFormat()
    {
        var snapshot = Fixtures.Snapshot() with
        {
            Startup =
            [
                new StartupEntry { Name = "Discord", Source = StartupSource.RegistryUser, Enabled = true, ApprovalName = "Discord" },
                new StartupEntry { Name = "Steam", Source = StartupSource.RegistryMachine32, Enabled = true, ApprovalName = "Steam" },
                new StartupEntry { Name = "MiaVPN", Source = StartupSource.StartupFolderUser, Enabled = true, ApprovalName = "MiaVPN.lnk" },
                new StartupEntry { Name = "SpotifyAB.SpotifyMusic", Source = StartupSource.StoreApp, Enabled = true, ApprovalName = @"SpotifyAB.SpotifyMusic_zpdnekdrzrea0\Spotify", StoreTaskState = 2 },
                new StartupEntry { Name = "Politica", Source = StartupSource.StoreApp, Enabled = true, ApprovalName = @"Pkg\Task", StoreTaskState = 4 },
                new StartupEntry { Name = "OneDriveSetup", Source = StartupSource.RegistryUser, Enabled = false, ApprovalName = "OneDriveSetup" },
                new StartupEntry { Name = "SecurityHealth", Source = StartupSource.RegistryMachine, Company = "Microsoft Corporation", Enabled = true, ApprovalName = "SecurityHealth" },
            ],
        };
        var c = Fixtures.Context(snapshot: snapshot);
        var tweaks = TweakCatalog.Build(c).Where(t => t.Group == TweakGroup.Startup).ToList();

        Assert.Equal(4, tweaks.Count);
        var discord = tweaks.Single(t => t.Title.Contains("Discord"));
        Assert.True(discord.Recommended);
        var change = discord.PlanChanges(c).Single();
        var target = Assert.IsType<RegistryValueTarget>(change.Target);
        Assert.Equal(RegHive.CurrentUser, target.Hive);
        Assert.EndsWith(@"StartupApproved\Run", target.KeyPath);
        Assert.Equal("030000000000000000000000", change.Desired.Data);

        var steam = (RegistryValueTarget)tweaks.Single(t => t.Title.Contains("Steam")).PlanChanges(c).Single().Target;
        Assert.EndsWith(@"StartupApproved\Run32", steam.KeyPath);
        Assert.Equal(RegHive.LocalMachine, steam.Hive);

        var vpn = tweaks.Single(t => t.Title.Contains("MiaVPN"));
        Assert.False(vpn.Recommended);
        Assert.Equal(Risk.Medium, vpn.Risk);
        Assert.Equal("MiaVPN.lnk", ((RegistryValueTarget)vpn.PlanChanges(c).Single().Target).ValueName);

        var store = tweaks.Single(t => t.Title.Contains("Spotify")).PlanChanges(c).Single();
        Assert.EndsWith(@"SpotifyAB.SpotifyMusic_zpdnekdrzrea0\Spotify", ((RegistryValueTarget)store.Target).KeyPath);
        Assert.Equal("1", store.Desired.Data);
    }

    [Fact]
    public void Tasks_TelemetryAndThirdPartyUpdatersOnly()
    {
        var snapshot = Fixtures.Snapshot() with
        {
            ScheduledTasks =
            [
                new ScheduledTaskEntry { Path = @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser", Enabled = true },
                new ScheduledTaskEntry { Path = @"\Adobe Acrobat Update Task", Enabled = true, Triggers = ["Accesso"] },
                new ScheduledTaskEntry { Path = @"\GoogleUpdaterTaskSystem156", Enabled = true },
            ],
        };
        var c = Fixtures.Context(snapshot: snapshot);
        var tasks = TweakCatalog.Build(c).Where(t => t.Group == TweakGroup.Tasks && t.IsApplicable(c, out _)).ToList();

        Assert.Equal(2, tasks.Count);
        Assert.Contains(tasks, t => t.Id == "tasks.windows.microsoft-compatibility-appraiser" && t.Recommended);
        var adobe = tasks.Single(t => t.Id.StartsWith("tasks.thirdparty.", StringComparison.Ordinal));
        Assert.Equal(new ScheduledTaskTarget(@"\Adobe Acrobat Update Task"), adobe.PlanChanges(c).Single().Target);
        Assert.Equal(ScheduledTaskValues.Disabled, adobe.PlanChanges(c).Single().Desired.Data);
    }

    // --- Gioco e GPU ---

    [Fact]
    public void Gaming_WindowedOptimizationsKeepsOtherFlags()
    {
        var c = Fixtures.Context();
        Assert.Equal("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", Single(c, "gaming.windowed-optimizations-on").Desired.Data);
        Assert.NotNull(Reason(Fixtures.Context(build: 19045), "gaming.windowed-optimizations-on"));
        Assert.Equal("SwapEffectUpgradeEnable=1;", GamingTweaks.WithFlag(null, "SwapEffectUpgradeEnable", "1"));
        Assert.Equal("A=1;SwapEffectUpgradeEnable=1;", GamingTweaks.WithFlag("SwapEffectUpgradeEnable=0;A=1", "SwapEffectUpgradeEnable", "1"));
    }

    [Fact]
    public void Gaming_BackgroundRecordingAndGameMode()
    {
        var c = Fixtures.Context();
        Assert.Null(Reason(c, "gaming.background-recording-off"));
        Assert.Equal(RegistryProbes.HistoricalCapture.Target, Single(c, "gaming.background-recording-off").Target);
        Assert.NotNull(Reason(c, "gaming.game-mode-on")); // già attiva

        var off = Fixtures.Snapshot() with
        {
            Registry = new Dictionary<string, SettingValue>(Fixtures.Snapshot().Registry)
            {
                [RegistryProbes.GameMode.Id] = SettingValue.Of(0u),
                [RegistryProbes.HistoricalCapture.Id] = SettingValue.Of(0u),
            },
        };
        var c2 = Fixtures.Context(snapshot: off);
        Assert.Null(Reason(c2, "gaming.game-mode-on"));
        Assert.NotNull(Reason(c2, "gaming.background-recording-off"));
    }

    [Fact]
    public void Hags_RequiresSupportAndIsNeverPreselected()
    {
        var c = Fixtures.Context();
        Assert.Null(Reason(c, "gaming.hags-on"));
        Assert.False(Find(c, "gaming.hags-on").Recommended);
        Assert.True(Find(c, "gaming.hags-on").RequiresReboot);
        Assert.Equal("2", Single(c, "gaming.hags-on").Desired.Data);

        var unsupported = Fixtures.Snapshot() with { GpuScheduling = [new GpuSchedulingEntry { AdapterName = "Old", HwSchSupported = false }] };
        Assert.NotNull(Reason(Fixtures.Context(snapshot: unsupported), "gaming.hags-on"));
    }

    [Fact]
    public void Display_MaxRefreshRate()
    {
        var c = Fixtures.Context();
        var change = Single(c, @"display.max-refresh.display1");
        Assert.Equal(new DisplayRefreshTarget(@"\\.\DISPLAY1"), change.Target);
        Assert.Equal("144", change.Desired.Data);

        var atMax = Fixtures.Environment() with { Displays = [new DisplayModes(@"\\.\DISPLAY1", 144, [60, 144])] };
        Assert.NotNull(Reason(Fixtures.Context(environment: atMax), "display.max-refresh.display1"));
    }

    [Fact]
    public void Msi_OnlyForLineBasedGpuNetworkAudio()
    {
        var snapshot = Fixtures.Snapshot() with
        {
            PciDevices =
            [
                Fixtures.Pci("GPU", Fixtures.DisplayClass, 16),
                Fixtures.Pci("NVMe", Fixtures.StorageClass, 17),
                Fixtures.Pci("Audio", Fixtures.MediaClass, 0xFFFFFFF0),
                Fixtures.Pci("LAN", Fixtures.NetClass, 18) with { MsiSupported = true },
            ],
        };
        var c = Fixtures.Context(snapshot: snapshot);
        var msi = TweakCatalog.Build(c).Where(t => t.Id.StartsWith("latency.msi.", StringComparison.Ordinal)).ToList();

        var gpu = Assert.Single(msi);
        Assert.True(gpu.Recommended);
        Assert.True(gpu.RequiresReboot);
        var target = (RegistryValueTarget)gpu.PlanChanges(c).Single().Target;
        Assert.Equal(@"SYSTEM\CurrentControlSet\Enum\" + snapshot.PciDevices[0].PnpDeviceId + @"\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties", target.KeyPath);
        Assert.Equal("MSISupported", target.ValueName);
    }

    [Fact]
    public void TimerGlobalRequests_Windows11Only()
    {
        Assert.NotNull(Reason(Fixtures.Context(build: 19045), "latency.timer.global-requests"));
        Assert.Null(Reason(Fixtures.Context(), "latency.timer.global-requests"));
    }

    // --- Rete ---

    [Fact]
    public void Network_UsesOnlyParametersTheDriverAccepts()
    {
        var c = Fixtures.Context();
        var eee = Find(c, "network.adapter.eee-off").PlanChanges(c);
        Assert.Equal(["*EEE", "EnableGreenEthernet"], eee.Select(x => ((RegistryValueTarget)x.Target).ValueName));
        Assert.All(eee, x => Assert.Equal(RegValueKind.String, ((RegistryValueTarget)x.Target).Kind));
        Assert.True(Find(c, "network.adapter.eee-off").Recommended);

        var buffers = Find(c, "network.adapter.buffers-max").PlanChanges(c);
        Assert.All(buffers, x => Assert.Equal("1024", x.Desired.Data));

        var power = Find(c, "network.adapter.power-saving-off").PlanChanges(c);
        Assert.Contains(power, x => ((RegistryValueTarget)x.Target).ValueName == "PnPCapabilities" && x.Desired.Data == "24");

        Assert.NotNull(Reason(c, "network.adapter.roaming-off"));

        var nagle = Find(c, "network.nagle-off").PlanChanges(c);
        Assert.All(nagle, x => Assert.EndsWith(@"Interfaces\{11111111-2222-3333-4444-555555555555}", ((RegistryValueTarget)x.Target).KeyPath));
    }

    [Fact]
    public void Network_NotApplicableWithoutAdapter()
    {
        var c = Fixtures.Context(environment: Fixtures.Environment() with { PrimaryAdapter = null });
        Assert.NotNull(Reason(c, "network.adapter.eee-off"));
        Assert.NotNull(Reason(c, "network.nagle-off"));
    }

    // --- Archiviazione e sicurezza ---

    [Fact]
    public void Trim_OnlyWhenDisabled()
    {
        Assert.NotNull(Reason(Fixtures.Context(), "storage.trim-on"));
        var env = Fixtures.Environment() with { Registry = new Dictionary<string, SettingValue> { [TweakProbes.DisableDeleteNotify] = SettingValue.Of(1u) } };
        var c = Fixtures.Context(environment: env);
        Assert.Null(Reason(c, "storage.trim-on"));
        Assert.Equal("0", Single(c, "storage.trim-on").Desired.Data);
    }

    [Fact]
    public void PageFile_RestoredWhenMissing()
    {
        Assert.NotNull(Reason(Fixtures.Context(), "storage.pagefile-system-managed"));
        var noPageFile = Fixtures.Snapshot() with { PageFile = new PageFileSnapshot { AutomaticManaged = false, Files = [] } };
        var c = Fixtures.Context(snapshot: noPageFile);
        Assert.Null(Reason(c, "storage.pagefile-system-managed"));
        Assert.Equal([@"?:\pagefile.sys"], Single(c, "storage.pagefile-system-managed").Desired.AsMulti());
    }

    [Fact]
    public void MemoryIntegrity_ExplicitAndBlockedWhenLockedOrManaged()
    {
        var running = Fixtures.Snapshot() with { Security = new SecuritySnapshot { VbsStatus = 2, SecurityServicesRunning = [2] } };
        var c = Fixtures.Context(snapshot: running);
        var t = Find(c, "security.memory-integrity-off");

        Assert.Null(Reason(c, t.Id));
        Assert.True(t.AffectsSecurity);
        Assert.False(t.Recommended);
        Assert.Equal(Risk.High, t.Risk);
        Assert.Contains("anti-cheat", t.SecurityWarning);

        var locked = Fixtures.Environment() with { Registry = new Dictionary<string, SettingValue> { [TweakProbes.HvciLocked] = SettingValue.Of(1u) } };
        Assert.NotNull(Reason(Fixtures.Context(snapshot: running, environment: locked), t.Id));
        Assert.NotNull(Reason(Fixtures.Context(), t.Id)); // non attiva
    }

    // --- Ripristino dei valori predefiniti ---

    [Fact]
    public void Defaults_RestoreModifiedSettings()
    {
        var snapshot = Fixtures.Snapshot() with
        {
            Registry = new Dictionary<string, SettingValue>(Fixtures.Snapshot().Registry)
            {
                ["memory.largesystemcache"] = SettingValue.Of(1u),
                ["mmcss.systemresponsiveness"] = SettingValue.Of(0u),
                ["placebo.irq8priority"] = SettingValue.Of(1u),
            },
            Timer = new TimerSnapshot { BcdReadable = true, BcdElements = new Dictionary<string, string> { ["useplatformclock"] = "Sì" } },
            TcpInterfaces = [new TcpInterfaceEntry("{AAAA}", 1, 1, null)],
        };
        var c = Fixtures.Context(snapshot: snapshot);

        var cache = Find(c, "defaults.memory.largesystemcache");
        Assert.True(cache.Recommended);
        Assert.False(Single(c, cache.Id).Desired.Exists);

        var responsiveness = Find(c, "defaults.mmcss.systemresponsiveness");
        Assert.False(responsiveness.Recommended);
        Assert.True(responsiveness.IsMeasurable);
        Assert.Equal("20", Single(c, responsiveness.Id).Desired.Data);

        Assert.False(Find(c, "defaults.placebo.irq8priority").IsMeasurable);

        var bcd = Find(c, "defaults.bcd.useplatformclock");
        Assert.True(bcd.Recommended);
        Assert.True(bcd.RequiresReboot);
        Assert.Equal(new BcdElementTarget("useplatformclock"), Single(c, bcd.Id).Target);

        var withBitLocker = Fixtures.Context(snapshot: snapshot, environment: Fixtures.Environment() with { BitLockerOnSystemDrive = true });
        Assert.NotNull(Reason(withBitLocker, bcd.Id));

        var nagle = Find(c, "defaults.network.nagle.{AAAA}").PlanChanges(c);
        Assert.Equal(3, nagle.Count);
        Assert.All(nagle, x => Assert.False(x.Desired.Exists));
        // Il tweak "in avanti" non si propone se il valore è già impostato da altri.
        Assert.NotNull(Reason(Fixtures.Context(snapshot: snapshot with { TcpInterfaces = [new TcpInterfaceEntry("{11111111-2222-3333-4444-555555555555}", 1, 1, null)] }), "network.nagle-off"));
    }

    // --- Giochi ---

    [Fact]
    public void Games_PerGameSettings()
    {
        var game = new GameEntry("Valorant", @"C:\Riot Games\VALORANT\live\ShooterGame\Binaries\Win64\VALORANT-Win64-Shipping.exe");
        var env = Fixtures.Environment() with
        {
            Registry = new Dictionary<string, SettingValue> { [GameTweaks.GameProbe(game)] = SettingValue.Of("~ HIGHDPIAWARE") },
        };
        var hybridLaptop = Fixtures.Profile(laptop: true, cpu: "Intel(R) Core(TM) i7-12700H", pCores: 6, eCores: 8,
            gpus: [new GpuInfo { Name = "Intel Iris Xe", Vendor = GpuVendor.Intel }, new GpuInfo { Name = "RTX 3060 Laptop", Vendor = GpuVendor.Nvidia, IsPhysicalPciDevice = true }]);
        var c = Fixtures.Context(profile: hybridLaptop, environment: env, games: new GameLibrary([game]), build: 19045);
        var slug = "valorant-win64-shipping-exe";

        var gpu = Find(c, $"games.{slug}.gpu-high-performance");
        Assert.True(gpu.Recommended);
        var gpuTarget = (RegistryValueTarget)Single(c, gpu.Id).Target;
        Assert.Equal(game.ExecutablePath, gpuTarget.ValueName);
        Assert.Equal("GpuPreference=2;", Single(c, gpu.Id).Desired.Data);

        Assert.Equal("~ HIGHDPIAWARE DISABLEDXMAXIMIZEDWINDOWEDMODE", Single(c, $"games.{slug}.fso-off").Desired.Data);

        var priority = (RegistryValueTarget)Single(c, $"games.{slug}.high-priority").Target;
        Assert.Equal(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\VALORANT-Win64-Shipping.exe\PerfOptions", priority.KeyPath);
        Assert.Equal("3", Single(c, $"games.{slug}.high-priority").Desired.Data);

        var script = Single(c, $"games.{slug}.p-cores-only");
        Assert.Equal(@"C:\Users\Prova\Desktop" + Path.DirectorySeparatorChar + "Valorant (solo P-core).cmd", ((FileContentTarget)script.Target).FilePath);
        Assert.Contains("start \"\" /affinity FFF \"C:\\Riot Games", script.Desired.Data);
        Assert.Equal(TweakEvidence.Situational, Find(c, $"games.{slug}.p-cores-only").Evidence);

        // Una sola GPU: nessuna preferenza da impostare; CPU non ibrida: nessuno script.
        var desktop = Fixtures.Context(games: new GameLibrary([game]));
        Assert.NotNull(Reason(desktop, $"games.{slug}.gpu-high-performance"));
        Assert.DoesNotContain(TweakCatalog.Build(desktop), t => t.Id.EndsWith("p-cores-only", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(8, true, 0xFFFFUL)]
    [InlineData(6, false, 0x3FUL)]
    [InlineData(0, true, 0UL)]
    [InlineData(32, true, ulong.MaxValue)]
    public void PerformanceCoreMask(int pCores, bool smt, ulong expected) =>
        Assert.Equal(expected, GameTweaks.PerformanceCoreMask(new CpuInfo { PerformanceCores = pCores, HasSmt = smt }));

    [Theory]
    [InlineData(null, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE")]
    [InlineData("~ RUNASADMIN", "~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE")]
    [InlineData("~ DISABLEDXMAXIMIZEDWINDOWEDMODE", "~ DISABLEDXMAXIMIZEDWINDOWEDMODE")]
    public void CompatibilityLayersAreMerged(string? current, string expected) =>
        Assert.Equal(expected, GameTweaks.WithLayer(current, GameTweaks.DisableFso));
}
