using System;
using System.Collections.Generic;
using System.Windows;
using WinPieGestures.Plugins;

namespace WinPieGestures;

/// <summary>
/// 插件级参数页窗口（SDK 1.6）。
/// <para>
/// 它是 <see cref="PluginSettingsPageService"/> 的一层皮：解析声明 → 交给
/// <see cref="PluginParameterForm"/> 渲染 → 关闭时落盘。字段控件与扇区编辑器里的动作参数
/// <b>共用同一套渲染</b>，所以取色器、文件浏览、必填星号、Min/Max 提示这些细节不需要在这里重写第二遍。
/// </para>
/// <para>
/// <b>为什么没有「保存」和「取消」</b>：值是写穿的 —— 用户一动控件，宿主就把值写进了插件正在使用的
/// 那份配置实例，插件下一次 <c>Settings.Get</c> 读到的就是新值。既然改已经生效了，
/// 放一个「取消」按钮就是撒谎（它撤不回任何东西），放一个「保存」按钮则暗示「没点就不算数」。
/// 只留「关闭」，落盘发生在关闭那一刻。
/// </para>
/// </summary>
public partial class PluginSettingsPageWindow : Window
{
    private readonly PluginSettingsPageService.Page? _page;
    private PluginParameterForm? _form;

    public PluginSettingsPageWindow(string pluginId) : this(pluginId, null) { }

    public PluginSettingsPageWindow(string pluginId, string? sectionId)
    {
        InitializeComponent();
        AppThemeManager.ApplyTheme(this, AppThemeManager.CurrentEffectiveTheme);

        _page = PluginSettingsPageService.Open(pluginId, sectionId);

        // 卡片上的按钮可见性已经按 HasPage 判过，正常路径不会走到 null。
        // 留着这条守卫是因为「刷新列表」与「点击」之间隔着一段时间，
        // 而那个插件可能已经在别的代码路径里被停用；此刻弹一张空表比不弹更让人困惑。
        if (_page == null)
        {
            Loaded += (_, _) => Close();
            return;
        }

        base.Title = string.IsNullOrWhiteSpace(_page.Title) ? _page.PluginId : _page.Title;
        TitleText.Text = base.Title;
        if (!string.IsNullOrWhiteSpace(_page.Description))
        {
            DescriptionText.Text = _page.Description;
            DescriptionText.Visibility = Visibility.Visible;
        }
        CloseButton.Content = I18n.T("BtnClose");

        _form = new PluginParameterForm(FormPanel, () => PluginSettingsPageService.IsCurrent(_page) ? _page.Target : null, RefreshValidation, includeSettingsShortcut: false);
        _form.Build(_page.Fields, _page.PluginId, _page.SectionId);
        RefreshValidation();
    }

    /// <summary>
    /// 就地刷新校验结论。
    /// <para>
    /// 校验只显示、<b>不拦关闭</b>，与扇区编辑器里的动作参数同一口径：
    /// 那里也是「写穿 + 标红提示」，超范围的值由插件在自己的执行代码里兜底钳制。
    /// 一旦这里改成「不合法就不许关窗」，用户就会被困在一张自己填不出合法值的表里。
    /// </para>
    /// </summary>
    private void RefreshValidation()
    {
        if (_page == null || ValidationText == null) return;

        try
        {
            List<PluginParameterIssue> issues = PluginSettingsPageService.Validate(_page);
            _form?.ShowIssues(issues);

            if (issues.Count == 0)
            {
                ValidationText.Text = "";
                ValidationText.Visibility = Visibility.Collapsed;
                return;
            }

            ValidationText.Text = "⛔ " + PluginActionPanelText.IssuesCount(issues.Count);
            ValidationText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 刷新插件设置页校验结论时异常", ex);
            ValidationText.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_page == null || _form == null) return;
        FormPanel.IsEnabled = PluginSettingsPageService.IsCurrent(_page);
        if (!FormPanel.IsEnabled) return;
        _form.Build(_page.Fields, _page.PluginId, _page.SectionId);
        RefreshValidation();
    }

    /// <summary>关闭即落盘。Esc、右上角 × 与本按钮都会走到这里，只此一处就不会漏。</summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        try
        {
            if (_page != null) PluginSettingsPageService.Persist(_page);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 保存插件设置页时异常", ex);
        }
    }
}
