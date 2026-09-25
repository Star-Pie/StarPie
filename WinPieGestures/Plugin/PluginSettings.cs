using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// 插件私有配置（<c>plugin-data\&lt;id&gt;\settings.json</c>）。
/// <para>
/// 与主 <c>config.json</c> 彻底隔离的理由（设计决策 D6）：
/// 主配置被钩子线程高频读取且写盘无锁，把插件数据并进去会直接放大既有的 H1 竞态；
/// 而且插件启停是相对高频操作，不应反复触发 97+ 键主配置的整份序列化。
/// </para>
/// <para>
/// 值一律用字符串：既避免插件自定义类型进入持久化层，也天然强制插件在边界做类型转换与校验。
/// </para>
/// </summary>
internal sealed class PluginSettings : IPluginSettings
{
    private readonly string _pluginId;
    private readonly string _path;
    private readonly object _gate = new();
    private readonly object _saveGate = new();
    private readonly Dictionary<string, List<SettingsSubscription>> _subscriptions =
        new(StringComparer.Ordinal);

    private Dictionary<string, string> _values;
    private Dictionary<string, string> _persistedValues;

    public PluginSettings(string pluginId)
    {
        _pluginId = pluginId;
        _path = PluginPaths.GetSettingsPath(pluginId);
        _values = Load();
        _persistedValues = new Dictionary<string, string>(_values, StringComparer.Ordinal);
    }

    public string? Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        lock (_gate)
        {
            return _values.TryGetValue(key, out string? value) ? value : null;
        }
    }

    public void Set(string key, string? value)
    {
        if (string.IsNullOrEmpty(key)) return;

        // 防止插件用巨型字符串把 settings.json 撑爆（与主配置的 LOH 防护同一口径）
        if (value != null && value.Length > PluginApi.MaxParameterValueLength)
        {
            value = value.Substring(0, PluginApi.MaxParameterValueLength);
        }

        lock (_gate)
        {
            if (value == null) _values.Remove(key);
            else _values[key] = value;
        }
    }

    public bool GetBool(string key, bool defaultValue = false) =>
        bool.TryParse(Get(key), out bool value) ? value : defaultValue;

    /// <summary>
    /// 按<b>不变文化</b>解析整数。
    /// <para>
    /// 宿主写进这里的数字（参数表单与插件级设置页都做了归一化）永远是不变文化字面量；
    /// 不指定文化就会在逗号作小数点的区域设置上出问题，也与
    /// <see cref="StarPie.Plugin.PluginActionInput.Int"/> 的既有口径不一致。
    /// </para>
    /// </summary>
    public int GetInt(string key, int defaultValue = 0) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : defaultValue;

    /// <summary>同 <see cref="GetInt"/>，必须用不变文化。</summary>
    public double GetDouble(string key, double defaultValue = 0) =>
        double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : defaultValue;

    /// <summary>
    /// 当前内存取值的快照。宿主侧渲染设置页时用它回填。
    /// <para>返回的是拷贝，调用方改不动内部字典 —— 写必须走 <see cref="Set"/>，才能保住长度上限与删除语义。</para>
    /// </summary>
    public IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, string>(_values, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// 订阅指定键的已持久化变更。Set 只修改内存，Save 成功后才会通知。
    /// </summary>
    public IDisposable OnChanged(string key, Action<PluginSettingChanged> handler)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("设置键不能为空。", nameof(key));
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new SettingsSubscription(this, key, handler);
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(key, out List<SettingsSubscription>? list))
            {
                list = new List<SettingsSubscription>();
                _subscriptions.Add(key, list);
            }

            list.Add(subscription);
        }

        return subscription;
    }

    /// <summary>
    /// 宿主停用插件时的兜底清理。插件正常会 Dispose 自己的 token，
    /// 这里仍必须清掉宿主持有的所有回调，避免插件 ALC 被配置服务钉住。
    /// </summary>
    internal void ClearSubscriptions()
    {
        SettingsSubscription[] subscriptions;
        lock (_gate)
        {
            var all = new List<SettingsSubscription>();
            foreach (List<SettingsSubscription> list in _subscriptions.Values)
            {
                all.AddRange(list);
            }

            _subscriptions.Clear();
            subscriptions = all.ToArray();
        }

        foreach (SettingsSubscription subscription in subscriptions)
        {
            subscription.DetachFromOwner();
        }
    }

    public void Save()
    {
        lock (_saveGate)
        {
            Dictionary<string, string> snapshot;
            List<PluginSettingChanged> changes;

            lock (_gate)
            {
                snapshot = new Dictionary<string, string>(_values, StringComparer.Ordinal);
                changes = BuildChanges(_persistedValues, snapshot);
            }

            if (changes.Count == 0) return;

            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            string temp = _path + ".tmp";

            try
            {
                string? dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(temp, json);
                if (File.Exists(_path))
                {
                    File.Replace(temp, _path, null);
                }
                else
                {
                    File.Move(temp, _path, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[plugin] 保存插件配置失败：{_path}", ex);
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch
                {
                    // 清理失败不应覆盖原始保存错误。
                }

                return;
            }

            lock (_gate)
            {
                _persistedValues = snapshot;
            }

            NotifySubscribers(changes);
        }
    }

    private static List<PluginSettingChanged> BuildChanges(
        IReadOnlyDictionary<string, string> oldValues,
        IReadOnlyDictionary<string, string> newValues)
    {
        var changes = new List<PluginSettingChanged>();

        foreach ((string key, string oldValue) in oldValues)
        {
            if (!newValues.TryGetValue(key, out string? newValue))
            {
                changes.Add(new PluginSettingChanged(key, oldValue, null));
            }
        }

        foreach ((string key, string newValue) in newValues)
        {
            if (!oldValues.TryGetValue(key, out string? oldValue))
            {
                changes.Add(new PluginSettingChanged(key, null, newValue));
            }
            else if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                changes.Add(new PluginSettingChanged(key, oldValue, newValue));
            }
        }

        return changes;
    }

    private void NotifySubscribers(IReadOnlyList<PluginSettingChanged> changes)
    {
        foreach (PluginSettingChanged change in changes)
        {
            SettingsSubscription[] subscribers;
            lock (_gate)
            {
                if (!_subscriptions.TryGetValue(change.Key, out List<SettingsSubscription>? list) || list.Count == 0)
                {
                    continue;
                }

                // 回调在锁外执行：回调可能 Dispose 自身、触发保存或导致插件停用。
                subscribers = list.ToArray();
            }

            foreach (SettingsSubscription subscription in subscribers)
            {
                subscription.Invoke(change);
            }
        }
    }

    private void RemoveSubscription(SettingsSubscription subscription)
    {
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(subscription.Key, out List<SettingsSubscription>? list)) return;

            list.Remove(subscription);
            if (list.Count == 0) _subscriptions.Remove(subscription.Key);
        }

        subscription.DetachFromOwner();
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new Dictionary<string, string>(StringComparer.Ordinal);
            string json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>(StringComparer.Ordinal);

            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] 插件配置损坏，已忽略并使用默认值：{_path}（{ex.Message}）");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private sealed class SettingsSubscription : IDisposable
    {
        private readonly Action<PluginSettingChanged> _handler;
        private PluginSettings? _owner;
        private int _disposed;

        public SettingsSubscription(
            PluginSettings owner,
            string key,
            Action<PluginSettingChanged> handler)
        {
            _owner = owner;
            Key = key;
            _handler = handler;
        }

        public string Key { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            PluginSettings? owner = Interlocked.Exchange(ref _owner, null);
            owner?.RemoveSubscription(this);
        }

        public void DetachFromOwner()
        {
            Interlocked.Exchange(ref _disposed, 1);
            Interlocked.Exchange(ref _owner, null);
        }

        public void Invoke(PluginSettingChanged change)
        {
            if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _owner) == null) return;

            try
            {
                _handler(change);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[plugin] 设置变更回调失败：{Key}", ex);
            }
        }
    }
}
