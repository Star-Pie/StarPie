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
    private Task? _activeInstallTask;
    private bool _isCancellingAndClosing;
    private readonly HashSet<string> _preExistingDisabledIds;
    private CancellationTokenSource? _installCts;

    public OfficialPluginsOnboardingDialog()
    {
        InitializeComponent();
        AppThemeManager.ApplyTheme(this, ConfigManager.CurrentConfig?.AppTheme ?? "System");
        _preExistingDisabledIds = new HashSet<string>(OfficialPluginOnboarding.GetOriginallyDisabledPluginIds(), StringComparer.OrdinalIgnoreCase);

        ApplyLocalization();
        PopulatePluginItems();

        I18n.LanguageChanged += ApplyLocalization;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isInstalling)
        {
            e.Cancel = true;
            if (!_isCancellingAndClosing)
            {
                _isCancellingAndClosing = true;
                _ = CancelAndCloseAfterOperationAsync();
            }
            return;
        }

        I18n.LanguageChanged -= ApplyLocalization;
        // 无论用户是点击完成、暂不安装、还是点击右上角关闭，均标记为已提示，升级或下次打开不再弹窗
        OfficialPluginOnboarding.MarkPrompted();
        base.OnClosing(e);
    }

    private async Task CancelAndCloseAfterOperationAsync()
    {
        try
        {
            if (StatusTextBlock != null)
            {
                StatusTextBlock.Text = I18n.T("OfficialPluginsOnboardingStatusCancelling");
            }

            if (InstallButton != null) InstallButton.IsEnabled = false;
            if (LaterButton != null) LaterButton.IsEnabled = false;
            if (RetryButton != null) RetryButton.IsEnabled = false;
            if (DoneButton != null) DoneButton.IsEnabled = false;

            try
            {
                _installCts?.Cancel();
            }
            catch (Exception ex)
            {
                AppLogger.LogWarn($"[plugin] 发起取消官方插件安装失败：{ex.Message}");
            }

            if (_activeInstallTask != null)
            {
                try
                {
                    await _activeInstallTask;
                }
                catch (OperationCanceledException)
                {
                    // 预期的取消异常
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarn($"[plugin] 等待安装任务取消完成时异常：{ex.Message}");
                }
            }
        }
        finally
        {
            _isInstalling = false;
            Dispatcher.Invoke(Close);
        }
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
            bool isEnabled = false;
            if (isInstalled)
            {
                PluginInstance? inst = PluginHost.Find(target.Id);
                if (inst != null)
                {
                    isEnabled = inst.Entry.Enabled;
                }
                else
                {
                    PluginRegistryEntry? entry = PluginRegistryStore.FindEntry(target.Id);
                    isEnabled = entry?.Enabled ?? false;
                }
            }

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

            // 状态徽标：区分待安装、已启用、已停用
            var badgeBorder = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = !isInstalled
                    ? (Brush)FindResource("SubtleCardBrush")
                    : (isEnabled
                        ? new SolidColorBrush(Color.FromArgb(32, 16, 185, 129))
                        : new SolidColorBrush(Color.FromArgb(32, 245, 158, 11)))
            };

            string badgeText;
            Brush badgeFg;
            if (!isInstalled)
            {
                badgeText = I18n.T("OfficialPluginsStateToInstall");
                badgeFg = (Brush)FindResource("TextSecondaryBrush");
            }
            else if (isEnabled)
            {
                badgeText = I18n.T("OfficialPluginsStateInstalled");
                badgeFg = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            }
            else
            {
                badgeText = I18n.T("OfficialPluginsStateInstalledDisabled");
                badgeFg = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            }

            badgeBorder.Child = new TextBlock
            {
                Text = badgeText,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = badgeFg
            };

            Grid.SetColumn(badgeBorder, 1);
            rowGrid.Children.Add(badgeBorder);

            PluginItemsPanel.Children.Add(rowGrid);
        }
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        _activeInstallTask = RunBatchInstallAsync();
        await _activeInstallTask;
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _activeInstallTask = RunBatchInstallAsync();
        await _activeInstallTask;
    }

    private async Task RunBatchInstallAsync()
    {
        if (_isInstalling) return;
        _isInstalling = true;

        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _installCts.Token;

        InstallButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        RetryButton.IsEnabled = false;

        ProgressArea.Visibility = Visibility.Visible;
        InstallProgressBar.Value = 0;
        StatusTextBlock.Text = I18n.T("OfficialPluginsOnboardingStatusReady");

        OfficialPluginOnboarding.MarkPrompted();

        var progressReporter = new Progress<OfficialPluginBatchProgress>(p =>
        {
            if (!IsLoaded || _isCancellingAndClosing) return;
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
                        if (!IsLoaded || _isCancellingAndClosing) return false;
                        string title = I18n.T("OfficialPluginsOnboardingExtraCapPromptTitle");
                        string msg = I18n.TF("OfficialPluginsOnboardingExtraCapPrompt", pluginName, string.Join(", ", extraCaps));
                        return MessageBox.Show(this, msg, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                    });
                },
                progress: progressReporter,
                cancellationToken: cancellationToken,
                preExistingDisabledPluginIds: _preExistingDisabledIds
            );

            if (!IsLoaded || _isCancellingAndClosing) return;
            PopulatePluginItems();

            if (report.AllSuccessfullyActive)
            {
                InstallProgressBar.Value = 100;
                StatusTextBlock.Text = I18n.T("OfficialPluginsOnboardingAllSucceeded");
                InstallButton.Visibility = Visibility.Collapsed;
                LaterButton.Visibility = Visibility.Collapsed;
                RetryButton.Visibility = Visibility.Collapsed;
                DoneButton.Visibility = Visibility.Visible;
            }
            else if (report.FailureCount == 0 && report.OriginallyInstalledDisabledPluginIds.Count > 0)
            {
                // 无安装失败，但有原本被用户停用的插件：保持停用，明确说明，绝不谎报全部启用
                InstallProgressBar.Value = 100;
                int activeCount = report.SuccessCount + report.AlreadyInstalledAndEnabledPluginIds.Count;
                StatusTextBlock.Text = I18n.TF("OfficialPluginsOnboardingSummaryWithDisabled", activeCount, report.OriginallyInstalledDisabledPluginIds.Count);
                InstallButton.Visibility = Visibility.Collapsed;
                LaterButton.Visibility = Visibility.Collapsed;
                RetryButton.Visibility = Visibility.Collapsed;
                DoneButton.Visibility = Visibility.Visible;
            }
            else
            {
                // 存在失败项或启用失败项：给出可操作的重试与启用指引，绝不谎报全部启用
                int totalHandled = report.SuccessCount + report.FailureCount;
                InstallProgressBar.Value = totalHandled > 0 ? (double)report.SuccessCount / totalHandled * 100.0 : 0;

                var guidanceList = new List<string>();
                foreach (var kv in report.InstalledButEnableFailedPlugins)
                {
                    guidanceList.Add(kv.Value);
                }
                foreach (var kv in report.FailedPlugins)
                {
                    guidanceList.Add(kv.Value);
                }
                string details = string.Join("\n", guidanceList);

                StatusTextBlock.Text = I18n.TF("OfficialPluginsOnboardingPartialFailed", report.SuccessCount, report.FailureCount, details);

                InstallButton.Visibility = Visibility.Collapsed;
                LaterButton.Visibility = Visibility.Collapsed;
                RetryButton.Visibility = Visibility.Visible;
                RetryButton.IsEnabled = true;
                DoneButton.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
            AppLogger.LogInfo("[plugin] 官方插件安装已取消。");
            if (IsLoaded && !_isCancellingAndClosing)
            {
                StatusTextBlock.Text = "安装已取消。";
                InstallButton.Visibility = Visibility.Collapsed;
                RetryButton.Visibility = Visibility.Visible;
                RetryButton.IsEnabled = true;
                DoneButton.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            if (IsLoaded && !_isCancellingAndClosing)
            {
                StatusTextBlock.Text = ex.Message;
                InstallButton.Visibility = Visibility.Collapsed;
                RetryButton.Visibility = Visibility.Visible;
                RetryButton.IsEnabled = true;
                DoneButton.Visibility = Visibility.Visible;
            }
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
