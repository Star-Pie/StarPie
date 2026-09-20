using System;
using System.Collections.Generic;
using System.Windows;
using WinPieGestures.Plugins;

namespace WinPieGestures;

/// <summary>
/// 安装确认页。插件以 StarPie 的权限在进程内跑代码，这一页是唯一的知情同意关口 ——
/// 它必须把「它是谁 / 从哪来 / 会拿到什么能力 / 装到哪 / 装完会怎样」摊开，
/// 而不是只弹一句「确定吗」。
/// <para>
/// <b>为什么自绘而不是 <c>MessageBox</c></b>：WPF 的 <c>MessageBox</c> 完全不可定制，
/// 于是十来行等权文字堆在一个滚动区里，用户找不到「它会拿到什么能力」那一段 ——
/// 而那段正是这一页存在的理由。自绘之后分区成卡片，能力行逐条加 ✔。
/// </para>
/// <para>
/// <b>正文不在本文件里</b>：内容是 <see cref="PluginInstallConfirmationText.BuildSections"/>
/// 算出来的，本窗口只负责把数据摆成控件。这样「切到英文后这一页还残不残中文」
/// 才能在无界面自检（<c>[3e]</c>）里被逐语言断言，而不是靠人肉点一遍。
/// </para>
/// <para>
/// 窗口是模态的，关闭前界面语言不可能被切换（切换入口在主窗口上，而主窗口此刻被禁用），
/// 所以这里的文案在构造时取一次即可 —— 这也正是它与插件管理页那些「必须重渲染」的面板的区别。
/// </para>
/// </summary>
public partial class PluginInstallDialog : Window
{
	private readonly PluginInstallConfirmation _confirmation;

	/// <summary>
	/// 构造函数刻意是 <c>internal</c>：入参 <see cref="PluginInstallConfirmation"/> 是 internal 类型，
	/// 与 public 构造函数放在一起会触发 CS0051。本窗口只由主程序集内部弹出，不需要对外开放。
	/// </summary>
	internal PluginInstallDialog(PluginInstallConfirmation confirmation)
	{
		InitializeComponent();

		// 与其它对话框一致：主题在构造时应用一次。不做这一步，深色主题下弹出来的是一个白窗口。
		AppThemeManager.ApplyTheme(this, AppThemeManager.CurrentEffectiveTheme);

		// 高度上限取「设计上限」与「当前屏幕可用高度」的较小者。
		// 屏幕才是真正的约束：确认页一旦比屏幕高，「安装」按钮就会落到屏幕外面去，
		// 而用户看到的只是「这个插件装不上」—— 他压根不知道按钮在下面。
		// XAML 里的 MaxHeight="760" 是设计意图，这里按屏幕再夹一次（WorkArea 已不含任务栏，
		// 再减 60 给窗口边框与中心定位留余量）。夹住之后被压缩的是正文那一 * 行，它会自己出滚动条。
		MaxHeight = Math.Min(760, Math.Max(360, SystemParameters.WorkArea.Height - 60));

		_confirmation = confirmation;

		Title = I18n.T("PluginsConfirmTitle");
		HeadingText.Text = I18n.T("PluginsConfirmTitle");
		CopyButton.Content = I18n.T("PluginsConfirmCopyButton");
		CopyButton.ToolTip = I18n.T("PluginsConfirmCopyToolTip");
		CancelButton.Content = I18n.T("BtnCancel");
		InstallButton.Content = I18n.T("PluginsConfirmInstallButton");

		SectionItemsControl.ItemsSource = BuildBodyViewModels(confirmation);
	}

	/// <summary>
	/// 分区 → 视图模型，并把<b>风险提示那一段摘出来单独交给固定的那一块</b>。
	/// <para>
	/// 摘出来的理由见 XAML 里 <c>SecuritySectionHost</c> 的注释：它排在滚动区末尾时，
	/// 只要内容稍多就会被滚出可视区，而它是用户在按下「安装」之前必须看到的最后一句。
	/// </para>
	/// <para>
	/// 认<b>标题键</b>而不是「最后一段」：位置约定由自检 <c>[3e]</c> 守着，
	/// 界面这边独立再判一次，<see cref="PluginInstallConfirmationText.BuildSections"/>
	/// 哪天调了顺序，这里会退化成「没有固定的风险块」，而不是静默把别的一段搬上去。
	/// 真退化了也还有兜底：全部段落进滚动区，内容一条不少。
	/// </para>
	/// </summary>
	private IReadOnlyList<ConfirmSectionViewModel> BuildBodyViewModels(PluginInstallConfirmation confirmation)
	{
		List<ConfirmSectionViewModel> sections = new(BuildSectionViewModels(confirmation));

		ConfirmSectionViewModel? security = null;
		if (sections.Count > 0
			&& sections[^1].TitleKey == PluginInstallConfirmationText.SecuritySectionKey)
		{
			security = sections[^1];
			sections.RemoveAt(sections.Count - 1);
		}

		SecuritySectionHost.Content = security;
		SecuritySectionHost.Visibility = security == null ? Visibility.Collapsed : Visibility.Visible;

		return sections;
	}

	/// <summary>用户是否按下了「安装」。只有它为 true，调用方才会真的落盘。</summary>
	public bool Confirmed { get; private set; }

	private void InstallButton_Click(object sender, RoutedEventArgs e)
	{
		Confirmed = true;
		DialogResult = true;
	}

	private void CancelButton_Click(object sender, RoutedEventArgs e)
	{
		Confirmed = false;
		DialogResult = false;
	}

	/// <summary>
	/// 把整页的**纯文本形态**复制到剪贴板。
	/// <para>
	/// 这个按钮同时也是 <see cref="PluginInstallConfirmationText.Build"/> 在界面上的唯一使用点：
	/// 没有它，<c>Build</c> 就只剩自检在调用 —— 而「只有断言在用的代码」正是那种
	/// 改坏了也不会有人发现的东西（自检 <c>[3e]</c> 会继续绿，因为它测的就是它自己）。
	/// </para>
	/// </summary>
	private void CopyButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			// 必须写全限定名：本工程同时开了 UseWindowsForms，隐式 using 会让
			// Clipboard 在 System.Windows.Forms 与 System.Windows 之间歧义（CS0104）。
			System.Windows.Clipboard.SetText(PluginInstallConfirmationText.Build(_confirmation));
			CopyButton.Content = I18n.T("PluginsConfirmCopied");
			// 复制完按钮已经变成「已复制」并禁用，气泡再留着「把这一页复制到剪贴板」
			// 就是在回答一个没人问的问题。
			CopyButton.ToolTip = null;
			CopyButton.IsEnabled = false;
		}
		catch (Exception)
		{
			// 剪贴板被别的进程独占时会抛异常。这不是安装流程的一部分，不打断用户。
		}
	}

	private static IReadOnlyList<ConfirmSectionViewModel> BuildSectionViewModels(PluginInstallConfirmation confirmation)
	{
		var sections = new List<ConfirmSectionViewModel>();
		foreach (PluginConfirmSection section in PluginInstallConfirmationText.BuildSections(confirmation))
		{
			var lines = new List<ConfirmLineViewModel>();
			foreach (PluginConfirmLine line in section.Lines)
			{
				lines.Add(new ConfirmLineViewModel
				{
					Text = line.Text,
					IsCapability = line.Kind == PluginConfirmLineKind.Capability,
					IsNotice = line.Kind == PluginConfirmLineKind.Notice,
				});
			}

			sections.Add(new ConfirmSectionViewModel
			{
				TitleKey = section.TitleKey,
				Title = section.Title,
				Lines = lines,
			});
		}

		return sections;
	}

	/// <summary>一个分区的视图模型。<see cref="HasTitle"/> 供 DataTrigger 决定要不要收起标题行。</summary>
	private sealed class ConfirmSectionViewModel
	{
		/// <summary>标题的<b>词条键</b>（不是译文）。界面靠它认出「哪一段是风险提示」——
		/// 拿译文去比就会把判断绑到某一种语言上，切到英文后静默失配。</summary>
		public string TitleKey { get; init; } = "";

		public string Title { get; init; } = "";

		public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

		public IReadOnlyList<ConfirmLineViewModel> Lines { get; init; } = Array.Empty<ConfirmLineViewModel>();
	}

	/// <summary>
	/// 一行的视图模型。
	/// <para>
	/// 两个 bool 而不是一个「行首标记字符串 + 颜色」：颜色不该从数据层带出来
	/// （深浅主题下同一个标记的底色不同），而两个互斥的 bool 正好对上 XAML 里
	/// 那两个 DataTrigger —— 标记的样式全部留在 XAML 里。
	/// </para>
	/// </summary>
	private sealed class ConfirmLineViewModel
	{
		public string Text { get; init; } = "";

		public bool IsCapability { get; init; }

		public bool IsNotice { get; init; }
	}
}
