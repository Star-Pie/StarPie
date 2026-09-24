using System;
using System.Collections.Generic;
using System.Globalization;
using StarPie.Plugin;

namespace StarPie.Plugin.FloatingBall;

/// <summary>
/// 悬浮球外观的<b>参数键</b>与<b>取值优先级</b>。
/// <para>
/// 键名写在这里一次，插件级设置页与动作参数表都从这里取 —— 两处用的是同一批键名是刻意的
/// （见 <see cref="ISettingsPageRegistry"/>：设置页的值就落在插件私有配置的同一命名空间里），
/// 但把它们写成两份字符串字面量，改一处漏一处就会让用户「改了设置没反应」，而且查不出来。
/// </para>
/// <para>
/// <b>优先级：动作参数 &gt; 插件级设置页 &gt; 内置默认。</b>
/// 理由是这个插件的两种用法分别对应两端：挂在扇区上的参数回答「这个扇区的球该长什么样」
/// （同一个球可以被两个扇区配成两副样子，这是悬浮球的正当用法），
/// 设置页回答「用户没有特别指定时它长什么样」（开机预加载恢复的那颗球就没有扇区）。
/// </para>
/// </summary>
internal static class BallPreference
{
    public const string DiameterKey = "diameter";
    public const string OpacityKey = "opacity";
    public const string ColorKey = "color";

    /// <summary>插件级直径（已钳进合法区间）。</summary>
    public static double Diameter(IPluginContext context) =>
        Number(context, DiameterKey, Defaults.DiameterDiu, Defaults.DiameterMin, Defaults.DiameterMax);

    /// <summary>插件级不透明度（已钳进合法区间）。</summary>
    public static double Opacity(IPluginContext context) =>
        Number(context, OpacityKey, Defaults.OpacityPercent, Defaults.OpacityMin, Defaults.OpacityMax);

    /// <summary>插件级颜色。非法色值退回默认，而不是把异常抛给宿主。</summary>
    public static string Color(IPluginContext context)
    {
        string? raw = context.SettingsPage.GetValue(ColorKey)?.Trim();
        if (string.IsNullOrEmpty(raw)) return Defaults.Color;

        // 十六进制色只有 #RGB / #RRGGBB / #AARRGGBB 三种合法形状，
        // 这里用形状判断而不是抛异常试探：一个错值不该让球画不出来。
        return IsHexColor(raw!) ? raw! : Defaults.Color;
    }

    /// <summary>
    /// 把「动作参数」叠在「插件级设置」之上：<c>input</c> 里有值且能解析就用它，否则用插件级值。
    /// 两种来源的值一律钳进合法区间后再用 —— 宿主的 <c>Min/Max</c> 只负责把问题<b>显示</b>给用户，
    /// 不会拦住一个越界值落盘（见 <see cref="ParameterField.Min"/>），兜底只能是插件自己的事。
    /// </summary>
    public static double FromAction(PluginActionInput input, string key, double pluginLevel, double min, double max)
    {
        string? raw = input.Parameter(key);
        if (string.IsNullOrWhiteSpace(raw)) return pluginLevel;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return pluginLevel;
        return Clamp(value, min, max);
    }

    public static double Clamp(double value, double min, double max) =>
        value < min ? min : (value > max ? max : value);

    private static double Number(IPluginContext context, string key, double fallback, double min, double max)
    {
        string? raw = context.SettingsPage.GetValue(key);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        // 不变文化解析：设置页写盘的就是不变文化字面量（宿主归一化过），
        // 逗号作小数点的区域设置上按当前文化解析会读不回自己写的值。
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? Clamp(value, min, max)
            : fallback;
    }

    private static bool IsHexColor(string value)
    {
        if (value.Length == 0 || value[0] != '#') return false;

        int digits = value.Length - 1;
        if (digits != 3 && digits != 6 && digits != 8) return false;

        for (int i = 1; i < value.Length; i++)
        {
            char c = value[i];
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex) return false;
        }
        return true;
    }
}

/// <summary>
/// 插件级设置页的字段表。<b>与动作参数表分开写</b>是刻意的：两处说明文字面向的问题不同 ——
/// 设置页要交代「这是默认值、改完只影响下一次显示」，动作参数只需要说清这个扇区的球长什么样。
/// </summary>
internal static class BallSettingsFields
{
    public static IReadOnlyList<ParameterField> Build() => new List<ParameterField>
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
            HelpText = "单位是逻辑像素（跟随系统缩放）。",
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
        },
        new()
        {
            Key = BallPreference.ColorKey,
            Label = "颜色",
            LabelKey = "field.color.label",
            Type = ParameterFieldType.Color,
            DefaultValue = Defaults.Color,
        },
    };
}
