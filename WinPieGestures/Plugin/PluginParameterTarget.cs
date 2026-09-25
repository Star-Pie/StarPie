using System;
using System.Collections.Generic;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// 参数表单的<b>写入目标</b>。
/// <para>
/// 抽这层接口只有一个动机：<see cref="PluginParameterForm"/> 的渲染与校验逻辑对存储位置一无所知才是对的 ——
/// 同一份 <see cref="ParameterField"/> 声明，挂在扇区动作上时值写进 <c>ActionItem.ExtensionData</c>
/// （跟着配置走），挂在插件上时值写进该插件的 <c>settings.json</c>（跟着插件走）。
/// 如果为了支持后者再写一套表单，两套渲染就会开始漂移：一边修了深色模式对比度、另一边没修，
/// 用户在两个界面上看到同一个字段长得不一样、校验行为也不一样。
/// </para>
/// </summary>
internal interface IPluginParameterTarget
{
    /// <summary>
    /// 当前已保存的全部取值。用于回填，以及 <see cref="ReportsUndeclaredValues"/> 为真时提示
    /// 「配置里有本版本未声明的键」。可能为 <c>null</c>（还没有写过任何值）。
    /// </summary>
    IReadOnlyDictionary<string, string>? Stored { get; }

    /// <summary>
    /// 是否把「未声明的键」显示给用户。
    /// <para>
    /// 动作参数表是<b>封闭</b>的：那个字典里的键只可能是参数，多出来的一定是残留，值得提醒。
    /// 插件的 <c>settings.json</c> 是<b>开放</b>的：插件私有键（缓存、计数器、上次运行时间）
    /// 与设置页字段共享同一命名空间是刻意的设计（见 SDK 的 <c>ISettingsPageRegistry</c>），
    /// 把它们当成「未声明参数」列出来只会吓到用户。
    /// </para>
    /// </summary>
    bool ReportsUndeclaredValues { get; }

    /// <summary>
    /// 写入一个值；空字符串按「未填写」处理，即删除该键。
    /// </summary>
    /// <returns>值是否真的发生了变化。<c>false</c> 时调用方不该触发自动保存。</returns>
    bool Write(string key, string? value);
}

/// <summary>
/// 写进 <c>ActionItem.ExtensionData</c> —— 扇区编辑器里的动作参数用这个。
/// </summary>
internal sealed class ActionItemParameterTarget : IPluginParameterTarget
{
    private readonly ActionItem _item;

    public ActionItemParameterTarget(ActionItem item) => _item = item;

    public IReadOnlyDictionary<string, string>? Stored => _item.ExtensionData;

    public bool ReportsUndeclaredValues => true;

    public bool Write(string key, string? value)
    {
        if (string.IsNullOrEmpty(key)) return false;

        if (string.IsNullOrEmpty(value))
        {
            if (_item.ExtensionData == null) return false;
            if (!_item.ExtensionData.Remove(key)) return false;

            // 空字典整个置 null：不留一个空 KV 集合在主配置里，
            // 否则旧版本读到的会是一个「有 ExtensionData 但什么都没有」的动作。
            if (_item.ExtensionData.Count == 0) _item.ExtensionData = null;
            return true;
        }

        _item.ExtensionData ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_item.ExtensionData.TryGetValue(key, out string? existing) &&
            string.Equals(existing, value, StringComparison.Ordinal))
        {
            return false;
        }

        _item.ExtensionData[key] = value;
        return true;
    }
}

/// <summary>
/// 写进插件的 <c>settings.json</c> —— 插件级参数页用这个。
/// <para>
/// 落盘时机由调用方决定（设置窗口关闭时 <c>Save()</c> 一次），这里只做内存写穿：
/// 宿主与插件共用同一个 <see cref="PluginSettings"/> 实例，所以用户一改动，
/// 插件下一次 <c>Settings.Get</c> 就读到新值；保存成功后，宿主还会通过
/// <see cref="IPluginSettings.OnChanged"/> 通知订阅了对应键的插件。
/// 不在每次按键上落盘：文本框每敲一个字符触发一次变化，逐字符重写整份 JSON 是纯粹的浪费。
/// </para>
/// </summary>
internal sealed class PluginSettingsParameterTarget : IPluginParameterTarget
{
    private readonly PluginSettings _settings;

    public PluginSettingsParameterTarget(PluginSettings settings) => _settings = settings;

    public IReadOnlyDictionary<string, string>? Stored => _settings.Snapshot();

    public bool ReportsUndeclaredValues => false;

    /// <summary>本次会话里是否发生过真实改动；关闭时据此决定要不要写盘。</summary>
    public bool Dirty { get; private set; }

    public bool Write(string key, string? value)
    {
        if (string.IsNullOrEmpty(key)) return false;

        string? existing = _settings.Get(key);

        if (string.IsNullOrEmpty(value))
        {
            if (existing == null) return false;
            _settings.Set(key, null);
            Dirty = true;
            return true;
        }

        if (string.Equals(existing, value, StringComparison.Ordinal)) return false;

        _settings.Set(key, value);
        Dirty = true;
        return true;
    }

    /// <summary>把内存里的改动落盘。</summary>
    public void Save()
    {
        if (!Dirty) return;
        Dirty = false;
        _settings.Save();
    }
}
