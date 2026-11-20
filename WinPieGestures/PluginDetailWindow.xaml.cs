using System;
using System.Windows;
using WinPieGestures.Plugins;

namespace WinPieGestures;

public partial class PluginDetailWindow : Window
{
    private readonly PluginDetailModel _model;

    internal PluginDetailWindow(PluginDetailModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        InitializeComponent();
        AppThemeManager.ApplyTheme(this, ConfigManager.CurrentConfig?.AppTheme ?? "System");
        RenderModel();
    }

    private void RenderModel()
    {
        Title = $"{I18n.T("PluginsDetailTitle")} - {_model.DisplayName}";
        NameText.Text = $"{_model.DisplayName} {_model.VersionText}".Trim();
        StatusText.Text = $"{_model.StateText} · {_model.SourceText}";
        DescriptionHeaderText.Text = I18n.T("PluginsDetailDescription");
        DescriptionText.Text = string.IsNullOrWhiteSpace(_model.Description) ? I18n.T("PluginsOfficialSummaryFallback") : _model.Description;
        FeaturesHeaderText.Text = I18n.T("PluginsDetailFeatures");
        FeaturesItemsControl.ItemsSource = _model.Features;
        NoFeaturesText.Text = I18n.T("PluginsDetailNoFeatures");
        NoFeaturesText.Visibility = _model.HasFeatures ? Visibility.Collapsed : Visibility.Visible;
        CapabilitiesHeaderText.Text = I18n.T("PluginsDetailCapabilities");
        CapabilitiesText.Text = _model.HasCapabilities ? _model.CapabilitiesText : I18n.T("PluginsDetailNoFeatures");
        MetadataHeaderText.Text = I18n.T("PluginsDetailMetadata");
        AuthorLabelText.Text = I18n.T("PluginsDetailAuthor");
        LicenseLabelText.Text = I18n.T("PluginsDetailLicense");
        VersionLabelText.Text = I18n.T("PluginsDetailVersion");
        SourceLabelText.Text = I18n.T("PluginsDetailSource");
        PluginIdLabelText.Text = I18n.T("PluginsDetailPluginId");
        TargetFrameworkLabelText.Text = I18n.T("PluginsDetailTargetFramework");
        DependenciesLabelText.Text = I18n.T("PluginsDetailDependencies");
        InstallPathLabelText.Text = I18n.T("PluginsDetailInstallPath");
        DigestLabelText.Text = I18n.T(_model.HasPackageDigest ? "PluginLibraryPackageHash" : "PluginLibraryFileHash");
        DigestValueText.Text = string.IsNullOrWhiteSpace(_model.Sha256Text) ? "—" : _model.Sha256Text;
        SignatureLabelText.Text = I18n.T("PluginLibrarySignature");
        SignatureValueText.Text = _model.SignatureText;
        SignatureLabelText.Visibility = SignatureValueText.Visibility = string.IsNullOrWhiteSpace(_model.SignatureText)
            ? Visibility.Collapsed : Visibility.Visible;
        AuthorValueText.Text = _model.Author;
        LicenseValueText.Text = _model.License;
        VersionValueText.Text = _model.VersionText;
        SourceValueText.Text = _model.SourceText;
        PluginIdValueText.Text = _model.PluginId;
        TargetFrameworkValueText.Text = _model.TargetFramework;
        DependenciesValueText.Text = _model.HasDependencies ? _model.DependenciesText : "—";
        InstallPathValueText.Text = string.IsNullOrWhiteSpace(_model.InstallPath) ? "—" : _model.InstallPath;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
