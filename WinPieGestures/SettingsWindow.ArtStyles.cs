using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinPieGestures.ArtStyles;

namespace WinPieGestures;

public partial class SettingsWindow
{
    private AppConfig? _artPreviewConfig;
    private AppConfig ArtPreviewConfig => _artPreviewConfig ?? ConfigManager.CurrentConfig;
    private readonly List<StackPanel> _artPreviewContents = new();

    private void InitializeArtStyleStudio()
    {
        WeakEventManager<ArtStyleService,EventArgs>.AddHandler(ArtStyleService.Instance,nameof(ArtStyleService.Changed),ArtStyles_Changed);
    }
    private void ArtStyles_Changed(object? sender, EventArgs e)
    {
        if (_resourcesReleased) return;
        _artPreviewConfig=null;
        UpdateSidebarThemeVisualState(ConfigManager.CurrentConfig.AppTheme);
        bool dark=IsCurrentThemeDark(); UpdateLogoTheme(dark); App.ApplyTrayTheme(dark);
        if (AppearanceSettingsGrid.Visibility==Visibility.Visible) RenderLiveWheelPreview();
        if (MappingsSettingsGrid.Visibility==Visibility.Visible) RenderMappingsWheelPreview();
    }
    private void ReloadArtStyleStudio()
    {
        _artPreviewConfig=null;
        ThemeStudioControl?.ReloadFromConfig();
    }
    private void ThemeStudio_DraftPreviewChanged(object sender, ArtStyleDraftEventArgs e)
    {
        var c=ConfigManager.CurrentConfig;
        // Only the art-style selection is isolated. Renderers retain the live legacy palette and glow parameters.
        _artPreviewConfig=e.Profile==null?null:new AppConfig
        {
            SelectedArtStyleId=e.Profile.Id, CustomArtStyles=[e.Profile.Copy()],
            ArtStyleFollowsWheel=c.ArtStyleFollowsWheel,
            Theme=c.Theme, UiStyle=c.UiStyle, UseIndependentSubWheelTheme=c.UseIndependentSubWheelTheme,
            SubWheelTheme=c.SubWheelTheme, SubWheelUiStyle=c.SubWheelUiStyle
        };
        if (AppearanceSettingsGrid?.Visibility==Visibility.Visible) RenderLiveWheelPreview();
    }

    private void RefreshArtPreviewText()
    {
        if (_previewStyleRenderer is not ArtStyleRenderer) return;
        for (int i = 0; i < _artPreviewContents.Count; i++)
        {
            foreach (FrameworkElement child in _artPreviewContents[i].Children)
            {
                bool highlighted = i == _lastHoveredSector;
                if (child is TextBlock text) text.Foreground = ArtStyleRenderer.ContentBrush(_previewStyleRenderer,text,highlighted,_previewTextBrush);
                else if (child is System.Windows.Shapes.Path icon) icon.Fill = ArtStyleRenderer.ContentBrush(_previewStyleRenderer,icon,highlighted,_previewTextBrush);
            }
        }
    }
}
