using System;
using System.Collections.Generic;
using System.Linq;

namespace WinPieGestures.Plugins;

/// <summary>「⚡ 分配至轮盘」没能执行下去的原因。</summary>
internal enum PluginAssignFailure
{
	None = 0,

	/// <summary>
	/// 该插件当前没有任何已登记的动作。
	/// <para>
	/// 最常见的原因是插件没启用（贡献点只在插件加载后登记），其次是加载失败。
	/// 所以提示语必须同时提到「先启用插件」，而不是只说「没有动作」——
	/// 后者会让用户去翻插件清单找不存在的 bug。
	/// </para>
	/// </summary>
	NoActions = 1,

	/// <summary>当前配置方案里一个扇区都没有（配置损坏或刚被清空）。</summary>
	NoSlots = 2,
}

/// <summary>
/// 卡片上「⚡ 分配至轮盘」为什么点不了。
/// <para>
/// 分三种而不是合成一句「不可用」：它们对用户要做的下一步<b>完全不同</b> ——
/// 未启用要去打开开关，认领型模块要去类型下拉里选，坏掉的插件要先修。
/// 合成一句「当前不可用」，用户就只能挨个试。
/// </para>
/// </summary>
internal enum PluginAssignBlock
{
	None = 0,

	/// <summary>插件未启用。启用之后这一下就能用。</summary>
	Disabled = 1,

	/// <summary>
	/// 这个模块的动作以<b>顶层动作类型</b>提供（<c>ClaimedTypes</c> 非空，如「平铺窗口」「OCR」）。
	/// <para>
	/// 那类动作在「动作类型」下拉里<b>直接可选</b>，本来就不需要「分配」这一步 ——
	/// 而它们同时又被从「插件动作」子下拉里排除（见 <c>PluginHost.GetRegisteredActions</c>
	/// 会滤掉被认领的 FullId）。所以对它亮着 ⚡ 的话，用户点下去只会得到
	/// 「没有可分配的动作」，而证据其实在点之前就拿得到。
	/// </para>
	/// </summary>
	ClaimsTypes = 2,

	/// <summary>插件当前不可用：被隔离 / 不兼容 / 加载失败 / 正在停止 / 需要重启。</summary>
	Unavailable = 3,
}

/// <summary>
/// 一次「⚡ 分配至轮盘」的决策结果 —— <b>只描述「该做什么」，不碰任何控件与配置</b>。
/// <para>
/// 调用方拿到它之后才去切页签、选扇区、写动作。这样拆的理由和
/// <see cref="PluginInstallConfirmationText"/> 一致：决策留在纯函数里，
/// 无界面自检才能用合成数据逐分支驱动它（<c>[3f]</c>），
/// 而不是只能靠「界面上恰好有几个插件、几个空扇区」碰运气。
/// </para>
/// </summary>
internal sealed class PluginAssignPlan
{
	public PluginAssignFailure Failure { get; init; } = PluginAssignFailure.None;

	public bool Ok => Failure == PluginAssignFailure.None;

	/// <summary>要写入的动作全局 ID（<see cref="PluginActionItem.FullId"/>）。</summary>
	public string FullId { get; init; } = "";

	/// <summary>动作的显示名，用于提示语。</summary>
	public string ActionName { get; init; } = "";

	/// <summary>该插件一共有几个动作。&gt;1 时提示语要说清「只分配了第一个」。</summary>
	public int ActionCount { get; init; }

	/// <summary>目标扇区下标（0 起，不含中心动作）。</summary>
	public int SlotIndex { get; init; } = -1;

	/// <summary>
	/// 目标扇区原有的动作名；<b>空串表示那个扇区本来是空的</b>。
	/// <para>
	/// 「没覆盖」与「覆盖了一个恰好没名字的动作」在提示语里必须是两句话，
	/// 所以这里用空串而不是 null 表达「本来就没配过」—— 后者会让调用方
	/// 分不清「没有值」与「值是空」。
	/// </para>
	/// </summary>
	public string ReplacedName { get; init; } = "";

	/// <summary>本次会覆盖掉一个已有动作。</summary>
	public bool Overwrites => !string.IsNullOrWhiteSpace(ReplacedName);
}

/// <summary>
/// 「⚡ 分配至轮盘」的决策层：把「哪个插件的哪个动作 → 哪个扇区」算出来。
/// <para>
/// <b>为什么要有这一层</b>：这件事有三个容易做错的地方，而它们全都不是编译错误 ——
/// ① 插件可能一个动作都没有（未启用时贡献点根本没登记）；
/// ② 当前配置可能一个扇区都没有；
/// ③ 随手往「当前扇区」一塞，会把用户已经配好的动作静默冲掉。
/// 三条都只在运行时、只在特定配置下才出现，写成纯函数才验得动。
/// </para>
/// <para>
/// <b>为什么优先找空扇区而不是直接覆盖当前扇区</b>：用户在插件页点这一下的时候，
/// 看不到手势页当前选中的是哪个扇区（可能还是上次离开时的那一个）。
/// 直接覆盖等于让用户在看不见的地方丢掉配置，而且还有自动保存，撤销不了。
/// 所以规则是「当前扇区是空的就用它 → 否则用第一个空扇区 → 全满才覆盖当前扇区」，
/// 并且无论走哪条，提示语都会报出目标扇区与是否覆盖。
/// </para>
/// </summary>
internal static class PluginWheelAssignment
{
	/// <summary>
	/// 判断卡片上那颗「⚡ 分配至轮盘」该不该亮，以及不亮时是什么原因。
	/// <para>
	/// <b>纯函数，且只吃登记表信息</b>（启用偏好、认领数、运行态、是否待重启）——
	/// 不吃 <see cref="PluginInstance"/> 对象。理由有二：一是本判据要在<b>插件还没加载</b>时
	/// 就算得出来（恰恰是启动后最常见的处境），二是无界面自检能直接摆出四种组合逐个断言
	/// （<c>[3f]</c>），不必依赖「沙箱里那个插件刚好是哪种状态」。
	/// </para>
	/// <para>
	/// <b>判据刻意<b>不</b>看已登记的动作数</b>：动作只在插件加载后才登记，而宿主默认不预加载
	/// （R1）。若按已登记数来判，则宿主每次启动后所有插件都是「没有动作」——
	/// 按钮全体灰掉，而点一下本来是能成功的（处理器会先把这个插件拉起来）。
	/// 这里改成问「能不能加载」，真正有没有动作交给加载之后那一步回答。
	/// </para>
	/// </summary>
	/// <param name="entryEnabled">登记表里的启用偏好（不是运行态）。</param>
	/// <param name="claimedTypeCount">该插件认领的顶层动作类型数（<c>ClaimedTypes.Count</c>）。</param>
	/// <param name="requiresRestart">旧运行时尚未释放，此刻加载必失败。</param>
	public static PluginAssignBlock BlockReason(
		bool entryEnabled,
		int claimedTypeCount,
		PluginRuntimeState state,
		bool requiresRestart)
	{
		// 认领排在最前：这是插件**固有**的属性，启用或不启用都改不了它 ——
		// 先报「未启用」会引导用户去做一件做了也没用的事。
		if (claimedTypeCount > 0) return PluginAssignBlock.ClaimsTypes;

		if (!entryEnabled) return PluginAssignBlock.Disabled;

		if (requiresRestart) return PluginAssignBlock.Unavailable;

		return state switch
		{
			PluginRuntimeState.Installed => PluginAssignBlock.None,
			PluginRuntimeState.Active => PluginAssignBlock.None,
			PluginRuntimeState.Loading => PluginAssignBlock.None,

			// 这里刻意留 `_` 而不写穷尽 switch：本方法问的是「能不能加载」，
			// 而**将来新增的运行态默认应当是不可加载**（安全方向）。写成穷尽的话，
			// 新增一个状态会让编译报错逼着人回来补一行 —— 补的时候顺手写成 None
			// 就凭空多出一个「按钮亮着但点了必失败」的状态，而那种 bug 只在运行时显形。
			_ => PluginAssignBlock.Unavailable,
		};
	}

	/// <summary>
	/// 取该插件当前可供分配的动作，顺序即插件声明顺序。
	/// <para>
	/// 直接复用 <see cref="PluginActionBinding.BuildPluginActionItems"/> 而不是自己
	/// 去查 <c>PluginHost</c>：那里已经把「插件异常不外泄」「同名插件补 ID」
	/// 这些事处理过了，另写一份必然漂移 —— 而漂移的表现是
	/// 「子下拉里有这个动作，⚡ 却说这个插件没有动作」。
	/// </para>
	/// <para>
	/// <b>调用前必须先 <c>PluginHost.EnsureLoadedForOperation</c></b>，否则已启用但尚未加载的
	/// 插件在这里永远取到空列表（原因见该方法注释）。
	/// </para>
	/// </summary>
	public static IReadOnlyList<PluginActionItem> ActionsOf(string pluginId)
	{
		if (string.IsNullOrWhiteSpace(pluginId)) return Array.Empty<PluginActionItem>();

		return PluginActionBinding.BuildPluginActionItems()
			.Where(item => string.Equals(item.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
			.ToList();
	}

	/// <summary>
	/// 选出要写入的扇区下标。返回 <c>-1</c> 表示没有扇区可用。
	/// <para>
	/// 输入是「扇区名数组」而<b>不是</b>扇区对象：本方法只需要知道「这一格空不空」，
	/// 传对象进来会连带把整个配置模型拖进断言里，合成数据也就无从构造。
	/// </para>
	/// </summary>
	public static int ChooseSlotIndex(IReadOnlyList<string> slotNames, int preferredIndex)
	{
		if (slotNames == null || slotNames.Count == 0) return -1;

		int preferred = Math.Clamp(preferredIndex, 0, slotNames.Count - 1);

		// ① 当前扇区是空的 —— 用户刚刚还在看它，用它最不意外。
		if (string.IsNullOrWhiteSpace(slotNames[preferred])) return preferred;

		// ② 否则找第一个空扇区，避免覆盖。
		for (int i = 0; i < slotNames.Count; i++)
		{
			if (string.IsNullOrWhiteSpace(slotNames[i])) return i;
		}

		// ③ 全满：只能覆盖当前扇区。调用方会在提示语里说明替换掉了哪一个。
		return preferred;
	}

	/// <summary>把「哪个插件 + 当前扇区快照」算成一份可执行的计划。</summary>
	public static PluginAssignPlan Plan(string pluginId, IReadOnlyList<string> slotNames, int preferredIndex)
	{
		IReadOnlyList<PluginActionItem> actions = ActionsOf(pluginId);
		if (actions.Count == 0)
		{
			return new PluginAssignPlan { Failure = PluginAssignFailure.NoActions };
		}

		if (slotNames == null || slotNames.Count == 0)
		{
			return new PluginAssignPlan
			{
				Failure = PluginAssignFailure.NoSlots,
				ActionCount = actions.Count,
			};
		}

		int index = ChooseSlotIndex(slotNames, preferredIndex);

		// 只分配第一个动作：这是一条「一键找个位置、再去右边精调」的快捷方式，
		// 不是动作选择器（要挑具体动作请用子下拉）。插件有多个动作时由提示语告知。
		PluginActionItem first = actions[0];

		return new PluginAssignPlan
		{
			FullId = first.FullId,
			ActionName = first.DisplayName,
			ActionCount = actions.Count,
			SlotIndex = index,
			ReplacedName = (slotNames[index] ?? "").Trim(),
		};
	}
}
