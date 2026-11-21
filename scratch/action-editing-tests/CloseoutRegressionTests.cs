using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinPieGestures;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace ActionEditingTests;

internal static class CloseoutRegressionTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void InheritedBooleanWritePersists()
    {
        var (config, global, local) = InheritedConfiguration();
        var window = Window(local);
        var checkbox = new CheckBox { IsChecked = true };
        Set(window, "FocusLaunchStandardUserCheckBox", checkbox);
        string originalGlobal = JsonSerializer.Serialize(global);
        string originalLocal = JsonSerializer.Serialize(local);
        _ = Invoke(window, "GetCurrentFocusActionItem", false);
        Equal(originalLocal, JsonSerializer.Serialize(local), "reading inherited child does not materialize it");

        Invoke(window, "FocusLaunchStandardUserCheckBox_Changed", checkbox, new RoutedEventArgs());
        Require(local.Actions[0].SubActions.Count == 2, "checkbox edit materializes the complete inherited sector");
        Require(local.Actions[0].SubActions[0].RunAsStandardUser, "checkbox commits to the local child");
        Require(!local.Actions[0].SubActions[1].RunAsStandardUser, "neighbor child remains unchanged");
        Equal(originalGlobal, JsonSerializer.Serialize(global), "checkbox never changes Global");
        var loaded = Reload(config);
        Require(loaded.Profiles[1].GetEffectiveAction(0, 0)!.RunAsStandardUser, "explicit true survives JSON reload");

        var loadedWindow = Window(loaded.Profiles[1]);
        checkbox = new CheckBox { IsChecked = false };
        Set(loadedWindow, "FocusLaunchStandardUserCheckBox", checkbox);
        Invoke(loadedWindow, "FocusLaunchStandardUserCheckBox_Changed", checkbox, new RoutedEventArgs());
        Require(!Reload(loaded).Profiles[1].GetEffectiveAction(0, 0)!.RunAsStandardUser, "explicit false survives JSON reload");
    }

    internal static void InheritedUrlPresetPersists()
    {
        var (config, global, local) = InheritedConfiguration();
        var window = Window(local);
        string originalGlobal = JsonSerializer.Serialize(global);
        Invoke(window, "SetFocusWebUrl", "https://example.invalid/closeout", "Fixture URL");
        Require(local.Actions[0].SubActions.Count == 2, "URL preset materializes inherited children");
        Equal("WebUrl", local.Actions[0].SubActions[0].Type, "URL preset commits the action type");
        Equal("https://example.invalid/closeout", local.Actions[0].SubActions[0].Parameter, "URL preset commits to local child");
        Equal("F8", local.Actions[0].SubActions[1].Parameter, "URL preset preserves the neighbor");
        Equal(originalGlobal, JsonSerializer.Serialize(global), "URL preset never changes Global");
        Equal("https://example.invalid/closeout", Reload(config).Profiles[1].GetEffectiveAction(0, 0)!.Parameter, "URL preset survives JSON reload");
    }

    internal static void InheritedFolderPresetPersists()
    {
        var (config, global, local) = InheritedConfiguration();
        var window = Window(local);
        string originalGlobal = JsonSerializer.Serialize(global);
        Invoke(window, "SetFocusFolder", "fixture-folder", "Fixture folder");
        Require(local.Actions[0].SubActions.Count == 2, "folder preset materializes inherited children");
        Equal("Folder", local.Actions[0].SubActions[0].Type, "folder preset commits its type");
        Equal("fixture-folder", Reload(config).Profiles[1].GetEffectiveAction(0, 0)!.Parameter, "folder preset survives JSON reload");
        Equal(originalGlobal, JsonSerializer.Serialize(global), "folder preset never changes Global");
    }

    internal static void RestoredCenterTracksGlobalAfterReload()
    {
        var (config, global, local) = InheritedConfiguration();
        var window = Window(local);
        Set(window, "_selectedSlotIndex", -1);
        Set(window, "_selectedSubActionIndex", null);
        string originalGlobal = JsonSerializer.Serialize(global);
        window.RestoreCenterInheritance(local);
        Equal("F10", local.GetEffectiveCenterAction(global)!.Parameter, "restored center resolves the actual Global action");
        Equal(originalGlobal, JsonSerializer.Serialize(global), "restoring center never changes Global");

        global.CenterAction!.Parameter = "F11";
        global.SyncActiveLayerFromRootProperties();
        Equal("F11", local.GetEffectiveCenterAction(global)!.Parameter, "restored center dynamically tracks Global");
        var loaded = Reload(config);
        Equal("F11", loaded.Profiles[1].GetEffectiveCenterAction(loaded.Profiles[0])!.Parameter, "center inheritance survives JSON reload");
        loaded.Profiles[0].CenterAction!.Parameter = "F12";
        loaded.Profiles[0].SyncActiveLayerFromRootProperties();
        Equal("F12", loaded.Profiles[1].GetEffectiveCenterAction(loaded.Profiles[0])!.Parameter, "reloaded center still dynamically tracks Global");
    }

    internal static void BreadcrumbUsesSelectedLanguage()
    {
        var oldLanguage = I18n.CurrentLanguage;
        try
        {
            var outputs = new Dictionary<LanguageCode, string>();
            foreach (LanguageCode language in Enum.GetValues<LanguageCode>())
            {
                I18n.CurrentLanguage = language;
                var (_, _, local) = InheritedConfiguration();
                var window = Window(local);
                window.UpdateActionBreadcrumb();
                string text = Get<TextBlock>(window, "FocusActionBreadcrumbText").Text;
                Require(text.Contains("Fixture child", StringComparison.Ordinal), "breadcrumb identifies the selected child");
                AssertLocalized(text, language, "breadcrumb");
                outputs[language] = text;
            }
            Require(outputs.Values.Distinct().Count() == 4, "all four breadcrumb languages have distinct translations");
        }
        finally { I18n.CurrentLanguage = oldLanguage; }
    }

    internal static void ClipboardAndHistoryStatusUseSelectedLanguage()
    {
        var oldLanguage = I18n.CurrentLanguage;
        try
        {
            var copies = new List<string>();
            var undos = new List<string>();
            var redos = new List<string>();
            foreach (LanguageCode language in Enum.GetValues<LanguageCode>())
            {
                I18n.CurrentLanguage = language;
                var (_, _, local) = InheritedConfiguration();
                var window = Window(local);
                window.CopyCurrentSelection();
                string copied = Get<TextBlock>(window, "MappingsCurrentEditIndicator").Text;
                Require(copied.Contains("Fixture child", StringComparison.Ordinal), "copy status identifies the action");
                AssertLocalized(copied, language, "copy status");
                copies.Add(copied);
                AssertLocalized(Get<Button>(window, "FocusPasteActionBtn").ToolTip?.ToString() ?? "", language, "paste tooltip");

                var before = new ActionSnapshot(local.Actions[0]);
                local.Actions[0] = new ActionItem { Type = "Hotkey", Name = "Fixture changed", Parameter = "F5" };
                var after = new ActionSnapshot(local.Actions[0]);
                Get<ActionTransactionHistory>(window, "_transactionHistory").Push(new SingleSectorTransaction(
                    "Fixture edit", new ActionAddress(local.ProcessName, 0, 0, null), before, after));
                window.ExecuteUndo();
                string undone = Get<TextBlock>(window, "MappingsCurrentEditIndicator").Text;
                AssertLocalized(undone, language, "undo status");
                undos.Add(undone);
                AssertLocalized(Get<Button>(window, "FocusRedoSubActionsBtn").ToolTip?.ToString() ?? "", language, "redo tooltip");
                window.ExecuteRedo();
                string redone = Get<TextBlock>(window, "MappingsCurrentEditIndicator").Text;
                AssertLocalized(redone, language, "redo status");
                redos.Add(redone);
            }
            Require(copies.Distinct().Count() == 4, "copy status translates to all four languages");
            Require(undos.Distinct().Count() == 4, "undo status translates to all four languages");
            // The Chinese term for redo can legitimately be identical in Simplified and Traditional Chinese.
            Require(redos.Distinct().Count() >= 3, "redo status translates to English and Japanese as well as Chinese");
        }
        finally { ActionClipboard.Clear(); I18n.CurrentLanguage = oldLanguage; }
    }

    internal static void ActionShortcutsRespectPageAndTextFocus()
    {
        try
        {
            var (_, _, local) = InheritedConfiguration();
            var window = Window(local);
            var mappings = new Grid { Visibility = Visibility.Collapsed };
            Set(window, "MappingsSettingsGrid", mappings);
            ActionClipboard.CopySingleAction(new ActionItem { Name = "Fixture clipboard", Type = "Hotkey", Parameter = "F5" });
            string before = JsonSerializer.Serialize(local);
            foreach (Key key in new[] { Key.V, Key.C, Key.Z, Key.Y })
            {
                bool handled = (bool)Invoke(window, "TryHandleActionEditingShortcut", key, ModifierKeys.Control, new Button())!;
                Require(!handled, $"hidden mappings page does not consume Ctrl+{key}");
                Equal(before, JsonSerializer.Serialize(local), $"Ctrl+{key} on another page cannot edit hidden actions");
                Equal("Fixture clipboard", ActionClipboard.Paste().Action!.Name, "hidden copy cannot replace the internal clipboard");
            }

            mappings.Visibility = Visibility.Visible;
            foreach (Key key in new[] { Key.V, Key.C, Key.Z, Key.Y })
            {
                Require(!(bool)Invoke(window, "TryHandleActionEditingShortcut", key, ModifierKeys.Control, new TextBox())!, $"text input retains Ctrl+{key}");
                Equal(before, JsonSerializer.Serialize(local), "text editing shortcuts do not edit actions");
            }
            Require(!(bool)Invoke(window, "TryHandleActionEditingShortcut", Key.V, ModifierKeys.None, new Button())!, "plain V is not an action shortcut");
            Require((bool)Invoke(window, "TryHandleActionEditingShortcut", Key.V, ModifierKeys.Control, new Button())!, "visible mappings page handles action paste");
            Equal("F5", local.GetEffectiveAction(0, 0)!.Parameter, "visible action paste reaches the selected child");
            Require((bool)Invoke(window, "TryHandleActionEditingShortcut", Key.Z, ModifierKeys.Control, new Button())!, "visible mappings page handles undo");
            Equal("fixture.exe", local.GetEffectiveAction(0, 0)!.Parameter, "action undo restores the inherited child");
            Require((bool)Invoke(window, "TryHandleActionEditingShortcut", Key.Y, ModifierKeys.Control, new Button())!, "visible mappings page handles redo");
            Equal("F5", local.GetEffectiveAction(0, 0)!.Parameter, "action redo restores the pasted child");
        }
        finally { ActionClipboard.Clear(); }
    }

    internal static void InheritedSectorPasteCancellationIsNonMutating()
    {
        try
        {
            var (config, _, local) = InheritedConfiguration();
            var window = Window(local);
            Set(window, "_selectedSubActionIndex", null);
            int observedCount = -1;
            Set(window, "_confirmSectorBundleReplacementForTesting", new Func<int, bool>(count => { observedCount = count; return false; }));
            ActionClipboard.CopySectorBundle(new ActionItem { Name = "Fixture replacement", Type = "Hotkey", Parameter = "F5" });
            string before = JsonSerializer.Serialize(config);
            window.PasteToCurrentSelection();
            Equal(2, observedCount, "replacement confirmation counts effective inherited children");
            Equal(before, JsonSerializer.Serialize(config), "cancelled paste preserves every local and Global field");
            Equal(0, Get<ActionTransactionHistory>(window, "_transactionHistory").UndoCount, "cancelled paste creates no history");
            Require(Get<object?>(window, "_autoSaveDebounceTimer") == null, "cancelled paste schedules no save");
        }
        finally { ActionClipboard.Clear(); }
    }

    internal static void InheritedPluginParameterWritesLazily()
    {
        var (config, global, local) = InheritedConfiguration();
        global.Actions[0].SubActions[0].Type = "Plugin";
        global.Actions[0].SubActions[0].ExtensionData = new() { ["level"] = "10" };
        var window = Window(local);
        var source = (ActionItem)Invoke(window, "GetCurrentFocusActionItem", false)!;
        string before = JsonSerializer.Serialize(local);
        var targetType = typeof(SettingsWindow).Assembly.GetType("WinPieGestures.Plugins.ActionItemParameterTarget")!;
        var constructor = targetType.GetConstructors(Members).SingleOrDefault(c => c.GetParameters().Length == 2);
        Require(constructor != null, "plugin parameter target provides a lazy writable-source resolver");
        int writeSourceCalls = 0;
        Func<ActionItem?> writableSource = () => { writeSourceCalls++; return (ActionItem?)Invoke(window, "GetCurrentFocusActionItem", true); };
        object target = constructor!.Invoke(new object?[] { source, writableSource });
        var stored = (IReadOnlyDictionary<string, string>?)targetType.GetProperty("Stored")!.GetValue(target);
        Equal("10", stored!["level"], "parameter rendering reads the inherited stored value");
        Equal(before, JsonSerializer.Serialize(local), "constructing and reading parameter target never materializes inheritance");
        Require(!(bool)targetType.GetMethod("Write")!.Invoke(target, new object?[] { "level", "10" })!, "unchanged parameter write reports no change");
        Require(!(bool)targetType.GetMethod("Write")!.Invoke(target, new object?[] { "absent", null })!, "deleting absent parameter reports no change");
        Equal(0, writeSourceCalls, "unchanged and absent values never resolve a writable source");
        Equal(before, JsonSerializer.Serialize(local), "no-op parameter edits leave inheritance untouched");
        Require((bool)targetType.GetMethod("Write")!.Invoke(target, new object?[] { "level", "75" })!, "parameter write reports a real change");
        Equal(1, writeSourceCalls, "real parameter write resolves the writable source exactly once");
        Equal("75", local.GetEffectiveAction(0, 0)!.ExtensionData!["level"], "parameter write materializes and updates the local action");
        Equal("10", global.Actions[0].SubActions[0].ExtensionData!["level"], "parameter write never changes Global");
        Equal("75", Reload(config).Profiles[1].GetEffectiveAction(0, 0)!.ExtensionData!["level"], "plugin parameter survives JSON reload");
    }

    internal static void CenterRestorePreservesControlsAndUnknownData()
    {
        foreach (bool enabled in new[] { false, true })
        {
            var (config, global, local) = InheritedConfiguration();
            local.EnableCenterAction = enabled;
            local.CenterAction!.Extras = new() { ["future-center-data"] = JsonSerializer.SerializeToElement(new { Revision = 3, Enabled = false }) };
            local.SyncActiveLayerFromRootProperties();
            var window = Window(local);
            Set(window, "_selectedSlotIndex", -1);
            Set(window, "_selectedSubActionIndex", null);
            window.RestoreCenterInheritance(local);
            Equal(enabled, local.EnableCenterAction, "restore retains the existing local center-enable preference");
            Equal("F10", local.GetEffectiveCenterAction(global)!.Parameter, "restored center uses existing Global fallback semantics");
            Require(local.CenterAction!.Extras?.ContainsKey("future-center-data") == true, "restore preserves unknown center data");
            Equal(3, local.CenterAction.Extras!["future-center-data"].GetProperty("Revision").GetInt32(), "unknown data retains its value");

            config.EnableGlobalInheritance = false;
            Require(local.GetEffectiveCenterAction(global) == null, "inheritance master switch suppresses Global fallback");
            config.EnableGlobalInheritance = true;
            global.EnableCenterAction = false;
            global.SyncActiveLayerFromRootProperties();
            Require(local.GetEffectiveCenterAction(global) == null, "disabled Global center remains disabled");
            global.EnableCenterAction = true;
            global.SyncActiveLayerFromRootProperties();
            var loaded = Reload(config);
            Equal(enabled, loaded.Profiles[1].EnableCenterAction, "center-enable preference survives JSON reload");
            Equal(3, loaded.Profiles[1].CenterAction!.Extras!["future-center-data"].GetProperty("Revision").GetInt32(), "unknown center data survives JSON reload");
        }
    }

    internal static void FocusPluginFormUsesLazyWritableTarget()
    {
        var (_, global, local) = InheritedConfiguration();
        global.Actions[0].SubActions[0].Type = "Plugin";
        global.Actions[0].SubActions[0].ExtensionData = new() { ["level"] = "10" };
        var window = Window(local);
        Set(window, "FocusPluginParamsPanel", new StackPanel());
        string before = JsonSerializer.Serialize(local);
        object form = Invoke(window, "EnsureFocusPluginParameterForm")!;
        var targetSource = (Delegate)form.GetType().GetField("_targetSource", Members)!.GetValue(form)!;
        object target = targetSource.DynamicInvoke()!;
        var stored = (IReadOnlyDictionary<string, string>?)target.GetType().GetProperty("Stored")!.GetValue(target);
        Equal("10", stored!["level"], "production form reads inherited parameters");
        Equal(before, JsonSerializer.Serialize(local), "production form target creation is read-only");
        Require((bool)target.GetType().GetMethod("Write")!.Invoke(target, new object?[] { "level", "75" })!, "production form target commits a changed parameter");
        Equal("75", local.GetEffectiveAction(0, 0)!.ExtensionData!["level"], "production form resolver writes the local child");
        Equal("10", global.Actions[0].SubActions[0].ExtensionData!["level"], "production form never changes Global");
    }

    internal static void CopyInheritedPrimaryUsesEffectiveAction()
    {
        try
        {
            var (config, global, local) = InheritedConfiguration();
            global.Actions[1].SubActions.Add(new ActionItem { Type = "Hotkey", Name = "Target child", Parameter = "F3" });
            local.Actions[1] = new ActionItem { Type = "Inherit", Name = "", SubActions = new() };
            var window = Window(local);
            Set(window, "_selectedSubActionIndex", null);
            string originalLocal = JsonSerializer.Serialize(local);
            string originalGlobal = JsonSerializer.Serialize(global);

            window.CopyCurrentAction();
            Equal(originalLocal, JsonSerializer.Serialize(local), "single-action copying does not materialize the inherited source");
            var single = ActionClipboard.Paste();
            Equal("Hotkey", single.Action!.Type, "single-action copy captures the effective type rather than the inherit marker");
            Equal("F6", single.Action.Parameter, "single-action copy captures its source Global slot");
            Equal(0, single.Action.SubActions.Count, "single-action copy excludes the source children");

            window.CopyEntireSector();
            Equal(originalLocal, JsonSerializer.Serialize(local), "sector copy does not materialize the inherited source");
            Set(window, "_selectedSlotIndex", 1);
            int confirmationCount = -1;
            Set(window, "_confirmSectorBundleReplacementForTesting", new Func<int, bool>(count => { confirmationCount = count; return false; }));
            window.PasteToCurrentSelection();
            Equal(1, confirmationCount, "cancellation checks the different target Global slot");
            Equal(originalLocal, JsonSerializer.Serialize(local), "cancelled sector paste materializes neither source nor target");

            Set(window, "_selectedSlotIndex", 0);
            window.CopyCurrentAction();
            Set(window, "_selectedSlotIndex", 1);
            window.PasteToCurrentSelection();
            Equal("F6", local.Actions[1].Parameter, "single-action paste copies source content instead of inheriting the target F2");
            Equal("Hotkey", local.Actions[1].Type, "single-action paste is a concrete local action");
            Equal("F3", local.Actions[1].SubActions.Single().Parameter, "single-action paste preserves the target effective children");
            Equal("Inherit", local.Actions[0].Type, "copy never materializes its inherited source");
            Equal(originalGlobal, JsonSerializer.Serialize(global), "copy, cancellation and paste never alter Global");
            Equal("F6", Reload(config).Profiles[1].GetEffectiveAction(1)!.Parameter, "copied source content survives JSON reload");
        }
        finally { ActionClipboard.Clear(); }
    }

    internal static void CopyInheritedCenterUsesEffectiveAction()
    {
        try
        {
            foreach (string method in new[] { "CopyCurrentAction", "CopyCurrentSelection" })
            {
                var (config, global, local) = InheritedConfiguration();
                local.Actions[1] = new ActionItem { Type = "Inherit", Name = "", SubActions = new() };
                var window = Window(local);
                Set(window, "_selectedSlotIndex", -1);
                Set(window, "_selectedSubActionIndex", null);
                window.RestoreCenterInheritance(local);
                string originalLocal = JsonSerializer.Serialize(local);
                string originalGlobal = JsonSerializer.Serialize(global);
                Invoke(window, method);
                Equal(originalLocal, JsonSerializer.Serialize(local), method + " does not materialize inherited center or slots");
                var copied = ActionClipboard.Paste();
                Equal("Hotkey", copied.Action!.Type, method + " captures the effective center rather than None");
                Equal("F10", copied.Action.Parameter, method + " copies the source Global center");
                Set(window, "_selectedSlotIndex", 1);
                window.PasteToCurrentSelection();
                Equal("F10", local.Actions[1].Parameter, method + " pastes Global center content rather than the target slot F2");
                Equal("None", local.CenterAction!.Type, method + " preserves the center inheritance marker");
                Equal(originalGlobal, JsonSerializer.Serialize(global), method + " never alters Global");
                Equal("F10", Reload(config).Profiles[1].GetEffectiveAction(1)!.Parameter, method + " copied center survives JSON reload");
            }
        }
        finally { ActionClipboard.Clear(); }
    }

    internal static void LocalizedDropdownRefreshDoesNotMaterializeInheritance()
    {
        var (_, global, local) = InheritedConfiguration();
        global.Actions[0].SubActions[0].Type = "Command";
        global.Actions[0].SubActions[0].CommandTerminal = "cmd";
        var window = Window(local);
        var terminal = new ComboBox { SelectedValuePath = "Tag", ItemsSource = SlotViewModel.LocalizedTerminals };
        terminal.SelectedValue = "cmd";
        Set(window, "FocusCommandTerminalComboBox", terminal);
        Set(window, "_isUiInitialized", true);
        // No dispatcher loop is run. Keep any red-state save timer well beyond the test lifetime as an extra guard.
        var saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromDays(1) };
        Set(window, "_autoSaveDebounceTimer", saveTimer);
        int changedEvents = 0;
        terminal.SelectionChanged += (_, args) => { changedEvents++; Invoke(window, "FocusCommandTerminalComboBox_SelectionChanged", terminal, args); };
        string before = JsonSerializer.Serialize(local);
        string beforeGlobal = JsonSerializer.Serialize(global);
        try
        {
            Invoke(window, "RefreshFocusLocalizedChoices");
            Require(changedEvents >= 2, "refresh actually clears and restores the bound selection");
            Equal("cmd", terminal.SelectedValue?.ToString(), "localized refresh preserves the selected terminal");
            Equal(before, JsonSerializer.Serialize(local), "real binding refresh never materializes inherited local data");
            Equal(beforeGlobal, JsonSerializer.Serialize(global), "real binding refresh never edits Global");
            Require(!Get<bool>(window, "_isUpdatingFocusUi"), "refresh restores an initially false update guard");
            Set(window, "_isUpdatingFocusUi", true);
            Invoke(window, "RefreshFocusLocalizedChoices");
            Require(Get<bool>(window, "_isUpdatingFocusUi"), "refresh preserves an already active update guard");
        }
        finally { saveTimer.Stop(); Set(window, "_isUiInitialized", false); }
    }

    internal static void InheritedCenterDragCopyPreservesTargetChildren()
    {
        var (_, global, local) = InheritedConfiguration();
        global.Actions[1].SubActions.Add(new ActionItem { Type = "Hotkey", Name = "Target child", Parameter = "F3" });
        local.Actions[1] = new ActionItem { Type = "Inherit", Name = "", SubActions = new() };
        var window = Window(local);
        Set(window, "_selectedSlotIndex", -1);
        Set(window, "_selectedSubActionIndex", null);
        window.RestoreCenterInheritance(local);
        Get<ActionTransactionHistory>(window, "_transactionHistory").Clear();
        string before = JsonSerializer.Serialize(local);
        string beforeGlobal = JsonSerializer.Serialize(global);
        Set(window, "_dragSourceSlotIndex", -1);
        window.ProcessCanvasDrop(local, new CanvasHitItem { SlotIndex = 1 }, true);
        Equal("F10", local.Actions[1].Parameter, "Ctrl-drag copies the effective inherited center");
        Equal("F3", local.Actions[1].SubActions.Single().Parameter, "Ctrl-drag from a single center preserves target children");
        Equal("None", local.CenterAction!.Type, "Ctrl-drag leaves the center inherited");
        Equal(beforeGlobal, JsonSerializer.Serialize(global), "Ctrl-drag does not change Global");
        window.ExecuteUndo();
        Equal(before, JsonSerializer.Serialize(local), "Ctrl-drag undo restores original inheritance at both ends");
        window.ExecuteRedo();
        Equal("F10", local.Actions[1].Parameter, "Ctrl-drag redo restores the copied center");
        Equal("F3", local.Actions[1].SubActions.Single().Parameter, "Ctrl-drag redo preserves target children");
    }

    internal static void InheritedCenterSwapUsesEffectiveActionsAndUndoRestoresInheritance()
    {
        var (_, global, local) = InheritedConfiguration();
        global.Actions[1].SubActions.Add(new ActionItem { Type = "Hotkey", Name = "Target child", Parameter = "F3" });
        local.Actions[1] = new ActionItem { Type = "Inherit", Name = "", SubActions = new() };
        var window = Window(local);
        Set(window, "_selectedSlotIndex", -1);
        Set(window, "_selectedSubActionIndex", null);
        window.RestoreCenterInheritance(local);
        Get<ActionTransactionHistory>(window, "_transactionHistory").Clear();
        string before = JsonSerializer.Serialize(local);
        string beforeGlobal = JsonSerializer.Serialize(global);
        Set(window, "_dragSourceSlotIndex", -1);
        window.ProcessCanvasDrop(local, new CanvasHitItem { SlotIndex = 1 }, false);
        Equal("F2", local.CenterAction!.Parameter, "swap moves the effective sector action into center");
        Equal("F10", local.Actions[1].Parameter, "swap moves the effective inherited center into the target sector");
        Equal("F3", local.Actions[1].SubActions.Single().Parameter, "swap retains sector children at their original position");
        Equal(beforeGlobal, JsonSerializer.Serialize(global), "swap never modifies Global");
        window.ExecuteUndo();
        Equal(before, JsonSerializer.Serialize(local), "swap undo restores both original inheritance markers and content");
        Equal("F10", local.GetEffectiveCenterAction(global)!.Parameter, "undo restores dynamic Global center inheritance");
        window.ExecuteRedo();
        Equal("F2", local.CenterAction!.Parameter, "swap redo restores the sector-derived center");
        Equal("F10", local.Actions[1].Parameter, "swap redo restores the center-derived sector");
    }

    internal static void AutoSaveStatusTranslationPreservesItsActualState()
    {
        var oldLanguage = I18n.CurrentLanguage;
        try
        {
            var (_, _, local) = InheritedConfiguration();
            var window = Window(local);
            var status = new TextBlock();
            Set(window, "AutoSaveStatusText", status);
            foreach (var state in new[] { (Saving: false, Success: true, Key: "AutoSaveSaved"), (Saving: true, Success: true, Key: "AutoSaveSaving"), (Saving: false, Success: false, Key: "AutoSaveFailed") })
            {
                I18n.CurrentLanguage = LanguageCode.ZhCn;
                Invoke(window, "UpdateAutoSaveStatus", state.Saving, state.Success);
                foreach (LanguageCode language in Enum.GetValues<LanguageCode>())
                {
                    I18n.CurrentLanguage = language;
                    Invoke(window, "RefreshAutoSaveStatus");
                    Equal(I18n.T(state.Key), status.Text, "language refresh preserves the actual save state: " + state.Key + "/" + language);
                    if (state.Key != "AutoSaveSaved") Require(status.Text != I18n.T("AutoSaveSaved"), "language refresh cannot turn pending or failed save into success");
                }
            }
        }
        finally { I18n.CurrentLanguage = oldLanguage; }
    }

    private static void AssertLocalized(string text, LanguageCode language, string context)
    {
        Require(!string.IsNullOrWhiteSpace(text), context + " is nonempty");
        if (language == LanguageCode.En)
            Require(!Regex.IsMatch(text, "[\\u4e00-\\u9fff]"), context + " contains no Chinese in English: " + text);
        if (language == LanguageCode.ZhTw)
            Require(!text.Contains("扇区") && !text.Contains("动作") && !text.Contains("复制") && !text.Contains("撤销") && !text.Contains("粘贴"), context + " uses Traditional Chinese: " + text);
        if (language == LanguageCode.Ja)
            Require(!text.Contains("扇区") && !text.Contains("子动作") && !text.Contains("已复制") && !text.Contains("已撤销") && !text.Contains("已重做") && !text.Contains("粘贴"), context + " uses Japanese: " + text);
    }

    private static (AppConfig Config, WheelProfile Global, WheelProfile Local) InheritedConfiguration()
    {
        var global = Profile("Global", new ActionItem
        {
            Type = "Hotkey", Name = "Fixture parent", Parameter = "F6",
            SubActions = new()
            {
                new() { Type = "Launch", Name = "Fixture child", Parameter = "fixture.exe", RunAsStandardUser = false },
                new() { Type = "Hotkey", Name = "Fixture neighbor", Parameter = "F8" }
            }
        }, "F10");
        var local = Profile("fixture.exe", new ActionItem { Type = "Inherit", Name = "", SubActions = new() }, "F9");
        var config = new AppConfig { EnableGlobalInheritance = true, Profiles = new() { global, local } };
        SetConfig(config);
        return (config, global, local);
    }

    private static WheelProfile Profile(string process, ActionItem primary, string centerParameter)
    {
        var profile = new WheelProfile
        {
            ProcessName = process, SectorCount = 4, EnableCenterAction = true,
            Actions = new() { primary, new() { Parameter = "F2" }, new() { Parameter = "F3" }, new() { Parameter = "F4" } },
            CenterAction = new() { Type = "Hotkey", Name = "Fixture center", Parameter = centerParameter }
        };
        profile.EnsureLayers();
        return profile;
    }

    private static AppConfig Reload(AppConfig config)
    {
        var loaded = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config))!;
        foreach (var profile in loaded.Profiles) profile.EnsureLayers();
        SetConfig(loaded);
        return loaded;
    }

    private static void SetConfig(AppConfig config) => typeof(ConfigManager).GetProperty("CurrentConfig")!.SetValue(null, config);

    private static SettingsWindow Window(WheelProfile profile)
    {
        var window = EditingWindowFixture.Open(profile);
        window.SelectSubAction(0, 0);
        return window;
    }

    private static void Set(SettingsWindow window, string field, object? value) => typeof(SettingsWindow).GetField(field, Members)!.SetValue(window, value);
    private static T Get<T>(SettingsWindow window, string field) => (T)typeof(SettingsWindow).GetField(field, Members)!.GetValue(window)!;
    private static object? Invoke(SettingsWindow window, string method, params object?[] args) => typeof(SettingsWindow).GetMethod(method, Members)!.Invoke(window, args);
    private static void Equal<T>(T expected, T actual, string message) => Require(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected={expected}, actual={actual}");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
