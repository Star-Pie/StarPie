using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Xml.Linq;
using WinPieGestures;
using WinPieGestures.ArtStyles;

internal static class Program
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static int _checks;
    private static readonly string[] ThemeBrushKeys =
    [
        "WindowBackgroundBrush", "SidebarBackgroundBrush", "CardBackgroundBrush", "CardBorderBrush",
        "TextPrimaryBrush", "TextSecondaryBrush", "TextMutedBrush", "TextTertiaryBrush",
        "InputBackgroundBrush", "InputBorderBrush", "ItemHoverBrush", "SubtleCardBrush",
        "NavTabDefaultBgBrush", "NavTabDefaultFgBrush", "NavTabHoverBgBrush", "NavTabHoverFgBrush",
        "NavTabActiveBgBrush", "NavTabActiveFgBrush", "ButtonDefaultBgBrush", "ButtonDefaultFgBrush",
        "ButtonDefaultBorderBrush", "ButtonHoverBgBrush", "AccentPrimaryBrush", "AccentHoverBrush",
        "AccentTextBrush", "PreviewCanvasBackgroundBrush", "PreviewCanvasBorderBrush", "PreviewGridLineBrush"
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        bool baselineMode = args.Contains("--baseline-probe");
        try
        {
            if (!args.Contains("--test-instance"))
            {
                throw new InvalidOperationException("Use --test-instance; refuses to run without isolated sandbox.");
            }

            string sandbox = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
                ? Path.GetFullPath(args[0])
                : Path.Combine(Path.GetTempPath(), "StarPie-Stage1-Perf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            foreach (string varName in new[] { "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" })
            {
                Environment.SetEnvironmentVariable(varName, sandbox);
            }

            string candidateDir = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "StarPieCandidateDir").Value!;
            string expectedDll = Path.Combine(candidateDir, "StarPie.dll");
            string loadedDll = typeof(AppThemeManager).Assembly.Location;
            string expectedHash = Hash(expectedDll);
            string loadedHash = Hash(loadedDll);
            Check(expectedHash == loadedHash, "candidate DLL SHA-256 matches StarPieCandidateDir");
            Console.WriteLine($"CANDIDATE={candidateDir}; SHA256={expectedHash}");

            ConfigManager.DisableAutoStartSync = true;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources = LoadHostAppResources();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

            var config = BuildTestConfig();
            SetCurrentConfig(config);

            // Measure cold RadialWindow phases FIRST before SettingsWindow initializes shared WPF/D3D paths.
            var radialMetrics = RunRadialWindowBenchmarksAndChecks(baselineMode);
            var themeMetrics = RunThemeSwitchBenchmarksAndIntegrity(baselineMode);

            Console.WriteLine("PERF_METRICS " + JsonSerializer.Serialize(new
            {
                candidateDir,
                sha256 = expectedHash,
                baselineMode,
                theme = themeMetrics,
                radial = radialMetrics
            }));

            AppLogger.Shutdown();
            Console.WriteLine($"PASS {_checks} stage1 perf & integrity checks (isolated sandbox, zero user config writes).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL {_checks}: {ex}");
            return 1;
        }
    }

    private static object RunThemeSwitchBenchmarksAndIntegrity(bool baselineMode)
    {
        // 1. Bare FrameworkElement & PluginSelfTest contract
        var dummy = new FrameworkElement();
        AppThemeManager.ApplyTheme(dummy, "Light");
        Check(dummy.Resources["CardBorderBrush"] is SolidColorBrush { IsFrozen: true }, "dummy Light CardBorderBrush frozen");
        AppThemeManager.ApplyTheme(dummy, "Dark");
        Check(dummy.Resources["CardBorderBrush"] is SolidColorBrush bDark && bDark.Color == Color.FromRgb(0x1E, 0x29, 0x3B), "dummy Dark CardBorderBrush updated");

        // 2. Real SettingsWindow offscreen visual tree (Measure/Arrange/UpdateLayout expands templates & DynamicResource listeners without visible popup)
        var settings = new SettingsWindow();
        typeof(SettingsWindow).GetField("_isOnboardingPromptActive", PrivateInstance)!.SetValue(settings, true);
        try
        {
            var settingsContent = (FrameworkElement)settings.Content;
            settingsContent.Measure(new Size(1220, 740));
            settingsContent.Arrange(new Rect(0, 0, 1220, 740));
            settingsContent.UpdateLayout();

            // Verify XAML-declared local brushes in SettingsWindow/KeyMapEditorWindow/TextResultWindow do NOT shadow Dark theme
            AppThemeManager.ApplyTheme(settings, "Dark");
            if (!baselineMode)
            {
                VerifyAllThemeKeys(settings, Color.FromRgb(0x09, 0x0D, 0x16), "SettingsWindow Dark after XAML cleanup");
            }
            else
            {
                Check(settings.Resources["WindowBackgroundBrush"] is SolidColorBrush sb && sb.Color == Color.FromRgb(0x09, 0x0D, 0x16), "SettingsWindow Dark WindowBackgroundBrush");
            }

            var keyMap = new KeyMapEditorWindow("KeyQ=Numpad7");
            AppThemeManager.ApplyTheme(keyMap, "Dark");
            VerifyWindowBrush(keyMap, "WindowBackgroundBrush", Color.FromRgb(0x09, 0x0D, 0x16), "KeyMapEditorWindow Dark");
            if (!baselineMode)
            {
                Check(keyMap.TryFindResource("BorderSubtleBrush") is SolidColorBrush, "KeyMapEditorWindow preserves non-managed BorderSubtleBrush");
            }
            keyMap.Close();

            var textWin = new TextResultWindow("Sample", Array.Empty<StarPie.Plugin.TextResultItem>());
            AppThemeManager.ApplyTheme(textWin, "Dark");
            VerifyWindowBrush(textWin, "WindowBackgroundBrush", Color.FromRgb(0x09, 0x0D, 0x16), "TextResultWindow Dark");
            textWin.Close();

            // Benchmark: sequential 28-key brush assignment simulation vs AppThemeManager.ApplyTheme swaps
            var seqResult = MeasureSequentialBrushSimulation(settings);
            AppThemeManager.ApplyTheme(settings, "Light");
            settingsContent.UpdateLayout();
            PumpIdle();

            var lightToDark = MeasureAction(() =>
            {
                AppThemeManager.ApplyTheme(settings, "Dark");
                settingsContent.UpdateLayout();
            });
            VerifyWindowBrush(settings, "WindowBackgroundBrush", Color.FromRgb(0x09, 0x0D, 0x16), "Light->Dark");

            var darkToGray = MeasureAction(() =>
            {
                AppThemeManager.ApplyTheme(settings, "TitaniumGray");
                settingsContent.UpdateLayout();
            });
            VerifyWindowBrush(settings, "WindowBackgroundBrush", Color.FromRgb(0x12, 0x12, 0x14), "Dark->TitaniumGray");

            var grayToLight = MeasureAction(() =>
            {
                AppThemeManager.ApplyTheme(settings, "Light");
                settingsContent.UpdateLayout();
            });
            VerifyWindowBrush(settings, "WindowBackgroundBrush", Color.FromRgb(0xF8, 0xFA, 0xFC), "TitaniumGray->Light");

            // ArtStyle roundtrip: Light -> pixel -> obsidian -> Light
            ConfigManager.CurrentConfig.SelectedArtStyleId = "pixel";
            var lightToPixel = MeasureAction(() =>
            {
                AppThemeManager.ApplyTheme(settings, "Light");
                settingsContent.UpdateLayout();
            });
            VerifyWindowBrush(settings, "WindowBackgroundBrush", Color.FromRgb(0xF2, 0xE9, 0xCD), "Light->pixel");
            Check(settings.TryFindResource("CardCornerRadius") is CornerRadius { TopLeft: 0 }, "pixel CardCornerRadius=0");
            Check(settings.TryFindResource("ArtBorderThickness") is Thickness { Left: 2.5 }, "pixel ArtBorderThickness=2.5");
            Check(settings.TryFindResource("CardShadowEffect") is DropShadowEffect { BlurRadius: 0 }, "pixel CardShadowEffect BlurRadius=0");

            ConfigManager.CurrentConfig.SelectedArtStyleId = "obsidian";
            var pixelToObsidian = MeasureAction(() =>
            {
                AppThemeManager.ApplyTheme(settings, "Light");
                settingsContent.UpdateLayout();
            });
            VerifyWindowBrush(settings, "WindowBackgroundBrush", Color.FromRgb(0x11, 0x17, 0x1B), "pixel->obsidian");

            ConfigManager.CurrentConfig.SelectedArtStyleId = "";
            var obsidianToLight = MeasureAction(() =>
            {
                AppThemeManager.ApplyTheme(settings, "Light");
                settingsContent.UpdateLayout();
            });
            VerifyWindowBrush(settings, "WindowBackgroundBrush", Color.FromRgb(0xF8, 0xFA, 0xFC), "obsidian->Light");
            VerifyLegacyMetricsRestored(settings);

            // Repeated identical ApplyTheme (simulates ArtStyleService.NotifyChanged 2nd call)
            var repeatSameTheme = MeasureAction(() =>
            {
                AppThemeManager.ApplyTheme(settings, "Light");
                settingsContent.UpdateLayout();
            });

            // 10-cycle average Standard <-> Standard
            var tenSwitchesSw = Stopwatch.StartNew();
            long allocStart = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++)
            {
                AppThemeManager.ApplyTheme(settings, (i % 2 == 0) ? "Dark" : "Light");
                settingsContent.UpdateLayout();
            }
            tenSwitchesSw.Stop();
            long tenSwitchesAlloc = GC.GetAllocatedBytesForCurrentThread() - allocStart;

            // ArtStyleResources.ApplyPreview must NOT leak to Application.Current.Resources
            var appAccentBefore = ((SolidColorBrush)Application.Current.Resources["AccentPrimaryBrush"]).Color;
            var previewHost = new Border();
            ArtStyleResources.ApplyPreview(previewHost, ArtStyleCatalog.Find(ConfigManager.CurrentConfig, "pixel")!);
            Check(((SolidColorBrush)previewHost.Resources["WindowBackgroundBrush"]).Color == Color.FromRgb(0xF2, 0xE9, 0xCD), "ApplyPreview sets previewHost");
            Check(((SolidColorBrush)Application.Current.Resources["AccentPrimaryBrush"]).Color == appAccentBefore, "ApplyPreview does not mutate Application.Current.Resources");

            VerifyTypographyAndHostWindowFrame();

            return new
            {
                sequential28KeysMs = Round(seqResult.ms),
                sequential28KeysAllocBytes = seqResult.allocBytes,
                lightToDarkMs = Round(lightToDark.ms),
                lightToDarkAllocBytes = lightToDark.allocBytes,
                darkToGrayMs = Round(darkToGray.ms),
                grayToLightMs = Round(grayToLight.ms),
                lightToPixelMs = Round(lightToPixel.ms),
                pixelToObsidianMs = Round(pixelToObsidian.ms),
                obsidianToLightMs = Round(obsidianToLight.ms),
                repeatSameThemeMs = Round(repeatSameTheme.ms),
                repeatSameThemeAllocBytes = repeatSameTheme.allocBytes,
                tenStandardSwitchesTotalMs = Round(tenSwitchesSw.Elapsed.TotalMilliseconds),
                tenStandardSwitchesAvgMs = Round(tenSwitchesSw.Elapsed.TotalMilliseconds / 10.0),
                tenStandardSwitchesAllocBytes = tenSwitchesAlloc
            };
        }
        finally
        {
            typeof(SettingsWindow).GetField("_isClosingForRelease", PrivateInstance)!.SetValue(settings, true);
            typeof(SettingsWindow).GetField("_resourcesReleased", PrivateInstance)!.SetValue(settings, true);
            settings.Close();
            PumpIdle();
        }
    }

    private static object RunRadialWindowBenchmarksAndChecks(bool baselineMode)
    {
        var config = ConfigManager.CurrentConfig;
        config.EnableEdgeCollisionAvoidance = false;
        config.EnableMultiTier = true;
        config.SubmenuStyle = "Wheel";
        config.AutoExpandSubRingsOnPopup = true;
        config.ShowCoreCircle = true;
        config.ShowText = true;
        config.IconLayoutMode = "IconAndText";
        var profile = config.Profiles[0];
        var offscreenCenter = new Point(-30000, -30000);

        // Phase 1: Real end-to-end COLD first-popup via Present() -> DispatcherPriority.Render reveal (offscreen at -30000,-30000)
        var coldFirstPopup = MeasureColdPresentCycle(offscreenCenter, profile, ConfigManager.ConfigurationRevision, 101L, out var coldWindow);
        double warmPresentEndToEndMs;
        double warmPresentCallMs;
        double warmPresentRenderRevealMs;
        double warmPresent2EndToEndMs;
        try
        {
            if (!baselineMode)
            {
                var subPaths = (IList)typeof(RadialWindow).GetField("_subSectorPaths", PrivateInstance)!.GetValue(coldWindow)!;
                Check(subPaths.Count > 0, $"cold first Present preserves AutoExpandSubRingsOnPopup sub-sectors (count={subPaths.Count})");
            }
            coldWindow.Dismiss(101L);

            var warmSameRev = MeasureWarmPresentCycle(coldWindow, offscreenCenter, profile, ConfigManager.ConfigurationRevision, 102L);
            warmPresentEndToEndMs = warmSameRev.totalMs;
            warmPresentCallMs = warmSameRev.presentMs;
            warmPresentRenderRevealMs = warmSameRev.revealMs;
            coldWindow.Dismiss(102L);

            var warmSameRev2 = MeasureWarmPresentCycle(coldWindow, offscreenCenter, profile, ConfigManager.ConfigurationRevision, 103L);
            warmPresent2EndToEndMs = warmSameRev2.totalMs;
            coldWindow.Dismiss(103L);
        }
        finally
        {
            coldWindow.CloseFast();
            PumpIdle();
        }

        // Phase 2: Detailed phase-by-phase breakdown on a fresh RadialWindow instance
        var ctorMetric = MeasureActionWithResult(() => new RadialWindow(offscreenCenter, profile));
        var rw = ctorMetric.result;
        try
        {
            int unfrozenResources = CountUnfrozenResourceFreezables(rw);
            int unfrozenRendererBrushes = CountUnfrozenRendererBrushes(rw);

            var initStyleMetric = MeasureAction(() => Invoke(rw, "InitializeThemeAndStyle"));
            var rebuildColdMetric = MeasureAction(() => Invoke(rw, "RebuildVisualsFromCurrentConfiguration", ConfigManager.ConfigurationRevision));
            var subTiersColdMetric = MeasureAction(() => Invoke(rw, "ShowAllSubTiers"));
            var layoutColdMetric = MeasureAction(() =>
            {
                rw.Measure(new Size(rw.Width, rw.Height));
                rw.Arrange(new Rect(0, 0, rw.Width, rw.Height));
                rw.UpdateLayout();
            });

            var svgParseMetric = MeasureAction(() =>
            {
                for (int i = 0; i < 16; i++)
                {
                    _ = IconHelper.GetCoreIconGeometry("Crosshair");
                    _ = Geometry.Parse(IconHelper.GetSvgPathByKey("Copy")!);
                }
            });

            var rebuildWarmMetric = MeasureAction(() => Invoke(rw, "RebuildVisualsFromCurrentConfiguration", ConfigManager.ConfigurationRevision));
            var highlightMetric = MeasureAction(() =>
            {
                for (int s = 0; s < 8; s++) rw.HighlightSector(s, -1, showSubTier: false);
                rw.HighlightSector(-1, -1, showSubTier: false);
                for (int s = 0; s < 8; s++) rw.HighlightSector(s, -1, showSubTier: false);
                rw.HighlightSector(-1, -1, showSubTier: false);
            });

            int unfrozenSectorGeo = CountUnfrozenGeometries(rw, "_sectorPaths");
            int unfrozenSubGeo = CountUnfrozenGeometries(rw, "_subSectorPaths");
            int unfrozenIconGeo = CountUnfrozenElementGeometries(rw, "_contentIconElements");
            if (!baselineMode)
            {
                Check(unfrozenResources == 0, $"RadialWindow XAML resources/decorations frozen (unfrozen={unfrozenResources})");
                Check(unfrozenRendererBrushes == 0, $"RadialWindow style brushes frozen (unfrozen={unfrozenRendererBrushes})");
                Check(unfrozenSectorGeo == 0, $"RadialWindow sector geometries frozen (unfrozen={unfrozenSectorGeo})");
                Check(unfrozenSubGeo == 0, $"RadialWindow sub-sector geometries frozen (unfrozen={unfrozenSubGeo})");
                Check(unfrozenIconGeo == 0, $"RadialWindow sector icon geometries frozen (unfrozen={unfrozenIconGeo})");
            }

            return new
            {
                coldPresentEndToEndMs = Round(coldFirstPopup.totalMs),
                coldPresentCtorMs = Round(coldFirstPopup.ctorMs),
                coldPresentCallMs = Round(coldFirstPopup.presentMs),
                coldPresentRenderRevealMs = Round(coldFirstPopup.revealMs),
                warmPresentEndToEndMs = Round(warmPresentEndToEndMs),
                warmPresentCallMs = Round(warmPresentCallMs),
                warmPresentRenderRevealMs = Round(warmPresentRenderRevealMs),
                warmPresent2EndToEndMs = Round(warmPresent2EndToEndMs),
                phaseCtorMs = Round(ctorMetric.ms),
                phaseCtorAllocBytes = ctorMetric.allocBytes,
                phaseInitStyleMs = Round(initStyleMetric.ms),
                phaseRebuildMs = Round(rebuildColdMetric.ms),
                phaseRebuildAllocBytes = rebuildColdMetric.allocBytes,
                phaseShowAllSubTiersMs = Round(subTiersColdMetric.ms),
                phaseLayoutMs = Round(layoutColdMetric.ms),
                phaseSvgParse32Ms = Round(svgParseMetric.ms),
                phaseWarmRebuildMs = Round(rebuildWarmMetric.ms),
                phaseHighlight18Ms = Round(highlightMetric.ms),
                unfrozenResources,
                unfrozenRendererBrushes,
                unfrozenSectorGeo,
                unfrozenSubGeo
            };
        }
        finally
        {
            rw.CloseFast();
            PumpIdle();
        }
    }

    private static (double totalMs, double ctorMs, double presentMs, double revealMs) MeasureColdPresentCycle(
        Point center, WheelProfile profile, long rev, long version, out RadialWindow window)
    {
        var totalSw = Stopwatch.StartNew();
        var ctorSw = Stopwatch.StartNew();
        window = new RadialWindow(center, profile)
        {
            ShowActivated = false,
            ShowInTaskbar = false
        };
        ctorSw.Stop();

        bool revealed = false;
        var revealSw = Stopwatch.StartNew();
        var presentSw = Stopwatch.StartNew();
        window.Present(center, profile, rev, version, () =>
        {
            revealed = true;
            revealSw.Stop();
        });
        presentSw.Stop();

        var waitSw = Stopwatch.StartNew();
        while (!revealed && waitSw.ElapsedMilliseconds < 2000)
        {
            PumpOneRenderFrame();
        }
        totalSw.Stop();
        Check(revealed, "cold Present completed render reveal");
        return (totalSw.Elapsed.TotalMilliseconds, ctorSw.Elapsed.TotalMilliseconds, presentSw.Elapsed.TotalMilliseconds, revealSw.Elapsed.TotalMilliseconds);
    }

    private static (double totalMs, double presentMs, double revealMs) MeasureWarmPresentCycle(
        RadialWindow window, Point center, WheelProfile profile, long rev, long version)
    {
        var totalSw = Stopwatch.StartNew();
        bool revealed = false;
        var revealSw = Stopwatch.StartNew();
        var presentSw = Stopwatch.StartNew();
        window.Present(center, profile, rev, version, () =>
        {
            revealed = true;
            revealSw.Stop();
        });
        presentSw.Stop();

        var waitSw = Stopwatch.StartNew();
        while (!revealed && waitSw.ElapsedMilliseconds < 2000)
        {
            PumpOneRenderFrame();
        }
        totalSw.Stop();
        Check(revealed, "warm Present completed render reveal");
        return (totalSw.Elapsed.TotalMilliseconds, presentSw.Elapsed.TotalMilliseconds, revealSw.Elapsed.TotalMilliseconds);
    }

    private static int CountUnfrozenResourceFreezables(RadialWindow rw)
    {
        int unfrozen = 0;
        foreach (DictionaryEntry entry in rw.Resources)
        {
            if (entry.Value is Freezable f && !f.IsFrozen) unfrozen++;
        }
        if (rw.FindName("OuterEllipse") is System.Windows.Shapes.Ellipse outer && outer.Stroke is Freezable os && !os.IsFrozen) unfrozen++;
        if (rw.FindName("CoreSelectionOverlay") is FrameworkElement cso && cso.Effect is Freezable csoe && !csoe.IsFrozen) unfrozen++;
        if (rw.FindName("LayerIndicatorBadge") is FrameworkElement lib && lib.Effect is Freezable libe && !libe.IsFrozen) unfrozen++;
        return unfrozen;
    }

    private static int CountUnfrozenRendererBrushes(RadialWindow rw)
    {
        int unfrozen = 0;
        string[] fields =
        [
            "_defaultSectorBrush", "_highlightSectorBrush", "_sectorBorderBrush", "_highlightBorderBrush",
            "_textColorBrush", "_coreBgBrush", "_coreBorderBrush",
            "_subDefaultSectorBrush", "_subHighlightSectorBrush", "_subSectorBorderBrush",
            "_subHighlightBorderBrush", "_subTextColorBrush"
        ];
        foreach (string name in fields)
        {
            var val = typeof(RadialWindow).GetField(name, PrivateInstance)?.GetValue(rw);
            if (val is Freezable f && !f.IsFrozen) unfrozen++;
        }
        return unfrozen;
    }

    private static int CountUnfrozenGeometries(RadialWindow rw, string fieldName)
    {
        int unfrozen = 0;
        var list = (IList)typeof(RadialWindow).GetField(fieldName, PrivateInstance)!.GetValue(rw)!;
        foreach (System.Windows.Shapes.Path p in list)
        {
            if (p.Data != null && p.Data.CanFreeze && !p.Data.IsFrozen) unfrozen++;
        }
        return unfrozen;
    }

    private static int CountUnfrozenElementGeometries(RadialWindow rw, string fieldName)
    {
        int unfrozen = 0;
        var list = (IList)typeof(RadialWindow).GetField(fieldName, PrivateInstance)!.GetValue(rw)!;
        foreach (object? item in list)
        {
            if (item is System.Windows.Shapes.Path p && p.Data != null && p.Data.CanFreeze && !p.Data.IsFrozen) unfrozen++;
        }
        return unfrozen;
    }

    private static (double ms, long allocBytes) MeasureSequentialBrushSimulation(SettingsWindow settings)
    {
        var content = (FrameworkElement)settings.Content;
        var colors = new[]
        {
            "#090D16", "#0F172A", "#131C2E", "#1E293B", "#F8FAFC", "#CBD5E1", "#94A3B8",
            "#0B1120", "#334155", "#1E293B", "#0F172A", "#00000000", "#CBD5E1", "#1E293B",
            "#F8FAFC", "#1E293B", "#60A5FA", "#1E293B", "#F8FAFC", "#334155", "#334155",
            "#3B82F6", "#60A5FA", "#FFFFFF", "#0B1120", "#1E293B", "#1E293B", "#94A3B8"
        };
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < ThemeBrushKeys.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
            brush.Freeze();
            settings.Resources[ThemeBrushKeys[i]] = brush;
            Application.Current.Resources[ThemeBrushKeys[i]] = brush;
        }
        content.UpdateLayout();
        sw.Stop();
        long alloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        foreach (string k in ThemeBrushKeys)
        {
            settings.Resources.Remove(k);
            Application.Current.Resources.Remove(k);
        }
        return (sw.Elapsed.TotalMilliseconds, alloc);
    }

    private static void VerifyAllThemeKeys(FrameworkElement root, Color expectedWindowBg, string label)
    {
        foreach (string key in ThemeBrushKeys)
        {
            Check(root.Resources[key] is SolidColorBrush b1 && b1.IsFrozen, $"{label} root.Resources[{key}] frozen");
            Check(root.TryFindResource(key) is SolidColorBrush b2 && b2.IsFrozen, $"{label} root.TryFindResource({key}) frozen");
            Check(Application.Current.Resources[key] is SolidColorBrush b3 && b3.IsFrozen, $"{label} App.Resources[{key}] frozen");
        }
        VerifyWindowBrush(root, "WindowBackgroundBrush", expectedWindowBg, label);
    }

    private static void VerifyWindowBrush(FrameworkElement root, string key, Color expected, string label)
    {
        Check(root.Resources[key] is SolidColorBrush rb && rb.Color == expected, $"{label} root.Resources[{key}] == {expected} (got {(root.Resources[key] as SolidColorBrush)?.Color})");
        Check(root.TryFindResource(key) is SolidColorBrush fb && fb.Color == expected, $"{label} TryFindResource({key}) == {expected}");
    }

    private static void VerifyLegacyMetricsRestored(FrameworkElement root)
    {
        Check(root.TryFindResource("CardCornerRadius") is CornerRadius { TopLeft: 12 }, "legacy CardCornerRadius restored to 12");
        Check(root.TryFindResource("ControlCornerRadius") is CornerRadius { TopLeft: 6 }, "legacy ControlCornerRadius restored to 6");
        Check(root.TryFindResource("ArtBorderThickness") is Thickness { Left: 1 }, "legacy ArtBorderThickness restored to 1");
        Check(root.TryFindResource("CardShadowEffect") is DropShadowEffect { BlurRadius: 12, ShadowDepth: 1, Opacity: 0.03 }, "legacy CardShadowEffect restored");
        Check(root.TryFindResource("ChipShadowSoftEffect") is DropShadowEffect { BlurRadius: 8, ShadowDepth: 1, Opacity: 0.16 }, "legacy ChipShadowSoftEffect restored");
        Check(root.TryFindResource("ChipShadowEffect") is DropShadowEffect { BlurRadius: 8, ShadowDepth: 1, Opacity: 0.12 }, "legacy ChipShadowEffect restored");
    }

    private static void VerifyTypographyAndHostWindowFrame()
    {
        var w = new Window { Title = "FrameCheck", Width = 420, Height = 180, Content = new TextBlock { Text = "Hello" } };
        try
        {
            ConfigManager.CurrentConfig.UiFontFamily = "System";
            AppThemeManager.ApplyTheme(w, "Light");
            var firstContent = w.Content;
            AppThemeManager.ApplyTheme(w, "Dark");
            Check(ReferenceEquals(firstContent, w.Content), "HostWindowFrame does not re-wrap Window content");
            Check(w.Resources["WindowBackgroundBrush"] is SolidColorBrush sb && sb.Color == Color.FromRgb(0x09, 0x0D, 0x16), "HostWindowFrame Window got Dark brush");

            ConfigManager.CurrentConfig.UiFontFamily = "Consolas";
            AppThemeManager.ApplyTheme(w, "Dark");
            Check(w.FontFamily.Source == "Consolas", "UiTypography named font Consolas applied");

            ConfigManager.CurrentConfig.UiFontFamily = "Theme";
            ConfigManager.CurrentConfig.SelectedArtStyleId = "pixel";
            AppThemeManager.ApplyTheme(w, "Light");
            Check(w.FontFamily.Source.Contains("Cascadia Mono", StringComparison.OrdinalIgnoreCase), "UiTypography Theme font tracks pixel ArtStyle");

            ConfigManager.CurrentConfig.SelectedArtStyleId = "";
            ConfigManager.CurrentConfig.UiFontFamily = "System";
            AppThemeManager.ApplyTheme(w, "Light");
            Check(w.FontFamily.Source == SystemFonts.MessageFontFamily.Source, "UiTypography returns cleanly to System MessageFontFamily");

            // Direct ArtStyleResources.Apply + ResetMetrics compatibility (preserve brushes, reset 7 metrics, invalidate UiTypography)
            var obsidian = ArtStyleCatalog.Find(ConfigManager.CurrentConfig, "obsidian")!;
            ArtStyleResources.Apply(w, obsidian);
            ArtStyleResources.ResetMetrics(w);
            VerifyWindowBrush(w, "WindowBackgroundBrush", Color.FromRgb(0x11, 0x17, 0x1B), "ArtStyleResources.ResetMetrics preserves brushes");
            VerifyLegacyMetricsRestored(w);
            AppThemeManager.ApplyTheme(w, "Light");
            Check(w.FontFamily.Source == SystemFonts.MessageFontFamily.Source, "UiTypography recovers System MessageFontFamily after direct ArtStyleResources call");
        }
        finally
        {
            w.Close();
        }
    }

    private static AppConfig BuildTestConfig()
    {
        var actions = Enumerable.Range(0, 8).Select(i => new ActionItem
        {
            Name = "Action " + i,
            Type = i == 0 ? "Folder" : "Hotkey",
            IconKey = i switch { 0 => "Folder", 1 => "Copy", 2 => "Paste", 3 => "Cut", 4 => "Undo", 5 => "Redo", 6 => "Save", _ => "Search" },
            Parameter = "CTRL+C",
            SubActions = i < 4
                ? Enumerable.Range(0, 3).Select(j => new ActionItem
                {
                    Name = $"Sub {i}.{j}",
                    Type = "Hotkey",
                    IconKey = j == 0 ? "Copy" : j == 1 ? "Paste" : "Search",
                    Parameter = "CTRL+V"
                }).ToList()
                : new List<ActionItem>()
        }).ToList();

        var layer0 = new WheelLayer { Name = "Layer0", SectorCount = 8, Actions = actions };
        var layer1 = new WheelLayer
        {
            Name = "Layer1",
            SectorCount = 12,
            Actions = Enumerable.Range(0, 12).Select(i => new ActionItem { Name = "L2-" + i, Type = "Command", IconKey = "Terminal", Parameter = "echo" }).ToList()
        };
        var profile = new WheelProfile { ProcessName = "Global", SectorCount = 8, Layers = [layer0, layer1], ActiveLayerIndex = 0, Actions = actions };
        return new AppConfig
        {
            AutoCheckUpdate = false,
            AppTheme = "Light",
            Theme = "Dark",
            UiStyle = "ClassicRing",
            Profiles = [profile]
        };
    }

    private static (double ms, long allocBytes) MeasureAction(Action action)
    {
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        return (sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocBefore);
    }

    private static (T result, double ms, long allocBytes) MeasureActionWithResult<T>(Func<T> func)
    {
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        T res = func();
        sw.Stop();
        return (res, sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocBefore);
    }

    private static void PumpOneRenderFrame()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void PumpIdle()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static double Round(double ms) => Math.Round(ms, 2);

    private static object? Invoke(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, AllInstance)!.Invoke(target, args);

    private static void SetCurrentConfig(AppConfig config) =>
        typeof(ConfigManager).GetProperty("CurrentConfig")!.SetValue(null, config);

    private static ResourceDictionary LoadHostAppResources()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "HostApp.xaml");
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source = XDocument.Load(path).Root!.Element(wpf + "Application.Resources")!.Element(wpf + "ResourceDictionary")!;
        source.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
        source.SetAttributeValue(XNamespace.Xmlns + "local", "clr-namespace:WinPieGestures;assembly=StarPie");
        return (ResourceDictionary)XamlReader.Parse(source.ToString());
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Check(bool condition, string label)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException($"Check #{_checks} failed: {label}");
    }
}
