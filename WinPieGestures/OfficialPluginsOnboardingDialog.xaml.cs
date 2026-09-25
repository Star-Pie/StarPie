using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinPieGestures.Plugins;

namespace WinPieGestures;

/// <summary>
/// 官方核心插件首次引导安装对话框。
/// 严格遵守知情同意原则：进入弹窗前及点击「一键安装」前不发网络请求。
/// </summary>
public partial class OfficialPluginsOnboardingDialog : Window
{
    private bool _isInstalling;

    public OfficialPluginsOnboardingDialog()
    {
        InitializeComponent();
        ApplyLocalization();
        PopulatePluginItems();

        I18n.LanguageChanged += ApplyLocalization;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        I18n.LanguageChanged -= ApplyLocalization;
        // 无论用户是点击完成、暂不安装、还是点击右上角关闭，均标记为已提示，升级或下次打开不再弹窗
        OfficialPluginOnboarding.MarkPrompted();
        base.OnClosing(e);
    }

    private void ApplyLocalization()
    {
        Title = I18n.T("OfficialPluginsOnboardingTitle") + " - StarPie";
        if (TitleTextBlock != null) TitleTextBlock.Text = I18n.T("OfficialPluginsOnboardingTitle");
        if (IntroTextBlock != null) IntroTextBlock.Text = I18n.T("OfficialPluginsOnboardingIntro");
        if (SecurityHeaderTextBlock != null) SecurityHeaderTextBlock.Text = "🛡️ " + I18n.T("OfficialPluginsOnboardingSecurityHeader");
        if (SecuritySourceTextBlock != null) SecuritySourceTextBlock.Text = "• " + I18n.T("OfficialPluginsOnboardingSecuritySource");
        if (SecurityPermsTextBlock != null) SecurityPermsTextBlock.Text = "• " + I18n.T("OfficialPluginsOnboardingSecurityPerms");
        if (SecurityDescTextBlock != null) SecurityDescTextBlock.Text = "• " + I18n.T("OfficialPluginsOnboardingSecurityDesc");
        if (LaterButton != null) LaterButton.Content = I18n.T("OfficialPluginsOnboardingLaterBtn");
        if (InstallButton != null) InstallButton.Content = I18n.T("OfficialPluginsOnboardingInstallBtn");
        if (RetryButton != null) RetryButton.Content = I18n.T("OfficialPluginsOnboardingRetryBtn");
        if (DoneButton != null) DoneButton.Content = I18n.T("OfficialPluginsOnboardingDoneBtn");
        if (HintTextBlock != null) HintTextBlock.Text = I18n.T("OfficialPluginsOnboardingHint");

        PopulatePluginItems();
    }

    private void PopulatePluginItems()
    {
        if (PluginItemsPanel == null) return;
        PluginItemsPanel.Children.Clear();

        foreach (OfficialPluginTargetInfo target in OfficialPluginOnboarding.TargetPlugins)
        {
            bool isInstalled = OfficialPluginOnboarding.IsPluginInstalled(target.Id);

            var rowGrid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoStack = new StackPanel();

            var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
            titleStack.Children.Add(new TextBlock
            {
                Text = target.Icon,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = target.Name,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = $"({target.Id})",
                FontSize = 10.5,
                Foreground = (Brush)FindResource("TextTertiaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });
            infoStack.Children.Add(titleStack);

            infoStack.Children.Add(new TextBlock
            {
                Text = target.Description,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(18, 1, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            Grid.SetColumn(infoStack, 0);
            rowGrid.Children.Add(infoStack);

            // 状态徽标
            var badgeBorder = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = isInstalled
                    ? new SolidColorBrush(Color.FromArgb(32, 16, 185, 129))
                    : (Brush)FindResource("SubtleCardBrush")
            };

            badgeBorder.Child = new TextBlock
            {
                Text = isInstalled ? I18n.T("OfficialPluginsStateInstalled") : I18n.T("OfficialPluginsStateToInstall"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = isInstalled
                    ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                    : (Brush)FindResource("TextSecondaryBrush")
            };

            Grid.SetColumn(badgeBorder, 1);
            rowGrid.Children.Add(badgeBorder);

            PluginItemsPanel.Children.Add(rowGrid);
        }
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBatchInstallAsync();
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBatchInstallAsync();
    }

    private async Task RunBatchInstallAsync()
    {
        if (_isInstalling) return;
        _isInstalling = true;

        InstallButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        RetryButton.IsEnabled = false;

        ProgressArea.Visibility = Visibility.Visible;
        InstallProgressBar.Value = 0;
        StatusTextBlock.Text = I18n.T("OfficialPluginsOnboardingStatusReady");

        OfficialPluginOnboarding.MarkPrompted();

        var progressReporter = new Progress<OfficialPluginBatchProgress>(p =>
        {
            if (p.TotalCount > 0)
            {
                InstallProgressBar.Value = (double)p.CompletedCount / p.TotalCount * 100.0;
            }
            StatusTextBlock.Text = p.Message;
        });

        try
        {
            OfficialPluginBatchInstallReport report = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                extraCapabilityPrompter: async (pluginName, extraCaps) =>
                {
                    return await Dispatcher.InvokeAsync(() =>
                    {
                        string title = I18n.T("OfficialPluginsOnboardingExtraCapPromptTitle");
                        string msg = I18n.TF("OfficialPluginsOnboardingExtraCapPrompt", pluginName, string.Join(", ", extraCaps));
                        return MessageBox.Show(this, msg, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                    });
                },
                progress: progressReporter
            );

            PopulatePluginItems();

            if (report.AllSucceeded)
            {
                InstallProgressBar.Value = 100;
                StatusTextBlock.Text = I18n.T("OfficialPluginsOnboardingAllSucceeded");
                InstallButton.Visibility = Visibility.Collapsed;
                LaterButton.Visibility = Visibility.Collapsed;
                RetryButton.Visibility = Visibility.Collapsed;
                DoneButton.Visibility = Visibility.Visible;
            }
            else
            {
                int totalHandled = report.SuccessCount + report.FailureCount;
                InstallProgressBar.Value = totalHandled > 0 ? (double)report.SuccessCount / totalHandled * 100.0 : 0;
                string errDetails = string.Join("; ", report.FailedPlugins.Select(kv => $"{kv.Key}: {kv.Value}"));
                StatusTextBlock.Text = I18n.TF("OfficialPluginsOnboardingPartialFailed", report.SuccessCount, report.FailureCount, errDetails);

                InstallButton.Visibility = Visibility.Collapsed;
                LaterButton.Visibility = Visibility.Collapsed;
                RetryButton.Visibility = Visibility.Visible;
                RetryButton.IsEnabled = true;
                DoneButton.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = ex.Message;
            InstallButton.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Visible;
            RetryButton.IsEnabled = true;
            DoneButton.Visibility = Visibility.Visible;
        }
        finally
        {
            _isInstalling = false;
        }
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        OfficialPluginOnboarding.MarkPrompted();
        Close();
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        OfficialPluginOnboarding.MarkPrompted();
        Close();
    }
}
