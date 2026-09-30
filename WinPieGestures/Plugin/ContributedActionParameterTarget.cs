using System;
using System.Collections.Generic;

namespace WinPieGestures.Plugins;

/// <summary>官方认领动作的附加参数：读取投影后的旧字段，写入仍只落 ExtensionData。</summary>
internal sealed class ContributedActionParameterTarget : IPluginParameterTarget
{
    private readonly ActionItem _item;
    private readonly Func<ActionItem?>? _writeSource;
    public ContributedActionParameterTarget(ActionItem item, Func<ActionItem?>? writeSource = null)
    { _item = item; _writeSource = writeSource; }
    public IReadOnlyDictionary<string, string>? Stored => ActionParameterProjection.Project(_item);
    public bool ReportsUndeclaredValues => false; // 投影还含手写面板的旧字段，不是残留。
    public bool Write(string key, string? value)
    {
        ActionItem? target = _writeSource == null ? _item : _writeSource();
        return target != null && new ActionItemParameterTarget(target).Write(key, value);
    }
}
