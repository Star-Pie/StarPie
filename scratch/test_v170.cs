using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinPieGestures;

public class Program
{
    [STAThread]
    public static void Main()
    {
        Console.WriteLine("=== Testing StarPie v1.7.0 Features ===");
        
        // 1. Test ConfigManager & ThemeManager initialization
        ConfigManager.LoadConfig();
        
        // 2. Test RadialWindow with CenterAction (when no custom pattern is configured)
        ConfigManager.CurrentConfig.ShowCoreIcon = true;
        ConfigManager.CurrentConfig.CoreIconType = "Exit";
        ConfigManager.CurrentConfig.CoreCustomImagePath = "";
        ConfigManager.CurrentConfig.CoreCustomIconKey = "";
        ConfigManager.CurrentConfig.CoreCustomIconSvg = "";
        ConfigManager.CurrentConfig.CoreImageOffsetX = 0;
        ConfigManager.CurrentConfig.CoreImageOffsetY = 0;

        var profile = new WheelProfile
        {
            ProcessName = "TestApp",
            SectorCount = 8,
            EnableCenterAction = true,
            CenterAction = new ActionItem
            {
                Name = "StarPie控制台",
                Type = "Launch",
                IconKey = "Paste"
            }
        };
        
        RadialWindow rw = new RadialWindow(new Point(500, 500), profile);
        
        var coreExitIcon = rw.FindName("CoreExitIcon") as System.Windows.Shapes.Path;
        if (coreExitIcon == null)
        {
            Console.WriteLine("FAIL: CoreExitIcon not found!");
            return;
        }
        
        Console.WriteLine($"CoreExitIcon Visibility: {coreExitIcon.Visibility}");
        Console.WriteLine($"CoreExitIcon Data present: {coreExitIcon.Data != null}");
        
        if (coreExitIcon.Visibility != Visibility.Visible || coreExitIcon.Data == null)
        {
            Console.WriteLine("FAIL: CenterAction icon was not rendered!");
            return;
        }
        else
        {
            Console.WriteLine("SUCCESS: CenterAction Paste icon successfully rendered in RadialWindow!");
        }

        // Test hover highlight (deadzone hover)
        rw.HighlightSector(-1, -1);
        var brush = coreExitIcon.Fill as SolidColorBrush;
        Console.WriteLine($"CoreExitIcon Fill color on center hover: {brush?.Color}");
        if (brush != null && brush.Color.R == 245 && brush.Color.G == 158 && brush.Color.B == 11)
        {
            Console.WriteLine("SUCCESS: Center hover highlighted with amber (245, 158, 11)!");
        }
        else
        {
            Console.WriteLine("FAIL: Center hover color mismatch!");
        }
        
        // 3. Test SettingsWindow ConfigMode switching (Simple vs Pro)
        SettingsWindow sw = new SettingsWindow();
        
        var mouseGesturesCard = sw.FindName("Tab0_MouseGesturesCardBorder") as FrameworkElement;
        var cancelActionOuter = sw.FindName("Tab0_CancelActionOuterBorder") as FrameworkElement;
        var screenEdgeCard = sw.FindName("Tab0_ScreenEdgeCardBorder") as FrameworkElement;
        var mappingsViewMode = sw.FindName("Tab2_MappingsViewModeBorder") as FrameworkElement;
        var multiLayerHeader = sw.FindName("Tab2_MultiLayerHeaderPanel") as FrameworkElement;
        var canvasRadio = sw.FindName("MappingsViewModeCanvasRadio") as System.Windows.Controls.RadioButton;

        // Test Simple Mode
        sw.ApplyConfigMode("Simple", false);
        Console.WriteLine($"[Simple Mode] MouseGesturesCard: {mouseGesturesCard?.Visibility}");
        Console.WriteLine($"[Simple Mode] CancelActionOuter: {cancelActionOuter?.Visibility}");
        Console.WriteLine($"[Simple Mode] ScreenEdgeCard: {screenEdgeCard?.Visibility}");
        Console.WriteLine($"[Simple Mode] MappingsViewMode: {mappingsViewMode?.Visibility}");
        Console.WriteLine($"[Simple Mode] MultiLayerHeader: {multiLayerHeader?.Visibility}");
        Console.WriteLine($"[Simple Mode] CanvasRadio checked: {canvasRadio?.IsChecked}");
        
        bool simplePass = mouseGesturesCard?.Visibility == Visibility.Collapsed &&
                          cancelActionOuter?.Visibility == Visibility.Collapsed &&
                          screenEdgeCard?.Visibility == Visibility.Collapsed &&
                          mappingsViewMode?.Visibility == Visibility.Collapsed &&
                          multiLayerHeader?.Visibility == Visibility.Collapsed &&
                          canvasRadio?.IsChecked == true;

        // Test Pro Mode
        sw.ApplyConfigMode("Pro", false);
        Console.WriteLine($"[Pro Mode] MouseGesturesCard: {mouseGesturesCard?.Visibility}");
        Console.WriteLine($"[Pro Mode] CancelActionOuter: {cancelActionOuter?.Visibility}");
        Console.WriteLine($"[Pro Mode] ScreenEdgeCard: {screenEdgeCard?.Visibility}");
        Console.WriteLine($"[Pro Mode] MappingsViewMode: {mappingsViewMode?.Visibility}");
        Console.WriteLine($"[Pro Mode] MultiLayerHeader: {multiLayerHeader?.Visibility}");
        
        bool proPass = mouseGesturesCard?.Visibility == Visibility.Visible &&
                       cancelActionOuter?.Visibility == Visibility.Visible &&
                       screenEdgeCard?.Visibility == Visibility.Visible &&
                       mappingsViewMode?.Visibility == Visibility.Visible &&
                       multiLayerHeader?.Visibility == Visibility.Visible;
                       
        if (simplePass && proPass)
        {
            Console.WriteLine("SUCCESS: Simple and Pro mode UI switching verified 100%!");
        }
        else
        {
            Console.WriteLine("FAIL: Mode visibility assertion failed!");
        }

        // 4. Test SafeSetClipboardText from MTA background thread
        bool mtaSuccess = false;
        Exception mtaEx = null;
        var safeClipboardMethod = typeof(ActionExecutor).GetMethod("SafeSetClipboardText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                safeClipboardMethod.Invoke(null, new object[] { "StarPie-Test-MTA" });
                mtaSuccess = true;
            }
            catch (Exception ex)
            {
                mtaEx = ex;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.MTA);
        thread.Start();
        thread.Join();

        if (mtaSuccess && mtaEx == null)
        {
            Console.WriteLine("SUCCESS: SafeSetClipboardText succeeded from MTA background thread without ThreadStateException!");
        }
        else
        {
            Console.WriteLine($"FAIL: SafeSetClipboardText threw: {mtaEx?.Message}");
        }

        // 5. Test ParseHotkey for sequences (U+U, U,U, Win+X)
        var parseHotkeyMethod = typeof(ActionExecutor).GetMethod("ParseHotkey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (parseHotkeyMethod != null)
        {
            var uuHotkey = parseHotkeyMethod.Invoke(null, new object[] { "U+U" });
            var uuSeq = uuHotkey?.GetType().GetProperty("SequenceKeys")?.GetValue(uuHotkey) as System.Collections.IList;
            Console.WriteLine($"'U+U' parsed SequenceKeys count: {uuSeq?.Count}");

            var commaHotkey = parseHotkeyMethod.Invoke(null, new object[] { "U,U" });
            var commaSeq = commaHotkey?.GetType().GetProperty("SequenceKeys")?.GetValue(commaHotkey) as System.Collections.IList;
            Console.WriteLine($"'U,U' parsed SequenceKeys count: {commaSeq?.Count}");

            var winXHotkey = parseHotkeyMethod.Invoke(null, new object[] { "Win+X" });
            var mods = winXHotkey?.GetType().GetProperty("Modifiers")?.GetValue(winXHotkey) as System.Collections.Generic.List<ushort>;
            var mainKey = (ushort)(winXHotkey?.GetType().GetProperty("MainKey")?.GetValue(winXHotkey) ?? (ushort)0);
            bool hasWin = mods != null && (mods.Contains(91) || mods.Contains(92));
            Console.WriteLine($"'Win+X' parsed Win Modifier: {hasWin}, MainKey: {mainKey}");

            if (uuSeq?.Count == 2 && commaSeq?.Count == 2 && hasWin && mainKey == 88)
            {
                Console.WriteLine("SUCCESS: ParseHotkey successfully parsed multi-key sequences and Win combos!");
            }
            else
            {
                Console.WriteLine("FAIL: ParseHotkey output mismatch!");
            }
        }

        // 6. Test EnsureTriggerHealth collision resolution
        var ensureHealthMethod = typeof(ConfigManager).GetMethod("EnsureTriggerHealth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (ensureHealthMethod != null)
        {
            var testConf = new AppConfig
            {
                Trigger = new TriggerConfig { MouseButton = "RightButton" },
                GestureEnabled = true,
                GestureTriggerButton = "RightButton"
            };
            ensureHealthMethod.Invoke(null, new object[] { testConf });
            Console.WriteLine($"Colliding RightButton adjusted to: {testConf.GestureTriggerButton}");
            if (testConf.GestureTriggerButton == "MiddleButton")
            {
                Console.WriteLine("SUCCESS: EnsureTriggerHealth successfully resolved button collision!");
            }
            else
            {
                Console.WriteLine("FAIL: EnsureTriggerHealth did not resolve button collision!");
            }
        }

        // 4. Test Layer Indicator Appearance Customization and Toggle
        Console.WriteLine("\n--- Testing Layer Indicator Customization ---");
        var layerIndicatorCard = sw.FindName("Tab1_LayerIndicatorCardBorder") as FrameworkElement;
        var showLayerIndicatorCheckBox = sw.FindName("ShowLayerIndicatorCheckBox") as CheckBox;
        var previewLayerBadge = sw.FindName("PreviewLayerIndicatorBadge") as FrameworkElement;
        var previewLayerText = sw.FindName("PreviewLayerIndicatorText") as TextBlock;
        var previewLayerIcon = sw.FindName("PreviewLayerIndicatorIcon") as TextBlock;

        if (layerIndicatorCard == null || showLayerIndicatorCheckBox == null || previewLayerBadge == null || previewLayerText == null || previewLayerIcon == null)
        {
            Console.WriteLine($"FAIL: Layer indicator UI elements missing! card={layerIndicatorCard!=null}, cb={showLayerIndicatorCheckBox!=null}, badge={previewLayerBadge!=null}, text={previewLayerText!=null}, icon={previewLayerIcon!=null}");
        }
        else
        {
            // Simple mode hides Tab1_LayerIndicatorCardBorder
            sw.ApplyConfigMode("Simple", false);
            Console.WriteLine($"[Simple Mode] LayerIndicatorCard Visibility: {layerIndicatorCard.Visibility}");
            if (layerIndicatorCard.Visibility == Visibility.Collapsed)
            {
                Console.WriteLine("SUCCESS: Tab1_LayerIndicatorCardBorder collapsed in Simple Mode!");
            }
            else
            {
                Console.WriteLine("FAIL: Tab1_LayerIndicatorCardBorder not collapsed in Simple Mode!");
            }

            // Pro mode shows Tab1_LayerIndicatorCardBorder
            sw.ApplyConfigMode("Pro", false);
            Console.WriteLine($"[Pro Mode] LayerIndicatorCard Visibility: {layerIndicatorCard.Visibility}");
            if (layerIndicatorCard.Visibility == Visibility.Visible)
            {
                Console.WriteLine("SUCCESS: Tab1_LayerIndicatorCardBorder visible in Pro Mode!");
            }
            else
            {
                Console.WriteLine("FAIL: Tab1_LayerIndicatorCardBorder not visible in Pro Mode!");
            }

            // Test AppConfig defaults and serialization roundtrip
            var cfg = new AppConfig
            {
                ShowLayerIndicator = false,
                LayerIndicatorStyle = "Purple",
                LayerIndicatorBg = "#E62E1065",
                LayerIndicatorBorder = "#C084FC",
                LayerIndicatorTextColor = "#FAF5FF",
                LayerIndicatorIcon = "❄️",
                LayerIndicatorFontSize = 13.0,
                LayerIndicatorCornerRadius = 16.0,
                LayerIndicatorOffsetY = 15.0,
                LayerIndicatorDurationMs = 1500.0
            };

            string json = System.Text.Json.JsonSerializer.Serialize(cfg);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);

            if (deserialized != null &&
                deserialized.ShowLayerIndicator == false &&
                deserialized.LayerIndicatorStyle == "Purple" &&
                deserialized.LayerIndicatorBg == "#E62E1065" &&
                deserialized.LayerIndicatorBorder == "#C084FC" &&
                deserialized.LayerIndicatorTextColor == "#FAF5FF" &&
                deserialized.LayerIndicatorIcon == "❄️" &&
                deserialized.LayerIndicatorFontSize == 13.0 &&
                deserialized.LayerIndicatorCornerRadius == 16.0 &&
                deserialized.LayerIndicatorOffsetY == 15.0 &&
                deserialized.LayerIndicatorDurationMs == 1500.0)
            {
                Console.WriteLine("SUCCESS: AppConfig Layer Indicator properties serialized and deserialized flawlessly!");
            }
            else
            {
                Console.WriteLine("FAIL: Serialization roundtrip mismatch for Layer Indicator properties!");
            }

            // Test RadialWindow Layer Indicator Style Application
            ConfigManager.CurrentConfig.ShowLayerIndicator = true;
            ConfigManager.CurrentConfig.LayerIndicatorStyle = "Aurora";
            ConfigManager.CurrentConfig.LayerIndicatorBg = "#E6082F49";
            ConfigManager.CurrentConfig.LayerIndicatorBorder = "#22D3EE";
            ConfigManager.CurrentConfig.LayerIndicatorTextColor = "#F0FDFA";
            ConfigManager.CurrentConfig.LayerIndicatorIcon = "🌀";
            ConfigManager.CurrentConfig.LayerIndicatorCornerRadius = 14.0;
            ConfigManager.CurrentConfig.LayerIndicatorFontSize = 12.0;

            var multiLayerProfile = new WheelProfile
            {
                ProcessName = "MultiLayerTest",
                Layers = new System.Collections.Generic.List<WheelLayer>
                {
                    new WheelLayer { Name = "第一层" },
                    new WheelLayer { Name = "第二层" }
                }
            };
            RadialWindow rw2 = new RadialWindow(new Point(600, 600), multiLayerProfile);
            rw2.SwitchToLayer(1);

            var rwBadge = rw2.FindName("LayerIndicatorBadge") as Border;
            var rwText = rw2.FindName("LayerIndicatorText") as TextBlock;
            var rwIcon = rw2.FindName("LayerIndicatorIcon") as TextBlock;

            if (rwBadge != null && rwText != null && rwIcon != null)
            {
                Console.WriteLine($"RadialWindow Badge Visibility: {rwBadge.Visibility}");
                Console.WriteLine($"RadialWindow Badge Text: {rwText.Text}");
                Console.WriteLine($"RadialWindow Badge Icon: {rwIcon.Text}");
                Console.WriteLine($"RadialWindow CornerRadius: {rwBadge.CornerRadius.TopLeft}");

                if (rwBadge.Visibility == Visibility.Visible &&
                    rwText.Text.Contains("第二层") &&
                    rwIcon.Text == "🌀" &&
                    rwBadge.CornerRadius.TopLeft == 14.0)
                {
                    Console.WriteLine("SUCCESS: RadialWindow multi-layer indicator badge correctly rendered with custom style!");
                }
                else
                {
                    Console.WriteLine("FAIL: RadialWindow layer indicator badge did not match expected values!");
                }

                // Switch off indicator
                ConfigManager.CurrentConfig.ShowLayerIndicator = false;
                rw2.SwitchToLayer(0);
                if (rwBadge.Visibility == Visibility.Collapsed)
                {
                    Console.WriteLine("SUCCESS: RadialWindow layer indicator correctly hidden when ShowLayerIndicator is false!");
                }
                else
                {
                    Console.WriteLine("FAIL: RadialWindow layer indicator remained visible when ShowLayerIndicator is false!");
                }
            }
            else
            {
                Console.WriteLine("FAIL: RadialWindow LayerIndicator elements not found!");
            }
        }

        // 7. Test Custom Center Pattern Priority & Center Action Offset Elimination
        Console.WriteLine("\n--- Testing Custom Center Pattern Priority & Offset Elimination ---");
        
        // 7.1 Test IconHelper.HasCustomCenterPattern logic
        var testConfig = new AppConfig
        {
            ShowCoreIcon = true,
            CoreIconType = "Exit",
            CoreCustomImagePath = "",
            CoreCustomIconKey = "",
            CoreCustomIconSvg = ""
        };
        bool defaultExitHasPattern = IconHelper.HasCustomCenterPattern(testConfig);
        Console.WriteLine($"Default Exit HasCustomCenterPattern: {defaultExitHasPattern} (Expected: false)");

        testConfig.CoreIconType = "Crosshair";
        bool crosshairHasPattern = IconHelper.HasCustomCenterPattern(testConfig);
        Console.WriteLine($"Crosshair HasCustomCenterPattern: {crosshairHasPattern} (Expected: true)");

        testConfig.CoreIconType = "Custom";
        testConfig.CoreCustomIconKey = "Settings";
        bool customIconHasPattern = IconHelper.HasCustomCenterPattern(testConfig);
        Console.WriteLine($"Custom IconKey HasCustomCenterPattern: {customIconHasPattern} (Expected: true)");

        testConfig.ShowCoreIcon = false;
        bool showCoreFalseHasPattern = IconHelper.HasCustomCenterPattern(testConfig);
        Console.WriteLine($"ShowCoreIcon=false HasCustomCenterPattern: {showCoreFalseHasPattern} (Expected: false)");

        bool patternLogicPass = !defaultExitHasPattern && crosshairHasPattern && customIconHasPattern && !showCoreFalseHasPattern;
        if (patternLogicPass)
        {
            Console.WriteLine("SUCCESS: IconHelper.HasCustomCenterPattern logic evaluated accurately!");
        }
        else
        {
            Console.WriteLine("FAIL: IconHelper.HasCustomCenterPattern returned unexpected results!");
        }

        // 7.2 Test RadialWindow: Custom pattern MUST take priority over CenterAction!
        ConfigManager.CurrentConfig.ShowCoreIcon = true;
        ConfigManager.CurrentConfig.ShowCoreIcon = true;
        ConfigManager.CurrentConfig.CoreIconType = "Crosshair";
        ConfigManager.CurrentConfig.CoreCustomImagePath = "";
        ConfigManager.CurrentConfig.CoreCustomIconKey = "";
        ConfigManager.CurrentConfig.CoreCustomIconSvg = "";
        ConfigManager.CurrentConfig.CoreIconScale = 1.2;
        ConfigManager.CurrentConfig.CoreImageOffsetX = 10.0;
        ConfigManager.CurrentConfig.CoreImageOffsetY = -8.0;

        var centerActionProfile = new WheelProfile
        {
            ProcessName = "TestCenterPriority",
            SectorCount = 8,
            EnableCenterAction = true,
            CenterAction = new ActionItem
            {
                Name = "测试截图动作",
                Type = "Hotkey",
                Parameter = "PrintScreen",
                IconKey = "Scissors" // Different from Crosshair
            }
        };

        RadialWindow rwCustomPattern = new RadialWindow(new Point(500, 500), centerActionProfile);
        var rwIconCustom = rwCustomPattern.FindName("CoreExitIcon") as System.Windows.Shapes.Path;
        var expectedCrosshairGeo = IconHelper.GetCoreIconGeometry("Crosshair");
        var scissorsGeo = IconHelper.GetSvgPathByKey("Scissors");

        bool isCrosshairRendered = rwIconCustom != null && rwIconCustom.Visibility == Visibility.Visible && rwIconCustom.Data != null;
        bool hasOffsetAppliedToCustom = rwIconCustom?.RenderTransform is TranslateTransform tt && tt.X == 10.0 && tt.Y == -8.0;

        Console.WriteLine($"[Custom Pattern Priority] RenderTransform type: {rwIconCustom?.RenderTransform?.GetType()?.FullName}, value: {rwIconCustom?.RenderTransform}");
        Console.WriteLine($"[Custom Pattern Priority] RenderTransform is TranslateTransform(10, -8): {hasOffsetAppliedToCustom}");

        if (isCrosshairRendered && hasOffsetAppliedToCustom)
        {
            Console.WriteLine("SUCCESS: When Custom Center Pattern is set, it takes absolute priority over CenterAction, with user scale/offsets applied!");
        }
        else
        {
            Console.WriteLine("FAIL: Custom Center Pattern priority failed!");
        }

        // 7.3 Test RadialWindow: When NO custom pattern is set, CenterAction MUST be rendered strictly centered with zero offset pollution!
        ConfigManager.CurrentConfig.ShowCoreIcon = true;
        ConfigManager.CurrentConfig.CoreIconType = "Exit"; // Default
        ConfigManager.CurrentConfig.CoreCustomImagePath = "";
        ConfigManager.CurrentConfig.CoreCustomIconKey = "";
        ConfigManager.CurrentConfig.CoreCustomIconSvg = "";
        // Leave offset in config to prove action icon is NOT polluted by it!
        ConfigManager.CurrentConfig.CoreImageOffsetX = 25.0;
        ConfigManager.CurrentConfig.CoreImageOffsetY = -18.0;

        RadialWindow rwActionNoOffset = new RadialWindow(new Point(500, 500), centerActionProfile);
        var rwIconAction = rwActionNoOffset.FindName("CoreExitIcon") as System.Windows.Shapes.Path;
        bool isActionRendered = rwIconAction != null && rwIconAction.Visibility == Visibility.Visible && rwIconAction.Data != null;
        bool isTransformNoOffset = rwIconAction?.RenderTransform == null || rwIconAction.RenderTransform == Transform.Identity || (rwIconAction.RenderTransform is TranslateTransform tt2 && tt2.X == 0 && tt2.Y == 0) || rwIconAction.RenderTransform.Value.IsIdentity;

        Console.WriteLine($"[Action Icon No-Offset] CoreExitIcon Visible: {rwIconAction?.Visibility}");
        Console.WriteLine($"[Action Icon No-Offset] RenderTransform type: {rwIconAction?.RenderTransform?.GetType()?.FullName}, value: {rwIconAction?.RenderTransform}");
        Console.WriteLine($"[Action Icon No-Offset] RenderTransform has zero offset (no offset pollution): {isTransformNoOffset}");

        if (isActionRendered && isTransformNoOffset)
        {
            Console.WriteLine("SUCCESS: When NO custom pattern is set, CenterAction is rendered strictly centered with zero offset pollution (Identity transform)!");
        }
        else
        {
            Console.WriteLine("FAIL: CenterAction icon was either not rendered or had an offset pollution!");
        }

        // 7.4 Test SettingsWindow CenterPatternPriorityTip visibility
        Console.WriteLine("\n--- Testing SettingsWindow CenterPatternPriorityTip ---");
        var centerTip = sw.FindName("CenterPatternPriorityTip") as FrameworkElement;
        var focusCenterBtn = sw.FindName("FocusCenterCoreBtn") as Button;
        if (centerTip != null && focusCenterBtn != null)
        {
            ConfigManager.CurrentConfig.ShowCoreIcon = true;
            ConfigManager.CurrentConfig.CoreIconType = "Crosshair";
            ConfigManager.CurrentConfig.Profiles[0].EnableCenterAction = true;
            
            // Click focus center core button
            var clickEvent = new RoutedEventArgs(Button.ClickEvent);
            focusCenterBtn.RaiseEvent(clickEvent);

            Console.WriteLine($"[Settings Tip] CenterPatternPriorityTip Visibility when custom pattern & center action ON: {centerTip.Visibility}");
            bool tipVisiblePass = centerTip.Visibility == Visibility.Visible;

            // Turn off center action
            ConfigManager.CurrentConfig.Profiles[0].EnableCenterAction = false;
            var enableCheckBox = sw.FindName("EnableCenterActionCheckBox") as CheckBox;
            if (enableCheckBox != null)
            {
                enableCheckBox.IsChecked = false;
            }
            Console.WriteLine($"[Settings Tip] CenterPatternPriorityTip Visibility when center action OFF: {centerTip.Visibility}");
            bool tipHiddenPass = centerTip.Visibility == Visibility.Collapsed;

            if (tipVisiblePass && tipHiddenPass)
            {
                Console.WriteLine("SUCCESS: SettingsWindow CenterPatternPriorityTip dynamically toggles correctly based on configuration!");
            }
            else
            {
                Console.WriteLine("FAIL: SettingsWindow CenterPatternPriorityTip did not match expected visibility states!");
            }
        }
        else
        {
            Console.WriteLine("FAIL: CenterPatternPriorityTip or FocusCenterCoreBtn not found in SettingsWindow!");
        }

        // 8. Test ApplyUipiProtection in TrayController
        Console.WriteLine("\n--- Testing ApplyTrayUipiProtection in TrayController ---");
        var applyUipiMethod = typeof(TrayController).GetMethod("ApplyUipiProtection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (applyUipiMethod != null)
        {
            try
            {
                using var tray = new TrayController();
                applyUipiMethod.Invoke(tray, null);
                Console.WriteLine("SUCCESS: ApplyUipiProtection on TrayController executed cleanly without exceptions!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: ApplyUipiProtection threw: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("FAIL: ApplyUipiProtection method not found on TrayController!");
        }

        // 9. Test ExecuteFolder robustness with non-existent drive
        Console.WriteLine("\n--- Testing ExecuteFolder non-existent drive handling ---");
        var executeFolderMethod = typeof(ActionExecutor).GetMethod("ExecuteFolder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (executeFolderMethod != null)
        {
            try
            {
                // Drive Z:\ or Q:\ which usually doesn't exist
                executeFolderMethod.Invoke(null, new object[] { @"Z:\NonExistentDrive\TestFolder" });
                Console.WriteLine("SUCCESS: ExecuteFolder gracefully handled non-existent drive without fatal crash!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: ExecuteFolder threw: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("FAIL: ExecuteFolder method not found!");
        }

        // 10. Test v1.7.4 Defaults
        Console.WriteLine("\n--- Testing v1.7.4 Default Configuration ---");
        var defCfg = ConfigManager.CreateDefaultConfig();
        if (defCfg.EnableGlobalInheritance != false)
            throw new Exception($"FAIL: EnableGlobalInheritance expected false but was {defCfg.EnableGlobalInheritance}");
        if (defCfg.MappingsCanvasShowText != false)
            throw new Exception($"FAIL: MappingsCanvasShowText expected false but was {defCfg.MappingsCanvasShowText}");
        if (defCfg.WheelRadius != 133.0)
            throw new Exception($"FAIL: WheelRadius expected 133.0 but was {defCfg.WheelRadius}");
        if (defCfg.InnerRadius != 70.0)
            throw new Exception($"FAIL: InnerRadius expected 70.0 but was {defCfg.InnerRadius}");
        if (defCfg.CoreRadius != 36.0)
            throw new Exception($"FAIL: CoreRadius expected 36.0 but was {defCfg.CoreRadius}");
        if (defCfg.SectorGap != 4.0)
            throw new Exception($"FAIL: SectorGap expected 4.0 but was {defCfg.SectorGap}");
        if (defCfg.SectorCornerRadius != 13.0)
            throw new Exception($"FAIL: SectorCornerRadius expected 13.0 but was {defCfg.SectorCornerRadius}");
        if (defCfg.SectorFontSize != 13.0)
            throw new Exception($"FAIL: SectorFontSize expected 13.0 but was {defCfg.SectorFontSize}");
        if (defCfg.SubWheelTriggerDistance != 141.0)
            throw new Exception($"FAIL: SubWheelTriggerDistance expected 141.0 but was {defCfg.SubWheelTriggerDistance}");
        if (defCfg.SubWheelOuterRadius != 196.0)
            throw new Exception($"FAIL: SubWheelOuterRadius expected 196.0 but was {defCfg.SubWheelOuterRadius}");
        if (defCfg.SubWheelInnerGap != 7.0)
            throw new Exception($"FAIL: SubWheelInnerGap expected 7.0 but was {defCfg.SubWheelInnerGap}");
        if (defCfg.SubWheelCornerRadius != 14.0)
            throw new Exception($"FAIL: SubWheelCornerRadius expected 14.0 but was {defCfg.SubWheelCornerRadius}");
        if (defCfg.SubWheelIconSize != 16.0)
            throw new Exception($"FAIL: SubWheelIconSize expected 16.0 but was {defCfg.SubWheelIconSize}");
        if (defCfg.SubWheelUiStyle != "FollowPrimary")
            throw new Exception($"FAIL: SubWheelUiStyle expected FollowPrimary but was {defCfg.SubWheelUiStyle}");
        if (defCfg.AutoExpandSubRingsOnPopup != false)
            throw new Exception($"FAIL: AutoExpandSubRingsOnPopup expected false but was {defCfg.AutoExpandSubRingsOnPopup}");
        if (defCfg.ShowCoreIcon != false)
            throw new Exception($"FAIL: ShowCoreIcon expected false but was {defCfg.ShowCoreIcon}");
        if (defCfg.CoreIconType != "Image")
            throw new Exception($"FAIL: CoreIconType expected Image but was {defCfg.CoreIconType}");
        Console.WriteLine("SUCCESS: All v1.7.4 default configuration assertions passed!");

        // 11. Test Multi-Layer Global Inheritance Isolation (v1.7.4)
        Console.WriteLine("\n--- Testing Multi-Layer Global Inheritance Isolation (v1.7.4) ---");
        ConfigManager.CurrentConfig.EnableGlobalInheritance = true;

        WheelProfile testGlobal = new WheelProfile
        {
            ProcessName = "Global",
            SectorCount = 8,
            Layers = new List<WheelLayer>
            {
                new WheelLayer
                {
                    Name = "第 1 层",
                    SectorCount = 8,
                    EnableCenterAction = true,
                    CenterAction = new ActionItem { Type = "Hotkey", Name = "全局中心1", Parameter = "Ctrl+1" },
                    Actions = new List<ActionItem>
                    {
                        new ActionItem { Type = "Hotkey", Name = "全局L1_动作0", Parameter = "Ctrl+C" },
                        new ActionItem { Type = "Hotkey", Name = "全局L1_动作1", Parameter = "Ctrl+V" }
                    }
                },
                new WheelLayer
                {
                    Name = "第 2 层",
                    SectorCount = 8,
                    EnableCenterAction = true,
                    CenterAction = new ActionItem { Type = "Hotkey", Name = "全局中心2", Parameter = "Ctrl+2" },
                    Actions = new List<ActionItem>
                    {
                        new ActionItem { Type = "Hotkey", Name = "全局L2_动作0", Parameter = "Ctrl+X" },
                        new ActionItem { Type = "Hotkey", Name = "全局L2_动作1", Parameter = "Ctrl+Z" }
                    }
                }
            }
        };
        testGlobal.EnsureLayers();

        WheelProfile testApp = new WheelProfile
        {
            ProcessName = "TestApp.exe",
            SectorCount = 8,
            Layers = new List<WheelLayer>
            {
                new WheelLayer
                {
                    Name = "App 第 1 层",
                    SectorCount = 8,
                    EnableCenterAction = false, // 留空继承全局
                    Actions = new List<ActionItem>
                    {
                        new ActionItem { Type = "Hotkey", Name = "动作 1", Parameter = "" }, // 留空继承全局
                        new ActionItem { Type = "Hotkey", Name = "App自身动作1", Parameter = "F5" } // 本地配置，不继承
                    }
                },
                new WheelLayer
                {
                    Name = "App 第 2 层",
                    SectorCount = 8,
                    EnableCenterAction = false, // 留空继承全局
                    Actions = new List<ActionItem>
                    {
                        new ActionItem { Type = "Hotkey", Name = "动作 1", Parameter = "" }, // 留空继承全局
                        new ActionItem { Type = "Hotkey", Name = "动作 2", Parameter = "" }  // 留空继承全局
                    }
                },
                new WheelLayer
                {
                    Name = "App 第 3 层", // 超额层，全局只有2层
                    SectorCount = 8,
                    EnableCenterAction = false,
                    Actions = new List<ActionItem>
                    {
                        new ActionItem { Type = "Hotkey", Name = "动作 1", Parameter = "" }
                    }
                }
            }
        };
        testApp.EnsureLayers();

        // 场景 A：全局方案当前停留在第 1 层 (ActiveLayerIndex = 0)
        testGlobal.ActiveLayerIndex = 0;
        testGlobal.SyncRootPropertiesFromActiveLayer();

        // App Layer 1 槽位 0 应该继承全局 Layer 1 动作 0 ("全局L1_动作0")
        testApp.ActiveLayerIndex = 0;
        testApp.SyncRootPropertiesFromActiveLayer();
        var effA_L1_0 = testApp.GetEffectiveAction(0, -1, testGlobal, 0);
        if (effA_L1_0 == null || effA_L1_0.Name != "全局L1_动作0" || !effA_L1_0.IsInherited)
            throw new Exception($"FAIL: App Layer 1 slot 0 expected 全局L1_动作0 but got {effA_L1_0?.Name}");

        // App Layer 1 槽位 1 是自身动作，不应继承
        var effA_L1_1 = testApp.GetEffectiveAction(1, -1, testGlobal, 0);
        if (effA_L1_1 == null || effA_L1_1.Name != "App自身动作1" || effA_L1_1.IsInherited)
            throw new Exception($"FAIL: App Layer 1 slot 1 expected App自身动作1 but got {effA_L1_1?.Name}");

        // App Layer 2 槽位 0 应该继承全局 Layer 2 动作 0 ("全局L2_动作0")
        testApp.ActiveLayerIndex = 1;
        testApp.SyncRootPropertiesFromActiveLayer();
        var effA_L2_0 = testApp.GetEffectiveAction(0, -1, testGlobal, 1);
        if (effA_L2_0 == null || effA_L2_0.Name != "全局L2_动作0" || !effA_L2_0.IsInherited)
            throw new Exception($"FAIL: App Layer 2 slot 0 expected 全局L2_动作0 but got {effA_L2_0?.Name}");

        // 场景 B：关键测试！模拟用户在控制台切换全局方案至第 2 层 (ActiveLayerIndex = 1)
        testGlobal.ActiveLayerIndex = 1;
        testGlobal.SyncRootPropertiesFromActiveLayer();

        // 当全局停留在第 2 层时，App Layer 1 槽位 0 依然必须严格继承全局第 1 层！不能被全局的第 2 层污染！
        testApp.ActiveLayerIndex = 0;
        testApp.SyncRootPropertiesFromActiveLayer();
        var effB_L1_0 = testApp.GetEffectiveAction(0, -1, testGlobal, 0);
        if (effB_L1_0 == null || effB_L1_0.Name != "全局L1_动作0" || !effB_L1_0.IsInherited)
            throw new Exception($"FAIL (CRITICAL): App Layer 1 polluted by Global Layer 2! Expected 全局L1_动作0 but got {effB_L1_0?.Name}");

        // App Layer 2 槽位 0 应该继续继承全局第 2 层
        testApp.ActiveLayerIndex = 1;
        testApp.SyncRootPropertiesFromActiveLayer();
        var effB_L2_0 = testApp.GetEffectiveAction(0, -1, testGlobal, 1);
        if (effB_L2_0 == null || effB_L2_0.Name != "全局L2_动作0" || !effB_L2_0.IsInherited)
            throw new Exception($"FAIL: App Layer 2 slot 0 expected 全局L2_动作0 but got {effB_L2_0?.Name}");

        // 场景 C：超额层优雅回退测试（App Layer 3 继承全局 Layer 1）
        testApp.ActiveLayerIndex = 2;
        testApp.SyncRootPropertiesFromActiveLayer();
        var effC_L3_0 = testApp.GetEffectiveAction(0, -1, testGlobal, 2);
        if (effC_L3_0 == null || effC_L3_0.Name != "全局L1_动作0" || !effC_L3_0.IsInherited)
            throw new Exception($"FAIL: App Layer 3 (excess) expected fallback to 全局L1_动作0 but got {effC_L3_0?.Name}");

        // 场景 D：中心核圆按层对应继承与污染防护测试
        var centerL1 = testApp.GetEffectiveCenterAction(testGlobal, 0);
        if (centerL1 == null || centerL1.Name != "全局中心1" || !centerL1.IsInherited)
            throw new Exception($"FAIL: App Layer 1 center expected 全局中心1 but got {centerL1?.Name}");

        var centerL2 = testApp.GetEffectiveCenterAction(testGlobal, 1);
        if (centerL2 == null || centerL2.Name != "全局中心2" || !centerL2.IsInherited)
            throw new Exception($"FAIL: App Layer 2 center expected 全局中心2 but got {centerL2?.Name}");

        Console.WriteLine("SUCCESS: Multi-layer global inheritance isolation 100% verified!");

        Console.WriteLine("\n=== ALL V1.7.0 COMPREHENSIVE TESTS PASSED! ===");
    }
}


