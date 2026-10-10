# Customizable Art Styles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task, inline in this session. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver five complete WPF art styles and a user-editable theme studio with instant application and portable theme files.

**Architecture:** Pure theme profiles, catalog and validation feed one service used by WPF resources and wheel renderers. A dedicated UserControl owns draft editing, while a small SettingsWindow partial owns existing preview integration. Legacy configuration remains active when no art style is selected.

**Tech Stack:** .NET 8, WPF, System.Text.Json, existing project services; no new package dependencies.

**Spec:** `docs/superpowers/specs/2026-10-05-customizable-art-styles-design.md`.

**Global constraints:** Keep wheel geometry and action mappings untouched. Freeze created Freezables. Do not run the interactive pywinauto suite (AGENTS.md §5.4). Use an isolated managed worktree. Save full verification output under ignored `artifacts/` when necessary.

---

### Task 1: Theme data and portable files

**Files:** Create `WinPieGestures/ArtStyles/ArtStyleProfile.cs`, `ArtStyleCatalog.cs`, `ArtStyleResolver.cs`, `ArtStyleService.cs`; modify `WinPieGestures/AppConfig.cs`; create `scratch/test_art_styles.csproj` and `scratch/test_art_styles.cs`.

**Interfaces:** `ArtStyleCatalog.GetAll(AppConfig)` and `Find(AppConfig,string)` return independent copies. `ArtStyleResolver.Resolve(AppConfig)` returns a validated active profile or null in legacy mode. `Validate(ArtStyleProfile)` returns field errors. `ArtStyleService.Select(AppConfig,string,bool)`, `Save(AppConfig,ArtStyleProfile)`, `Delete(AppConfig,string)`, `Import(AppConfig,string)` and `Export(ArtStyleProfile)` alter theme data only. `ArtStyleProfile.Copy()` is deep.

- [ ] Write a console regression suite that initially asserts a selected `paper` profile produces warm paper resources rather than existing light resources. Check catalog availability via reflection so the suite compiles against the unmodified host.

```csharp
var config = JsonSerializer.Deserialize<AppConfig>("{\"SelectedArtStyleId\":\"paper\"}")!;
ConfigManager.CurrentConfig = config;
var root = new System.Windows.Controls.Border();
AppThemeManager.ApplyTheme(root, "Light");
Check(((SolidColorBrush)root.Resources["WindowBackgroundBrush"]).Color.ToString() == "#FFF5F1E8", "selected paper art style is used");
```

- [ ] Run `dotnet run --project scratch/test_art_styles.csproj -c Release`. Expected: failure for missing art-style behavior.
- [ ] Implement profile and palette models with defaults, five complete styles, deep-copy catalog and validation. Support envelope `{ schemaVersion: 1, theme: ... }`, bounded file size, finite/ranged numbers, valid colors, bounded font strings and duplicate-ID import copies. Add tests for each rejecting import before production implementation of that case.
- [ ] Add direct runtime tests for clone isolation, all presets' contrast, round trips, bad colors, invalid versions, null objects, missing optional fields, out-of-range and non-finite values, duplicate IDs, deleting active styles and preservation of existing action/geometry settings.
- [ ] Run the suite and commit the data layer after its assertions pass. Resource-selection test remains red until Task 2.

### Task 2: App-wide theme resources

**Files:** Modify `WinPieGestures/AppThemeManager.cs`, `WinPieGestures/SettingsWindow.xaml`, relevant dialog XAML, `WinPieGestures/App.xaml.cs`; create `WinPieGestures/ArtStyles/ArtStyleResources.cs` and `WinPieGestures/SettingsWindow.ArtStyles.cs`.

**Interfaces:** `ArtStyleResources.Apply(FrameworkElement,ArtStyleProfile)` writes all existing semantic brushes and typography/radius/effect resources. `ArtStyleService.NotifyChanged()` applies current resources to open host windows, increments the revision and raises a weak event used by theme studio/settings. `AppThemeManager.IsEffectiveDark(string)` resolves art style metadata before legacy mode.

- [ ] Add failing tests for freezing, explicit light/dark metadata, radius/font resource updates, resetting art styles to legacy resources and theme refresh of a second open host window.
- [ ] Route `ApplyTheme` through the profile resolver; map palette to semantic resources and derived hover/active states. Replace editable generic control/card radius and effect literals with dynamic resources while preserving layout-specific values.

```csharp
var style = ArtStyleResolver.Resolve(ConfigManager.CurrentConfig);
if (style != null) { ArtStyleResources.Apply(rootElement, style); return; }
```

- [ ] Add settings partial integration to refresh UI, both previews, logo and tray. Base-mode buttons clear the active style ID. Existing config reload refreshes studio; release on window closure continues to work.
- [ ] Run suite and Release build. Expected: paper selection and resource tests pass. Commit.

### Task 3: Wheel material and decoration integration

**Files:** Modify `WinPieGestures/BaseStyleRenderer.cs`, `StyleRendererFactory.cs`, `RadialWindow.xaml.cs`, `SettingsWindow.xaml.cs`; create `WinPieGestures/ArtStyles/ArtStyleRenderer.cs`.

**Interfaces:** `StyleRendererFactory.CreateRenderer(string, AppConfig?, bool isSubWheel = false)` chooses art renderer only when follow-wheel is enabled. `ArtStyleRenderer` implements existing renderer contract; normal and highlighted text brushes are explicit. Secondary wheels respect follow-primary configuration and existing independent override mode.

- [ ] Add failing tests for profile palette use, preserving legacy mode with follow disabled, subwheel inheritance/independence, frozen brushes/effects and zero-decoration mode.
- [ ] Add lightweight vector decorations for each style, profile stroke/shadow/transparency, and correct hover text contrast. Route real wheel and both preview construction paths through the new factory overload; preserve custom per-action text/font overrides.

```csharp
var art = config != null && config.ArtStyleFollowsWheel ? ArtStyleResolver.Resolve(config) : null;
return art != null ? new ArtStyleRenderer(art) : CreateLegacyRenderer(style);
```

- [ ] Run console suite, verify representative rendered samples offscreen and commit.

### Task 4: Theme studio UI and draft editing

**Files:** Create `WinPieGestures/ArtStyles/ThemeStudio.xaml(.cs)` and `ArtStyleText.cs`; modify `WinPieGestures/SettingsWindow.xaml` and `SettingsWindow.ArtStyles.cs`.

**Interfaces:** Studio owns an isolated draft, not live config. `Refresh()` reloads cards and active marker. `DraftChanged` supplies preview-only data; `ThemeChanged` is the weak service event. Localized strings are resolved on use and redraw on language change.

- [ ] Add failing integration tests that instantiate the UserControl without showing a window, edit a draft and cancel it, save/reload a custom profile, and check each language's labels. These tests may use reflection for existing private event handlers, without test-only production APIs.
- [ ] Build selectable cards with miniature wheel samples and swatches; add apply/duplicate/new/rename/delete/reset and file import/export buttons. Build palette fields, sliders and font/base-mode choices, a live control sample and wheel sample, plus contrast and invalid-field feedback. Invalid drafts disable save; cancel keeps config unchanged.
- [ ] Connect draft updates to a temporary preview config through the settings partial, never persist draft values. Applied changes persist and redraw both previews. Keep the legacy wheel editing controls available when follow-wheel is off; explain their relation when on.
- [ ] Render studio offscreen under all five profiles, inspect output images, run tests and commit.

### Task 5: Delivery, review and packages

**Files:** Add `docs/art-style-themes.md`; update `CHANGELOG.md`, version synchronization files named in AGENTS.md §5.3; produce ignored `releases/` packages.

- [ ] Document user workflow, legacy compatibility and the portable JSON envelope with a complete exported example. Add five ready-to-import theme files under `themes/`.
- [ ] Run Release build, art-style suite, project static guards and `--plugin-selftest --skip-invoke` in an isolated LOCALAPPDATA sandbox. Expected: zero build errors and no regression failures.
- [ ] Dispatch one fresh-context whole-feature reviewer as required by executing-plans/requesting-code-review. Review focus: legacy preservation, dynamic-resource shadowing in child windows, culture-safe editing, draft/config separation, lifecycle events, import rejection before mutation, subwheel overrides and all real/preview rendering paths.
- [ ] Fix material findings using failing regression tests first, run the complete relevant checks, and commit final changes.
- [ ] Publish win-x64 lightweight and self-contained single-file folders, verify absence of official plugin binaries, zip both, and open the output folder/file for the user. Preserve the managed worktree for review.
