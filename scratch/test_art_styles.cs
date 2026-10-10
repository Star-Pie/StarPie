using System.Reflection;
using System.IO;
using System.Collections;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinPieGestures;
using WinPieGestures.ArtStyles;
using System.Windows.Media.Imaging;

internal static class ArtStyleTests
{
    private static int failures;
    private static int passed;
    [STAThread]
    public static int Main(string[] args)
    {
        if (!args.Contains("--test-instance"))
        {
            Console.Error.WriteLine("Use --test-instance: refuses to run outside diagnostic isolation.");
            return 2;
        }
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(Path.GetTempPath(), "StarPie-ArtStyle-Tests-" + Guid.NewGuid().ToString("N")));
        ConfigManager.DisableAutoStartSync=true;
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        RunDataTests();
        if (args.Contains("--data")) return Summary();
        SetConfig(JsonSerializer.Deserialize<AppConfig>("{\"SelectedArtStyleId\":\"paper\"}")!);
        var root = new Border();
        AppThemeManager.ApplyTheme(root, "Light");
        Check(((SolidColorBrush)root.Resources["WindowBackgroundBrush"]).Color.ToString() == "#FFF5F1E8", "selected paper style supplies app resources");
        Check(root.TryFindResource("CardCornerRadius") is CornerRadius { TopLeft: 4 }, "paper corner radius resource");
        Check(root.TryFindResource("ArtFontFamily") is FontFamily, "profile typography resource");
        foreach (var p in All(ConfigManager.CurrentConfig))
        {
            var colors = Value(p, "Colors");
            Check((double)Call("ArtStyleResolver","Contrast",Value(colors,"Text"),Value(colors,"Background")) >= 4.5, "body contrast " + Value(p,"Id"));
            Check((double)Call("ArtStyleResolver","Contrast",Value(colors,"WheelHighlightText"),Value(colors,"WheelHighlight")) >= 4.5, "hover contrast " + Value(p,"Id"));
        }
        bool hasNotify = typeof(AppConfig).Assembly.GetType("WinPieGestures.ArtStyles.ArtStyleService")!.GetMethod("NotifyChanged") != null;
        Check(hasNotify, "open-window refresh service exists");
        if (hasNotify)
        {
            var first = new Window(); var second = new Window();
            AppThemeManager.ApplyTheme(first,"Light"); AppThemeManager.ApplyTheme(second,"Light");
            Put(ConfigManager.CurrentConfig,"SelectedArtStyleId","obsidian");
            var pluginWindow=new Window { FontFamily=new FontFamily("Consolas") };
            pluginWindow.Resources["AccentPrimaryBrush"]=ArtStyleResources.Brush("#123456");
            Call("ArtStyleService","NotifyChanged");
            Check(pluginWindow.FontFamily.Source=="Consolas" && ((SolidColorBrush)pluginWindow.Resources["AccentPrimaryBrush"]).Color.ToString()=="#FF123456","refresh preserves unowned plugin window local appearance");
            Check(((SolidColorBrush)second.Resources["WindowBackgroundBrush"]).Color.ToString() == "#FF11171B", "second host window refreshes");
            Check(AppThemeManager.CurrentEffectiveTheme == "Dark", "dark metadata controls effective mode");
            Check(((SolidColorBrush)second.Resources["TextPrimaryBrush"]).IsFrozen, "semantic brushes frozen");
            Put(ConfigManager.CurrentConfig,"SelectedArtStyleId","");
            Call("ArtStyleService","NotifyChanged");
            Check(second.Resources["CardCornerRadius"] is CornerRadius { TopLeft: 12 }, "legacy metrics restored after leaving art style");
            Check(second.Resources["ControlCornerRadius"] is CornerRadius { TopLeft: 6 }, "legacy control radius restored exactly");
            first.Close(); second.Close(); pluginWindow.Close();
        }
        RunRendererTests();
        RunStudioTests(args);
        RunReviewRegressions();
        RunWindowPresentationTests(args);
        RunCornersFontsAndLayerTests();
        RunSharedMetricTests();
        if (args.Contains("--export-themes"))
        {
            Directory.CreateDirectory("themes");
            foreach (var p in ArtStyleCatalog.GetAll(new AppConfig())) File.WriteAllText(Path.Combine("themes",p.Id+".starpie-theme.json"),ArtStyleService.Export(p));
        }
        return Summary();
    }
    private static void RunSharedMetricTests()
    {
        SetConfig(new AppConfig { SelectedArtStyleId="pixel" });
        System.Xml.Linq.XNamespace wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source=System.Xml.Linq.XDocument.Load("WinPieGestures/App.xaml").Root!.Element(wpf+"Application.Resources")!.Element(wpf+"ResourceDictionary")!;
        source.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns+"x","http://schemas.microsoft.com/winfx/2006/xaml");
        source.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns+"local","clr-namespace:WinPieGestures;assembly=StarPie");
        var dictionary=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(source.ToString());
        var button=new Button { Content="Shared host control",Style=(Style)dictionary["ModernButtonStyle"] };
        var root=new Border { Child=button }; ArtStyleResources.ApplyPreview(root,ArtStyleCatalog.Find(ConfigManager.CurrentConfig,"pixel")!);
        root.Measure(new Size(240,60)); root.Arrange(new Rect(0,0,240,60)); root.UpdateLayout(); button.ApplyTemplate();
        var frame=(Border)button.Template.FindName("border",button);
        Check(button.BorderThickness.Left==2.5 && frame.CornerRadius.TopLeft==0,"shared application style consumes Pixel stroke and radius");
        var dialog=new ColorPickerWindow("#123456");
        var ok=(Button)dialog.FindName("OkButton");
        Check(ok.BorderThickness.Left==2.5,"self-contained host dialog follows Pixel stroke"); dialog.Close();
    }
    private static void RunStudioTests(string[] args)
    {
        var studioType = typeof(AppConfig).Assembly.GetType("WinPieGestures.ArtStyles.ThemeStudio");
        Check(studioType != null,"theme studio exists");
        if (studioType == null) return;
        var c = new AppConfig { SelectedArtStyleId = "paper" }; SetConfig(c);
        var studio = (UserControl)Activator.CreateInstance(studioType)!;
        var edit = studioType.GetMethod("BeginEdit",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var cancel = studioType.GetMethod("CancelDraft",BindingFlags.Instance|BindingFlags.NonPublic)!;
        edit.Invoke(studio,[true]);
        var font=(ComboBox)studio.FindName("FontBox"); font.ApplyTemplate();
        Check(font.Template.FindName("PART_EditableTextBox",font) is TextBox,"font selection supports editable text template");
        var name = (TextBox)studio.FindName("DraftNameBox");
        name.Text = "Saved Theme";
        string before = JsonSerializer.Serialize(c);
        var draft = studioType.GetField("draft",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(studio)!;
        Put(Value(draft,"Colors"),"Accent","#123456");
        Check(JsonSerializer.Serialize(c) == before,"draft editing does not alter current config");
        var fields=(StackPanel)studio.FindName("PaletteFields");
        var colorInput=fields.Children.OfType<Grid>().First().Children.OfType<TextBox>().Single();
        try
        {
            colorInput.Text="oops";
            Check(!((Button)studio.FindName("SaveDraftButton")).IsEnabled,"invalid color disables save without crashing editor");
        }
        catch (Exception) { Check(false,"invalid color disables save without crashing editor"); }
        try
        {
            foreach(var language in Enum.GetValues<LanguageCode>())
            {
                I18n.CurrentLanguage=language; studioType.GetMethod("Refresh")!.Invoke(studio,null);
            }
            Check(!((Button)studio.FindName("SaveDraftButton")).IsEnabled,"language refresh safely preserves invalid draft color");
        }
        catch (Exception) { Check(false,"language refresh safely preserves invalid draft color"); }
        cancel.Invoke(studio,null);
        Check(JsonSerializer.Serialize(c) == before,"cancel leaves current config intact");
        edit.Invoke(studio,[true]); name.Text = "Saved Theme";
        studioType.GetMethod("SaveDraft_Click",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(studio,[studio,new RoutedEventArgs()]);
        Check(c.CustomArtStyles.Count == 1 && c.CustomArtStyles[0].Name == "Saved Theme","studio saves independent user theme");
        Check(c.SelectedArtStyleId == c.CustomArtStyles[0].Id,"studio applies saved theme");
        ConfigManager.LoadConfig();
        Check(ConfigManager.CurrentConfig.SelectedArtStyleId==c.SelectedArtStyleId && ConfigManager.CurrentConfig.CustomArtStyles.Single().Name=="Saved Theme","saved user theme survives disk reload");
        SetConfig(c);
        var paletteBefore = ((SolidColorBrush)Application.Current.Resources["AccentPrimaryBrush"]).Color;
        var previewApi = typeof(AppConfig).Assembly.GetType("WinPieGestures.ArtStyles.ArtStyleResources")!.GetMethod("ApplyPreview");
        Check(previewApi != null,"isolated draft preview resources exist");
        if (previewApi != null)
        {
            previewApi.Invoke(null,[new Border(),Call("ArtStyleCatalog","Find",c,"obsidian")]);
            Check(((SolidColorBrush)Application.Current.Resources["AccentPrimaryBrush"]).Color == paletteBefore,"draft preview does not publish application resources");
        }
        foreach (var lang in Enum.GetValues<LanguageCode>())
        {
            I18n.CurrentLanguage = lang;
            studioType.GetMethod("Refresh")!.Invoke(studio,null);
            Check(!string.IsNullOrEmpty(((TextBlock)studio.FindName("TitleText")).Text),"studio title translated " + lang);
            Check(ArtStyleText.Keys.All(key=>!string.IsNullOrWhiteSpace(ArtStyleText.Get(key)) && (lang!=LanguageCode.En || !System.Text.RegularExpressions.Regex.IsMatch(ArtStyleText.Get(key),"[\\u4E00-\\u9FFF]"))),"all studio strings translated " + lang);
        }
        if (args.Contains("--render")) RenderStyles();
        c.AutoCheckUpdate=false; c.EnableMultiTier=true;
        var actions=Enumerable.Range(0,8).Select(i=>new ActionItem { Name="Command "+i,Parameter="CTRL+C" }).ToList();
        actions[0].SubActions=[new ActionItem { Name="Subcommand",Parameter="CTRL+V" }];
        c.Profiles=[new WheelProfile { Actions=actions }];
        SetConfig(c);
        var settings=new SettingsWindow();
        Check(settings.FindName("ThemeStudioControl") is UserControl,"studio integrated in native settings");
        Check(((Image)settings.FindName("SidebarLogoImage")).Source is DrawingImage { IsFrozen: true }, "sidebar uses the frozen theme-aware vector brand");
        var integrated=(ThemeStudio)settings.FindName("ThemeStudioControl");
        typeof(ThemeStudio).GetMethod("Subscribe",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(integrated,null);
        settings.SwitchToTab(1);
        var render=typeof(SettingsWindow).GetMethod("RenderLiveWheelPreview",BindingFlags.NonPublic|BindingFlags.Instance)!;
        render.Invoke(settings,null);
        var field=typeof(SettingsWindow).GetField("_previewSubContainers",BindingFlags.NonPublic|BindingFlags.Instance)!;
        int firstCount=((IList)field.GetValue(settings)!).Count;
        render.Invoke(settings,null);
        Check(firstCount>0 && ((IList)field.GetValue(settings)!).Count==firstCount,"repeated preview does not retain stale secondary containers");
        c.CustomColorPresets=[new CustomColorPreset { Id="preview-test",SectorBg="#FF123456",SectorBorder="#FFABCDEF",TextColor="#FFEEDDCC" }];
        c.UseIndependentSubWheelTheme=true; c.SubWheelTheme="CustomPreset_preview-test";
        edit.Invoke(integrated,[true]);
        var subRenderer=(IRadialStyleRenderer)typeof(SettingsWindow).GetField("_previewSubStyleRenderer",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(settings)!;
        Check(((SolidColorBrush)subRenderer.DefaultSectorBrush).Color.ToString()=="#FF123456","draft preview preserves saved independent secondary preset");
        cancel.Invoke(integrated,null);
        c.ArtStyleFollowsWheel=false; c.Theme="Custom"; c.CustomSectorBg="#FF654321";
        edit.Invoke(integrated,[true]);
        var mainRenderer=(IRadialStyleRenderer)typeof(SettingsWindow).GetField("_previewStyleRenderer",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(settings)!;
        Check(((SolidColorBrush)mainRenderer.DefaultSectorBrush).Color.ToString()=="#FF654321","draft preview preserves custom main palette when follow disabled");
        cancel.Invoke(integrated,null); c.ArtStyleFollowsWheel=true; c.UseIndependentSubWheelTheme=false; c.SubWheelTheme="FollowPrimary";
        var segment=(RadioButton)settings.FindName("ConfigModeSimpleRadio"); segment.ApplyTemplate();
        var saveButton=(Button)settings.FindName("SaveButton");
        var card=(Border)((FrameworkElement)((FrameworkElement)settings.FindName("VisualThemeCardTitleText")).Parent).Parent;
        foreach (var p in ArtStyleCatalog.GetAll(new AppConfig()))
        {
            c.SelectedArtStyleId=p.Id; ArtStyleService.NotifyChanged();
            Check(ArtStyleResolver.Contrast(((SolidColorBrush)segment.Foreground).Color.ToString(),p.Colors.Accent)>=4.5,"selected settings control contrast "+p.Id);
            Check(((SolidColorBrush)integrated.Resources["CardBackgroundBrush"]).Color.ToString()==ArtStyleResources.Brush(p.Colors.Surface).Color.ToString(),"studio resources refresh after switch "+p.Id);
            Check(saveButton.BorderThickness.Left==p.StrokeWidth && card.BorderThickness.Left==p.StrokeWidth,"generic settings controls follow stroke "+p.Id);
        }
        c.SelectedArtStyleId="paper"; ArtStyleService.NotifyChanged();
        typeof(ThemeStudio).GetMethod("Unsubscribe",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(integrated,null);
        Check(!(bool)typeof(ThemeStudio).GetField("subscribed",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(integrated)!,"studio detaches language and theme listeners on unload");
        var invalid=new ArtStyleProfile { Id="user-invalid",Name="Invalid configured style",Colors=null! };
        var brokenConfig=new AppConfig { SelectedArtStyleId=invalid.Id,CustomArtStyles=[invalid] }; SetConfig(brokenConfig);
        try
        {
            var fallbackStudio=new ThemeStudio(); edit.Invoke(fallbackStudio,[true]);
            var safeDraft=(ArtStyleProfile)studioType.GetField("draft",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(fallbackStudio)!;
            Check(ArtStyleResolver.Validate(safeDraft).Count==0 && brokenConfig.CustomArtStyles.Count==1,"invalid configured theme offers safe editable fallback without deleting data");
        }
        catch(Exception) { Check(false,"invalid configured theme offers safe editable fallback without deleting data"); }
        SetConfig(c);
        if (args.Contains("--render")) Capture((FrameworkElement)settings.Content,1220,740,Path.GetFullPath("artifacts/art-styles/settings-studio.png"));
        typeof(SettingsWindow).GetField("_isClosingForRelease",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(settings,true);
        settings.Close();
    }
    private static void RunReviewRegressions()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var actions = Enumerable.Range(0, 8).Select(i => new ActionItem { Name = "Review " + i, Parameter = "CTRL+C" }).ToList();
        actions[0].SubActions = [new ActionItem { Name = "Review sub", Parameter = "CTRL+V" }];
        var config = new AppConfig
        {
            SelectedArtStyleId = "obsidian", AutoCheckUpdate = false, EnableMultiTier = true,
            Theme = "Light", UiStyle = "ClassicRing", WheelFontFamily = "Segoe UI",
            SubWheelTheme = "Custom", SubWheelUiStyle = "FollowPrimary", UseIndependentSubWheelTheme = true,
            SubWheelCustomSectorBg = "#FF123456", SubWheelCustomText = "#FFEEDDCC",
            Profiles = [new WheelProfile { Actions = actions }]
        };
        SetConfig(config);
        var settings = new SettingsWindow();
        try
        {
            settings.SwitchToTab(1);
            var render = typeof(SettingsWindow).GetMethod("RenderLiveWheelPreview", flags)!;
            var previewBrush = typeof(SettingsWindow).GetField("_previewSubDefaultBrush", flags)!;
            var expander = (Expander)settings.FindName("SubCustomColorExpander");
            Check(expander.IsExpanded, "review starts with expanded independent custom palette");
            render.Invoke(settings, null);
            Check(Hex(previewBrush.GetValue(settings)) == "#FF123456", "independent custom secondary palette still takes priority");

            var themeBox = (ComboBox)settings.FindName("SubWheelThemeComboBox");
            themeBox.SelectedItem = themeBox.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "FollowPrimary");
            Check(config.SubWheelTheme == "FollowPrimary" && !config.UseIndependentSubWheelTheme,
                "custom-to-follow transition changes the real secondary configuration");
            Check(expander.IsExpanded && config.SubWheelCustomSectorBg == "#FF123456",
                "custom-to-follow retains the user's inactive palette");

            foreach (var profile in ArtStyleCatalog.GetAll(new AppConfig()))
            {
                config.SelectedArtStyleId = profile.Id;
                render.Invoke(settings, null);
                var radial = new RadialWindow(new Point(400, 400), config.Profiles[0]);
                try
                {
                    var actual = typeof(RadialWindow).GetField("_subDefaultSectorBrush", flags)!.GetValue(radial);
                    Check(Hex(previewBrush.GetValue(settings)) == Hex(actual), "secondary preview matches real wheel after returning to follow " + profile.Id);
                }
                finally { radial.Close(); }

                AppThemeManager.ApplyTheme(settings, config.AppTheme);
                var slotText = (TextBlock)settings.FindName("FocusSlotBadgeText");
                var slotFrame = (Border)settings.FindName("FocusSlotBadgeBorder");
                var batchText = (TextBlock)settings.FindName("FocusBatchBadgeText");
                var batchFrame = (Border)batchText.Parent;
                Check(ArtStyleResolver.Contrast(Hex(slotText.Foreground), Hex(slotFrame.Background)) >= 4.5,
                    "slot badge text is readable " + profile.Id);
                Check(ArtStyleResolver.Contrast(Hex(batchText.Foreground), Hex(batchFrame.Background)) >= 4.5,
                    "batch badge text is readable " + profile.Id);
            }

            config.SelectedArtStyleId = "pixel";
            render.Invoke(settings, null);
            settings.OnPreviewSectorClicked(0);
            var fontBox = (ComboBox)settings.FindName("WheelFontFamilyComboBox");
            string selectedFont() => (fontBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fontBox.Text;
            string pixelFont = ArtStyleCatalog.Find(config, "pixel")!.FontFamily;
            Check(selectedFont() == pixelFont, "primary font editor inherits the art style");
            settings.OnPreviewSubSectorClicked(0, 0);
            Check(selectedFont() == pixelFont, "following secondary font editor inherits the art style");

            config.SubWheelTheme = "Light";
            config.UseIndependentSubWheelTheme = true;
            render.Invoke(settings, null);
            settings.OnPreviewSubSectorClicked(0, 0);
            Check(selectedFont() == config.WheelFontFamily, "independent secondary font editor inherits the legacy global font");
            var editingAction = (ActionItem)typeof(SettingsWindow).GetMethod("GetCurrentEditingAction", flags)!.Invoke(settings, null)!;
            editingAction.CustomFontFamily = "Consolas, Cascadia Code";
            settings.OnPreviewSubSectorClicked(0, 0);
            Check(selectedFont() == "Consolas, Cascadia Code", "secondary action font override remains highest priority");

            config.SelectedArtStyleId = "";
            AppThemeManager.ApplyTheme(settings, config.AppTheme);
            var soft = (System.Windows.Media.Effects.DropShadowEffect)settings.Resources["ChipShadowSoftEffect"];
            var chip = (System.Windows.Media.Effects.DropShadowEffect)settings.Resources["ChipShadowEffect"];
            Check(soft.BlurRadius == 8 && soft.ShadowDepth == 1 && soft.Opacity == .16 && soft.IsFrozen,
                "leaving an art style restores the original soft chip shadow");
            Check(chip.BlurRadius == 8 && chip.ShadowDepth == 1 && chip.Opacity == .12 && chip.IsFrozen,
                "leaving an art style restores the original chip shadow");
        }
        finally
        {
            typeof(SettingsWindow).GetField("_isClosingForRelease", flags)!.SetValue(settings, true);
            settings.Close();
        }
        static string Hex(object? brush) => ((SolidColorBrush)brush!).Color.ToString();
    }

    private static void RunWindowPresentationTests(string[] args)
    {
        var assembly = typeof(AppConfig).Assembly;
        var dialogType = assembly.GetType("WinPieGestures.AppMessageDialog");
        var frameType = assembly.GetType("WinPieGestures.HostWindowFrame");
        Check(dialogType != null && frameType != null, "shared themed caption and prompt exist");
        if (dialogType == null || frameType == null) return;
        var config = new AppConfig { SelectedArtStyleId = "paper", AutoCheckUpdate = false };
        SetConfig(config);
        var content = new TextBox { Text = "Original content", MinHeight = 100 };
        var window = new Window { Title = "StarPie", Width = 460, Height = 200, Content = content };
        AppThemeManager.ApplyTheme(window, "Light");
        object? wrapped = window.Content;
        Check(System.Windows.Shell.WindowChrome.GetWindowChrome(window) is { CaptionHeight: 36, UseAeroCaptionButtons: false },
            "host caption uses native WPF non-client behavior");
        Check(Elements((FrameworkElement)window.Content).Contains(content), "caption wrapping preserves original content identity");
        AppThemeManager.ApplyTheme(window, "Light");
        Check(ReferenceEquals(wrapped, window.Content), "reapplying a theme does not nest window frames");
        var captionButtons = Elements((FrameworkElement)window.Content).OfType<Button>().ToArray();
        Check(captionButtons.All(button => button.IsEnabled), "caption commands are enabled immediately after frame construction");
        var maximize = captionButtons.Single(button => button.Command == System.Windows.SystemCommands.MaximizeWindowCommand);
        Check(captionButtons.Any(button => button.Command == System.Windows.SystemCommands.MinimizeWindowCommand) &&
            captionButtons.Any(button => button.Command == System.Windows.SystemCommands.CloseWindowCommand), "caption retains standard minimize and close commands");
        window.WindowState = WindowState.Maximized;
        AppThemeManager.ApplyTheme(window, "Light");
        Check(maximize.Command == System.Windows.SystemCommands.RestoreWindowCommand, "maximized caption exposes restore command");
        window.WindowState = WindowState.Normal;
        window.ResizeMode = ResizeMode.NoResize;
        AppThemeManager.ApplyTheme(window, "Light");
        Check(maximize.Visibility == Visibility.Collapsed && System.Windows.Shell.WindowChrome.GetWindowChrome(window).ResizeBorderThickness.Left == 0,
            "fixed-size prompts hide resize caption controls");
        window.Close();

        var overlay = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Content = new Border() };
        object? overlayContent = overlay.Content;
        AppThemeManager.ApplyTheme(overlay, "Light");
        Check(System.Windows.Shell.WindowChrome.GetWindowChrome(overlay) == null && ReferenceEquals(overlayContent, overlay.Content),
            "transparent overlays retain their original window contract");
        overlay.Close();

        var lateWindow = new Window { Title = "Parameters" };
        AppThemeManager.ApplyTheme(lateWindow, "Light");
        Check(System.Windows.Shell.WindowChrome.GetWindowChrome(lateWindow) == null, "empty dynamic windows defer caption wrapping until content exists");
        var lateContent = new Border { Child = new TextBox { Text = "Parameter input" } };
        lateWindow.Content = lateContent;
        AppThemeManager.ApplyTheme(lateWindow, "Light");
        Check(Elements((FrameworkElement)lateWindow.Content).OfType<Grid>().Any(grid => grid.Name == "HostCaption") &&
            Elements((FrameworkElement)lateWindow.Content).Contains(lateContent), "dynamic content remains below its installed caption");
        lateWindow.Close();

        foreach (var profile in ArtStyleCatalog.GetAll(config))
        {
            config.SelectedArtStyleId = profile.Id;
            var dialog = (Window)Activator.CreateInstance(dialogType, [I18n.T("PromptSavedMessage"), I18n.T("PromptSavedTitle"), MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK])!;
            var elements = Elements((FrameworkElement)dialog.Content).ToArray();
            var caption = elements.OfType<Grid>().Single(grid => grid.Name == "HostCaption");
            Check(((SolidColorBrush)caption.Background).Color == ArtStyleResources.Brush(profile.Colors.Background).Color, "caption follows current palette " + profile.Id);
            Check(elements.OfType<Image>().First().Source is DrawingImage { IsFrozen: true }, "caption brand mark is vector and frozen " + profile.Id);
            var results = (StackPanel)dialog.FindName("ResultButtons");
            var primary = results.Children.OfType<Button>().Single();
            Check(primary.IsCancel, "OK-only prompt preserves the Escape route " + profile.Id);
            Check(primary.IsDefault && ArtStyleResolver.Contrast(((SolidColorBrush)primary.Foreground).Color.ToString(), ((SolidColorBrush)primary.Background).Color.ToString()) >= 4.5,
                "prompt default button is readable " + profile.Id);
            if (args.Contains("--render"))
            {
                string directory = Path.GetFullPath("artifacts/art-styles"); Directory.CreateDirectory(directory);
                Capture((FrameworkElement)dialog.Content, 460, 210, Path.Combine(directory, profile.Id + "-prompt.png"));
            }
            primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check((MessageBoxResult)dialogType.GetProperty("Response")!.GetValue(dialog)! == MessageBoxResult.OK, "prompt OK preserves its result " + profile.Id);
        }
        foreach (var choice in new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel })
        {
            var dialog = (Window)Activator.CreateInstance(dialogType, ["Question", "StarPie", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.No])!;
            var results = (StackPanel)dialog.FindName("ResultButtons");
            var buttons = results.Children.OfType<Button>().ToArray();
            Check(buttons.Single(button => button.IsDefault).Content?.ToString() == I18n.T("PromptNo"), "confirmation preserves explicit default No for " + choice);
            Check(buttons.Single(button => button.IsCancel).Content?.ToString() == I18n.T("BtnCancel"), "confirmation keeps keyboard cancellation for " + choice);
            int index = choice == MessageBoxResult.Yes ? 0 : choice == MessageBoxResult.No ? 1 : 2;
            buttons[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check((MessageBoxResult)dialogType.GetProperty("Response")!.GetValue(dialog)! == choice, "confirmation preserves selected result " + choice);
        }
        var yesNo = (Window)Activator.CreateInstance(dialogType, ["Question", "StarPie", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No])!;
        var closing = new System.ComponentModel.CancelEventArgs();
        dialogType.GetMethod("Dialog_Closing", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(yesNo, [yesNo, closing]);
        Check(closing.Cancel, "YesNo confirmation rejects dismissal without a choice");
        var no = ((StackPanel)yesNo.FindName("ResultButtons")).Children.OfType<Button>().Last();
        no.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check((MessageBoxResult)dialogType.GetProperty("Response")!.GetValue(yesNo)! == MessageBoxResult.No, "YesNo confirmation returns No rather than accidental consent");
        foreach (var language in Enum.GetValues<LanguageCode>())
        {
            I18n.CurrentLanguage = language;
            Check(new[] { "CaptionMinimize", "CaptionMaximize", "CaptionRestore", "CaptionClose", "CaptionHideSettings", "PromptYes", "PromptNo", "PromptSavedTitle", "PromptSavedMessage" }
                .All(key => !string.IsNullOrWhiteSpace(I18n.T(key)) && I18n.T(key) != key), "caption and prompt strings resolve " + language);
        }
        var released = ClosedPromptReference(dialogType);
        for (int i = 0; i < 3 && released.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Check(!released.IsAlive, "closed prompt and caption are not retained by language listeners");
        static IEnumerable<FrameworkElement> Elements(FrameworkElement root)
        {
            yield return root;
            foreach (object child in LogicalTreeHelper.GetChildren(root))
                if (child is FrameworkElement element) foreach (var descendant in Elements(element)) yield return descendant;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ClosedPromptReference(Type type)
    {
        var dialog = (Window)Activator.CreateInstance(type, ["Lifetime", "StarPie", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.OK])!;
        var reference = new WeakReference(dialog);
        dialog.Close();
        return reference;
    }

    private static void RunCornersFontsAndLayerTests()
    {
        var config = new AppConfig { AutoCheckUpdate = false, SelectedArtStyleId = "grove" };
        SetConfig(config);
        var window = new Window { Content = new TextBlock { Text = "System UI font" }, Width = 460, Height = 200 };
        AppThemeManager.ApplyTheme(window, "Light");
        var border = (Border)window.Content;
        border.Measure(new Size(460, 200)); border.Arrange(new Rect(0, 0, 460, 200)); border.UpdateLayout();
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(window);
        Check(chrome.CornerRadius.TopLeft > 0 && border.CornerRadius.TopLeft > 0 && border.Clip != null && !border.Clip.FillContains(new Point(0, 0)),
            "restored host window has matching rounded native and client bounds");
        window.WindowState = WindowState.Maximized;
        AppThemeManager.ApplyTheme(window, "Light");
        Check(chrome.CornerRadius.TopLeft == 0 && border.CornerRadius.TopLeft == 0, "maximized host window keeps square work-area bounds");
        window.WindowState = WindowState.Normal;
        AppThemeManager.ApplyTheme(window, "Light");
        Check(chrome.CornerRadius.TopLeft > 0, "restoring a window restores rounded corners");

        var uiFont = typeof(AppConfig).GetProperty("UiFontFamily");
        Check(uiFont != null, "configuration exposes an additive UI font preference");
        if (uiFont != null)
        {
            Check((string)uiFont.GetValue(config)! == "System", "UI font follows Windows by default");
            window.Resources[SystemFonts.MessageFontFamilyKey] = new FontFamily("Georgia");
            AppThemeManager.ApplyTheme(window, "Light");
            Check(window.FontFamily.Source == "Georgia", "UI font resolves the Windows dynamic resource rather than a fixed family");
            window.Resources[SystemFonts.MessageFontFamilyKey] = new FontFamily("Arial");
            Check(window.FontFamily.Source == "Arial", "an open host window follows system font resource changes");
            uiFont.SetValue(config, "Consolas");
            AppThemeManager.ApplyTheme(window, "Light");
            Check(window.FontFamily.Source == "Consolas", "an explicit UI font remains independent of the theme");
            uiFont.SetValue(config, "Theme");
            AppThemeManager.ApplyTheme(window, "Light");
            Check(window.FontFamily.Source == ArtStyleCatalog.Find(config, "grove")!.FontFamily, "theme typography remains an explicit UI option");
            uiFont.SetValue(config, "file:///outside/font.ttf");
            AppThemeManager.ApplyTheme(window, "Light");
            Check(window.FontFamily.Source == "Arial", "invalid external UI font paths fall back without loading files");
            uiFont.SetValue(config, "Consolas");
            var copy = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config))!;
            Check((string)uiFont.GetValue(copy)! == "Consolas", "UI font preference survives full config roundtrip");
            uiFont.SetValue(config, "System");
            var settings = new SettingsWindow();
            try
            {
                var selector = (ComboBox)settings.FindName("UiFontFamilyComboBox");
                Check(selector.Items.Count == 2 && (string)uiFont.GetValue(config)! == "System", "opening settings neither enumerates all UI fonts nor rewrites the preference");
                string wheelFont = config.WheelFontFamily;
                selector.SelectedItem = selector.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == "Theme");
                Check((string)uiFont.GetValue(config)! == "Theme" && settings.FontFamily.Source == ArtStyleCatalog.Find(config, "grove")!.FontFamily,
                    "the real UI selector immediately applies theme typography");
                selector.SelectedItem = selector.Items.OfType<ComboBoxItem>().Single(item => item.Tag?.ToString() == "System");
                Check((string)uiFont.GetValue(config)! == "System" && settings.FontFamily.Source == SystemFonts.MessageFontFamily.Source,
                    "the real UI selector returns to Windows typography");
                Check(config.WheelFontFamily == wheelFont, "UI font selection preserves the separate wheel font preference");
            }
            finally
            {
                typeof(SettingsWindow).GetField("_isClosingForRelease", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(settings, true);
                settings.Close();
            }
        }
        window.Close();

        List<ActionItem> LayerActions(string prefix) => Enumerable.Range(0, 8).Select(i => new ActionItem
        {
            Name = prefix + i, LayoutMode = "TextOnly",
            SubActions = [new ActionItem { Name = "Sub A", LayoutMode = "TextOnly" }, new ActionItem { Name = "Sub B", LayoutMode = "TextOnly" }]
        }).ToList();
        var first = new WheelLayer { Name = "First", Actions = LayerActions("First ") };
        var second = new WheelLayer { Name = "Second", Actions = LayerActions("Second ") };
        var profile = new WheelProfile { Layers = [first, second], ActiveLayerIndex = 0, Actions = first.Actions };
        config = new AppConfig { EnableMultiTier = true, SubmenuStyle = "Wheel", AutoExpandSubRingsOnPopup = true,
            IconLayoutMode = "TextOnly", ShowText = true, ShowLayerIndicator = false, Profiles = [profile] };
        SetConfig(config);
        var radial = new RadialWindow(new Point(400, 400), profile);
        TextBlock[] SubLabels() => ((IEnumerable)typeof(RadialWindow).GetField("_subContentContainers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(radial)!)
            .Cast<Grid>().SelectMany(grid => grid.Children.OfType<StackPanel>()).SelectMany(panel => panel.Children.OfType<TextBlock>()).ToArray();
        try
        {
            foreach (int index in new[] { 1, 0, 1, 0 })
            {
                radial.SwitchToLayer(index);
                var before = SubLabels().Select(text => text.FontWeight).ToArray();
                Check(before.Length == 16 && before.All(weight => weight == FontWeights.Medium), "switching to layer " + index + " creates normal-weight secondary labels before mouse movement");
                radial.HighlightSector(-1, -1, false);
                Check(SubLabels().Select(text => text.FontWeight).SequenceEqual(before), "mouse movement does not change unselected secondary weights on layer " + index);
            }
            radial.HighlightSector(0, 0, true);
            Check(SubLabels().Count(text => text.FontWeight == FontWeights.Bold) == 1, "only the hovered secondary label becomes bold");
            radial.HighlightSector(-1, -1, false);
            Check(SubLabels().All(text => text.FontWeight == FontWeights.Medium), "leaving a secondary label restores the same normal weight");
        }
        finally { radial.Close(); }
        config.SubmenuStyle = "Fan";
        var fan = new RadialWindow(new Point(400, 400), profile);
        try
        {
            typeof(RadialWindow).GetMethod("RenderFanSubtier", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(fan, [0]);
            var containers = (IEnumerable)typeof(RadialWindow).GetField("_subContentContainers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fan)!;
            var labels = containers.Cast<Grid>().SelectMany(grid => grid.Children.OfType<StackPanel>()).SelectMany(panel => panel.Children.OfType<TextBlock>()).ToArray();
            Check(labels.Length == 2 && labels.All(text => text.FontWeight == FontWeights.Medium), "fan secondary labels use the same normal weight on first construction");
        }
        finally { fan.Close(); }
    }

    private static void RenderStyles()
    {
        I18n.CurrentLanguage=LanguageCode.ZhCn;
        var folder=Path.GetFullPath("artifacts/art-styles"); Directory.CreateDirectory(folder);
        var gallery=new StackPanel { Orientation=Orientation.Horizontal };
        foreach (var profile in ArtStyleCatalog.GetAll(new AppConfig()))
        {
            SetConfig(new AppConfig { SelectedArtStyleId=profile.Id });
            var studio=new ThemeStudio { Width=440 };
            var surface=new Border { Background=ArtStyleResources.Brush(profile.Colors.Background),Padding=new Thickness(15),Child=studio };
            ArtStyleResources.ApplyPreview(surface,profile);
            Capture(surface,470,920,Path.Combine(folder,profile.Id+"-studio.png"));
            var tile=new Border { Width=300,Padding=new Thickness(20),Background=ArtStyleResources.Brush(profile.Colors.Background) };
            var stack=new StackPanel();
            stack.Children.Add(new TextBlock { Text=ArtStyleText.Name(profile),FontSize=24,FontWeight=FontWeights.SemiBold,Foreground=ArtStyleResources.Brush(profile.Colors.Text),Margin=new Thickness(0,0,0,18) });
            stack.Children.Add(ArtStyleSamples.Wheel(profile,260));
            stack.Children.Add(new TextBlock { Text=ArtStyleText.Get(profile.Id+".Desc"),TextWrapping=TextWrapping.Wrap,FontSize=14,Foreground=ArtStyleResources.Brush(profile.Colors.Muted),Margin=new Thickness(0,20,0,0) });
            tile.Child=stack; gallery.Children.Add(tile);
        }
        Capture(gallery,1500,430,Path.Combine(folder,"style-gallery.png"));
        SetConfig(new AppConfig { SelectedArtStyleId="obsidian" });
        var editor=new ThemeStudio { Width=440 };
        typeof(ThemeStudio).GetMethod("BeginEdit",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(editor,[true]);
        ((Expander)editor.FindName("AdvancedPanel")).IsExpanded=true;
        Capture(editor,440,1900,Path.Combine(folder,"theme-editor.png"));
    }
    private static void Capture(FrameworkElement visual,int width,int height,string path)
    {
        visual.Measure(new Size(width,height)); visual.Arrange(new Rect(0,0,width,height)); visual.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        var background=new DrawingVisual(); using (var dc=background.RenderOpen()) dc.DrawRectangle(visual.TryFindResource("WindowBackgroundBrush") as Brush ?? Brushes.White,null,new Rect(0,0,width,height));
        bitmap.Render(background); bitmap.Render(visual);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(path); encoder.Save(stream);
    }
    private static void RunRendererTests()
    {
        var c = JsonSerializer.Deserialize<AppConfig>("{\"SelectedArtStyleId\":\"paper\"}")!;
        SetConfig(c);
        var factory = typeof(StyleRendererFactory).GetMethods().FirstOrDefault(m => m.Name == "CreateRenderer" && m.GetParameters().Length == 3);
        Check(factory != null, "wheel factory supports art style and tier context");
        if (factory == null) return;
        IRadialStyleRenderer Create(bool sub) => (IRadialStyleRenderer)factory.Invoke(null, ["ClassicRing", c, sub])!;
        var wheel = Create(false); wheel.Initialize("Light",c);
        Check(((SolidColorBrush)wheel.DefaultSectorBrush).Color.ToString() == "#FFFFFCF5", "wheel uses paper palette");
        Check(((SolidColorBrush)wheel.DefaultSectorBrush).IsFrozen, "wheel brushes frozen");
        var path = new System.Windows.Shapes.Path(); wheel.ApplySectorHighlight(path,true);
        Check(path.Effect == null || path.Effect.IsFrozen, "wheel effect frozen");
        c.ArtStyleFollowsWheel = false;
        Check(Create(false).GetType().Name == "ClassicRingRenderer", "follow disabled retains legacy renderer");
        c.ArtStyleFollowsWheel = true;
        c.UseIndependentSubWheelTheme = true;
        Check(Create(true).GetType().Name == "ClassicRingRenderer", "independent secondary appearance preserved");
        c.UseIndependentSubWheelTheme = false;
        Check(Create(true).GetType().Name == "ArtStyleRenderer", "secondary inherits whole style");
        var custom = Call("ArtStyleService","Import",c,(string)Call("ArtStyleService","Export",All(c)[0]));
        Put(custom,"DecorationStrength",0d); Call("ArtStyleService","Save",c,custom);
        Call("ArtStyleService","Select",c,Value(custom,"Id"),true);
        wheel = Create(false); wheel.Initialize("Light",c);
        var canvas = new Canvas(); wheel.RenderDecorations(canvas,new Grid(),100,100,80,30,0);
        Check(canvas.Children.Count == 0,"zero decoration strength draws no decoration");
        c.SelectedArtStyleId = "obsidian";
        c.ShowText = true; c.IconLayoutMode = "TextOnly";
        var actions = Enumerable.Range(0,8).Select(i => new ActionItem { Name = "Action " + i, Type = "Hotkey", Parameter = "CTRL+C" }).ToList();
        actions[1].CustomTextColor = "#FFAA00";
        actions[0].SubActions = [new ActionItem { Name = "Sub", CustomTextColor = "#FFAA00", LayoutMode = "TextOnly" }];
        var profile = new WheelProfile { Actions = actions, SectorCount = 8 };
        c.Profiles = [profile];
        var radial = new RadialWindow(new Point(400,400),profile);
        typeof(RadialWindow).GetMethod("RebuildVisualsFromCurrentConfiguration",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(radial,[ConfigManager.ConfigurationRevision]);
        var texts = (IEnumerable)typeof(RadialWindow).GetField("_contentTextBlocks",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(radial)!;
        var textItems = texts.Cast<TextBlock?>().ToArray();
        radial.HighlightSector(0,-1,false);
        Check(((SolidColorBrush)textItems[0]!.Foreground).Color.ToString() == "#FF102421", "real wheel uses readable Obsidian hover text");
        radial.HighlightSector(1,-1,false);
        Check(((SolidColorBrush)textItems[1]!.Foreground).Color.ToString() == "#FFFFAA00", "real wheel preserves per-action color on hover");
        radial.HighlightSector(0,0,true);
        var containers = (IEnumerable)typeof(RadialWindow).GetField("_subContentContainers",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(radial)!;
        var subText = containers.Cast<Grid>().SelectMany(g=>g.Children.OfType<StackPanel>()).SelectMany(s=>s.Children.OfType<TextBlock>()).FirstOrDefault();
        Check(subText != null && ((SolidColorBrush)subText.Foreground).Color.ToString() == "#FFFFAA00","real secondary wheel preserves action color on hover");
        radial.Close();
    }
    private static int Summary() { Console.WriteLine($"Art style tests: {passed} passed, {failures} failed"); return failures == 0 ? 0 : 1; }
    private static object Call(string type, string method, params object[] args)
    {
        var target = typeof(AppConfig).Assembly.GetType("WinPieGestures.ArtStyles." + type)!;
        try { return target.GetMethods().Single(m => m.Name == method && m.GetParameters().Length == args.Length).Invoke(null, args)!; }
        catch (TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
    }
    private static object Value(object item, string name) => item.GetType().GetProperty(name)!.GetValue(item)!;
    private static void Put(object item, string name, object value) => item.GetType().GetProperty(name)!.SetValue(item, value);
    private static object[] All(AppConfig c) => ((IEnumerable)Call("ArtStyleCatalog", "GetAll", c)).Cast<object>().ToArray();
    private static void Reject(AppConfig c, string json, string name)
    {
        int before = All(c).Length;
        try { Call("ArtStyleService", "Import", c, json); Check(false, name); }
        catch (InvalidDataException) { Check(All(c).Length == before, name + " without mutation"); }
    }
    private static void RunDataTests()
    {
        bool exists = typeof(AppConfig).Assembly.GetType("WinPieGestures.ArtStyles.ArtStyleCatalog") != null;
        Check(exists, "five-style catalog exists");
        if (!exists) return;
        var c = new AppConfig();
        var profiles = All(c);
        Check(profiles.Length == 5, "five built-in complete styles");
        Put(Value(profiles[0], "Colors"), "Accent", "#123456");
        Check((string)Value(Value(All(c)[0], "Colors"), "Accent") != "#123456", "catalog copy is deeply isolated");
        foreach (var profile in All(c))
        {
            Check(!((IEnumerable)Call("ArtStyleResolver", "Validate", profile)).Cast<object>().Any(), "valid preset " + Value(profile, "Id"));
            string json = (string)Call("ArtStyleService", "Export", profile);
            var imported = Call("ArtStyleService", "Import", c, json);
            Check((string)Value(imported, "Id") != (string)Value(profile, "Id"), "import has independent ID " + Value(profile, "Id"));
            Check(JsonSerializer.Serialize(Value(imported, "Colors")) == JsonSerializer.Serialize(Value(profile, "Colors")), "palette roundtrip " + Value(profile, "Id"));
        }
        string original = (string)Call("ArtStyleService", "Export", All(c)[0]);
        var bad = JsonNode.Parse(original)!;
        bad["schemaVersion"] = 999; Reject(c, bad.ToJsonString(), "reject future format");
        bad = JsonNode.Parse(original)!; bad["theme"]!["colors"]!["accent"] = "not-a-color"; Reject(c, bad.ToJsonString(), "reject invalid color");
        bad = JsonNode.Parse(original)!; bad["theme"]!["cornerRadius"] = 1000; Reject(c, bad.ToJsonString(), "reject out-of-range radius");
        bad = JsonNode.Parse(original)!; bad["theme"]!["colors"] = null; Reject(c, bad.ToJsonString(), "reject null palette");
        bad = JsonNode.Parse(original)!; bad["theme"] = null; Reject(c, bad.ToJsonString(), "reject null theme");
        Reject(c, "{}", "reject missing envelope");
        Reject(c, new string(' ', 65537), "reject oversized file");
        Reject(c, "{\"schemaVersion\":1,\"theme\":{\"name\":\"Unicode\"},\"ignored\":\""+new string('雪',23000)+"\"}", "reject oversized UTF-8 file");
        var minimal = Call("ArtStyleService", "Import", c, "{\"schemaVersion\":1,\"theme\":{\"name\":\"Minimal\"}}");
        Check(Value(minimal, "Colors") != null, "missing optional fields use defaults");
        var data = All(c).Last(); Put(data, "ShadowBlur", double.NaN);
        Check(((IEnumerable)Call("ArtStyleResolver", "Validate", data)).Cast<object>().Any(), "reject non-finite value");
        double radius = c.WheelRadius;
        string actions = JsonSerializer.Serialize(c.Profiles);
        Call("ArtStyleService", "Select", c, "paper", true);
        Check(c.WheelRadius == radius && JsonSerializer.Serialize(c.Profiles) == actions, "theme selection preserves wheel geometry/actions");
        var custom = Call("ArtStyleService", "Import", c, original);
        Call("ArtStyleService", "Select", c, (string)Value(custom,"Id"), true);
        Call("ArtStyleService", "Delete", c, (string)Value(custom,"Id"));
        Check((string)Value(c,"SelectedArtStyleId") == "paper", "deleting active custom style selects built-in default");
        string configJson = JsonSerializer.Serialize(c);
        Check(All(JsonSerializer.Deserialize<AppConfig>(configJson)!).Length == All(c).Length, "user themes survive full config roundtrip");
        Check(Call("ArtStyleResolver", "Resolve", new AppConfig()) == null, "old config retains legacy appearance");
        Put(c,"SelectedArtStyleId","missing");
        Check((string)Value(Call("ArtStyleResolver","Resolve",c),"Id") == "paper", "missing theme uses safe fallback");
    }
    private static void SetConfig(AppConfig config) => typeof(ConfigManager).GetProperty("CurrentConfig")!.SetValue(null, config);
    private static void Check(bool condition, string name)
    {
        if (condition) { passed++; Console.WriteLine("PASS " + name); }
        else { failures++; Console.WriteLine("FAIL " + name); }
    }
}
