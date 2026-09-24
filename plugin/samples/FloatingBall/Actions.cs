using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.FloatingBall;

/// <summary>
/// 动作一：显示悬浮球（并按参数设定它的外观）。
/// <para>
/// 直径 / 不透明度 / 颜色同时存在于<b>两处</b>：这里是「这个扇区的球要长什么样」，
/// 插件级设置页（见 <see cref="BallSettingsFields"/>）是「没有特别指定时长什么样」。
/// 两处的键名相同、优先级固定为<b>动作参数 &gt; 插件级设置 &gt; 内置默认</b>，
/// 于是「配两个扇区、一个常规球一个小而淡的球」和「开机恢复那颗球」都能各得其所。
/// </para>
/// </summary>
internal sealed class ShowBallContribution : IActionContribution
{
    private readonly IPluginContext _context;
    private readonly BallController _ball;
    private readonly string? _iconKey;

    public ShowBallContribution(IPluginContext context, BallController ball, string? iconKey)
    {
        _context = context;
        _ball = ball;
        _iconKey = iconKey;
    }

    public ActionDescriptor Descriptor => new()
    {
        Id = "showBall",
        DisplayName = "显示悬浮球",
        DisplayNameKey = "action.show-ball.name",
        Description = "在屏幕上放一颗常驻悬浮球，点它呼出你的轮盘。拖动可挪位置，右键收起。",
        Category = "悬浮球",
        IconKey = _iconKey,
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 3,
    };

    public IReadOnlyList<ParameterField> Parameters => new List<ParameterField>
    {
        new()
        {
            Key = BallPreference.DiameterKey,
            Label = "直径",
            LabelKey = "field.diameter.label",
            Type = ParameterFieldType.Number,
            DefaultValue = Defaults.DiameterDiu.ToString("0.##", CultureInfo.InvariantCulture),
            Min = Defaults.DiameterMin,
            Max = Defaults.DiameterMax,
            HelpText = "留空则用插件设置页里的默认直径。",
        },
        new()
        {
            Key = BallPreference.OpacityKey,
            Label = "不透明度",
            LabelKey = "field.opacity.label",
            Type = ParameterFieldType.Number,
            DefaultValue = Defaults.OpacityPercent.ToString("0.##", CultureInfo.InvariantCulture),
            Min = Defaults.OpacityMin,
            Max = Defaults.OpacityMax,
            HelpText = "留空则用插件设置页里的默认不透明度。",
        },
        new()
        {
            Key = BallPreference.ColorKey,
            Label = "颜色",
            LabelKey = "field.color.label",
            Type = ParameterFieldType.Color,
            DefaultValue = Defaults.Color,
            HelpText = "留空则用插件设置页里的默认颜色。",
        },
    };

    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        // 只校验「扇区上确实填了」的项：留空是合法的，含义是「用插件级设置页的默认值」，
        // 把它算成越界会让刚装好、一个参数都没动的用户点哪都失败。
        // 判据是「键在不在、空不空」而不是「值是不是大于 0」：后者会把显式填的 0 当成没填。
        if (!TryReadDouble(parameters, BallPreference.DiameterKey, out double? diameter))
        {
            return _context.I18n.T("error.diameter", "直径得是个数（像素）。");
        }
        if (OutOfRange(diameter, Defaults.DiameterMin, Defaults.DiameterMax))
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                _context.I18n.T("error.diameter-range", "直径要在 {0} 到 {1} 之间。"),
                Defaults.DiameterMin, Defaults.DiameterMax);
        }

        if (!TryReadDouble(parameters, BallPreference.OpacityKey, out double? opacity))
        {
            return _context.I18n.T("error.opacity", "不透明度得是个数（百分比）。");
        }
        if (OutOfRange(opacity, Defaults.OpacityMin, Defaults.OpacityMax))
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                _context.I18n.T("error.opacity-range", "不透明度要在 {0} 到 {1} 之间。"),
                Defaults.OpacityMin, Defaults.OpacityMax);
        }

        return null;
    }

    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        // 契约要求极快：这里只做字符串拼接，不碰窗口。
        // 回落到插件级默认值读的是内存字典（PluginSettings 在实例化时就整份载入并缓存），
        // 不是磁盘 —— 否则预览会显示内置默认，而实际放出来的是用户配的另一套尺寸。
        // 钳制口径与 ExecuteAsync 的 BallPreference.FromAction 保持一致，预览就不会撒谎。
        TryReadDouble(parameters, BallPreference.DiameterKey, out double? diameter);
        TryReadDouble(parameters, BallPreference.OpacityKey, out double? opacity);

        double shownDiameter = diameter is double d
            ? BallPreference.Clamp(d, Defaults.DiameterMin, Defaults.DiameterMax)
            : BallPreference.Diameter(_context);
        double shownOpacity = opacity is double o
            ? BallPreference.Clamp(o, Defaults.OpacityMin, Defaults.OpacityMax)
            : BallPreference.Opacity(_context);

        return string.Format(
            CultureInfo.InvariantCulture,
            _context.I18n.T("preview.show-ball", "直径 {0} · 不透明 {1}%"),
            shownDiameter.ToString("0.##", CultureInfo.InvariantCulture),
            shownOpacity.ToString("0.##", CultureInfo.InvariantCulture));
    }

    public async Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ActionResult.Fail(_context.I18n.T("error.cancelled", "动作已被取消。"));
        }

        if (!_context.Info.HasCapability(PluginCapability.Ui))
        {
            // 装机时用户可以在确认页上不勾「界面」，这属于「环境不具备」而不是插件出错：
            // 报 Fail 会让连点五次之后一个正常插件被判隔离（宿主纪律，见 AGENTS.md 的熔断一节）。
            return ActionResult.Ok(
                _context.I18n.T("notify.no-ui-capability", "清单里没声明「界面」能力，本插件画不出球窗。"),
                silent: false);
        }

        // 优先级：扇区上填了这个参数就用它，留空 → 插件级设置页 → 内置默认。
        // 三个值都在这里收敛，BallController 只接最终数字，不参与来源判断。
        double diameter = BallPreference.FromAction(
            input, BallPreference.DiameterKey,
            BallPreference.Diameter(_context), Defaults.DiameterMin, Defaults.DiameterMax);
        double opacity = BallPreference.FromAction(
            input, BallPreference.OpacityKey,
            BallPreference.Opacity(_context), Defaults.OpacityMin, Defaults.OpacityMax);

        string? actionColor = input.Parameter(BallPreference.ColorKey);
        string color = string.IsNullOrWhiteSpace(actionColor)
            ? BallPreference.Color(_context)
            : actionColor!.Trim();

        try
        {
            // 球是 WPF 窗口，只能在 UI 线程上建；动作线程到这里必须切一次。
            await _context.Dispatcher.InvokeAsync(() => _ball.Show(diameter, opacity, color)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _context.Log.Error("显示悬浮球失败", ex);
            return ActionResult.Fail(string.Format(
                CultureInfo.InvariantCulture,
                _context.I18n.T("error.show-failed", "悬浮球没能显示出来：{0}"),
                ex.Message));
        }

        return ActionResult.Ok(_context.I18n.T("info.shown", "悬浮球已显示，点它即可呼出轮盘。"), silent: true);
    }

    /// <summary>
    /// 读一个可选的数值参数。三种结局必须分得开：
    /// <b>没填</b>（键不存在，或填了空白）→ <paramref name="value"/> 为 <c>null</c>，合法，含义是「用插件级默认值」；
    /// <b>填了但不是数</b> → 返回 false；<b>填了且是数</b> → 返回 true 并带出值。
    /// <para>
    /// 不要退回成「拿 0 当未填」的哨兵写法：那样显式填 0 会溜过范围校验，而 0 恰恰是最该被拦住的那个输入。
    /// 真机踩过反过来的一种：缺键被读成 0，于是刚装好的插件在用户一个参数都没填的情况下
    /// 永远报「直径要在 24 到 160 之间」。
    /// </para>
    /// </summary>
    private static bool TryReadDouble(IReadOnlyDictionary<string, string> parameters, string key, out double? value)
    {
        value = null;
        if (!parameters.TryGetValue(key, out string? raw) || string.IsNullOrWhiteSpace(raw)) return true;

        // 与 PluginActionInput.Double 同一条口径：不变文化。宿主写盘用的就是它。
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) return false;
        value = parsed;
        return true;
    }

    /// <summary>只在「真的填了值」时才算越界；<c>null</c>（未填）永远合法，由执行侧回落默认值。</summary>
    private static bool OutOfRange(double? value, double min, double max) =>
        value is double v && (v < min || v > max);
}

/// <summary>
/// 动作二：隐藏悬浮球。
/// <para>
/// 它与「右键收球」是同一条路（都走 <see cref="BallController.Hide"/>），差别只在要不要落盘
/// <c>visible=false</c> —— 落了这个标记，下次开机就不会再自动出现。
/// 没有这个动作的话，用户只能右键收球、然后再也找不回开机自启的开关。
/// </para>
/// </summary>
internal sealed class HideBallContribution : IActionContribution
{
    private readonly IPluginContext _context;
    private readonly BallController _ball;

    public HideBallContribution(IPluginContext context, BallController ball)
    {
        _context = context;
        _ball = ball;
    }

    public ActionDescriptor Descriptor => new()
    {
        Id = "hideBall",
        DisplayName = "隐藏悬浮球",
        DisplayNameKey = "action.hide-ball.name",
        Description = "收掉当前显示的悬浮球，并记住这个选择（下次启动不再自动出现）。",
        Category = "悬浮球",
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 3,
    };

    public IReadOnlyList<ParameterField> Parameters => Array.Empty<ParameterField>();

    public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;

    public string Preview(IReadOnlyDictionary<string, string> parameters) =>
        _context.I18n.T("preview.hide-ball", "收掉悬浮球");

    public async Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ActionResult.Fail(_context.I18n.T("error.cancelled", "动作已被取消。"));
        }

        try
        {
            await _context.Dispatcher.InvokeAsync(_ball.Hide).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _context.Log.Error("隐藏悬浮球失败", ex);
            return ActionResult.Fail(string.Format(
                CultureInfo.InvariantCulture,
                _context.I18n.T("error.hide-failed", "悬浮球没能收起来：{0}"),
                ex.Message));
        }

        return ActionResult.Ok(Preview(input.Parameters), silent: true);
    }
}
