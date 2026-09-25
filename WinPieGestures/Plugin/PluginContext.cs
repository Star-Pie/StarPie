using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// <see cref="IPluginContext"/> 的宿主实现 —— 插件能看到的「整个世界」。
/// <para>
/// 它刻意只暴露接口，不暴露任何宿主具体类型。这是「插件禁止引用 StarPie.dll」的落地方式：
/// 插件作者只能针对 SDK 编程，宿主内部怎么重构都不影响他们。
/// </para>
/// </summary>
internal sealed class PluginContext : IPluginContext
{
    public PluginContext(
        PluginMetadata metadata,
        string pluginDirectory,
        string dataDirectory,
        PluginRegistrationSession session,
        PluginLogger logger,
        PluginSettings settings,
        PluginEventService events)
    {
        Me = metadata;
        PluginDirectory = pluginDirectory;
        DataDirectory = dataDirectory;
        Log = logger;
        Settings = settings;
        Events = events;

        Actions = new PluginActionRegistry(session, metadata.Id);
        I18n = new PluginI18nRegistry(session, metadata.Id);
        Icons = new PluginIconRegistry(session, metadata.Id);
        SettingsPage = new PluginSettingsPageRegistry(session, metadata.Id, settings);
        Host = new PluginHostActionInvoker(metadata.Id);

        // 这四个服务带能力门禁：构造时就把清单里的 Capabilities 交给它们，
        // 未声明对应能力的插件拿到的是一个「调用即拒绝」的对象。
        // 判定放在服务内部而不是这里 —— 因为「未声明」与「已声明」两种情况下
        // 服务的元数据（终端清单 / 动词清单 / 布局清单 / 预设清单）都应当照常可读，
        // 只有产生后果的调用该被拦。
        Commands = new PluginCommandService(metadata.Id, metadata.Capabilities);
        Shell = new PluginShellService(metadata.Id, metadata.Capabilities);
        Windows = new PluginWindowService(metadata.Id, metadata.Capabilities);
        ScreenCapture = new PluginScreenCaptureService(metadata.Id, metadata.Capabilities);
        System = new PluginSystemService(metadata.Id, metadata.Capabilities);
        Wheel = new PluginWheelService(metadata.Id, metadata.Capabilities);
        KeyboardRemap = new PluginKeyboardRemapService(metadata.Id, metadata.Capabilities);

        Info = new PluginHostInfo(metadata.Capabilities);
        Notify = new PluginNotificationService(metadata.Id);
        Dispatcher = new PluginDispatcherFacade();
    }

    public PluginMetadata Me { get; }

    public string PluginDirectory { get; }

    public string DataDirectory { get; }

    public IPluginLogger Log { get; }

    public IPluginSettings Settings { get; }

    public IActionRegistry Actions { get; }

    public II18nRegistry I18n { get; }

    public IIconRegistry Icons { get; }

    /// <summary>插件级参数页声明（SDK 1.6 起）。</summary>
    public ISettingsPageRegistry SettingsPage { get; }

    public IHostActionInvoker Host { get; }

    public IHostCommandService Commands { get; }

    public IHostShellService Shell { get; }

    public IHostWindowService Windows { get; }

    /// <summary>屏幕截取（框选截屏 + 文字识别）。</summary>
    public IHostScreenCaptureService ScreenCapture { get; }

    /// <summary>系统功能（最小化 / 任务视图 / 音量 / 锁屏 / 关机 …）。</summary>
    public IHostSystemService System { get; }

    /// <summary>轮盘呼出（粘滞会话，供悬浮球这类常驻形态使用）。</summary>
    public IHostWheelService Wheel { get; }

    /// <summary>键盘重映射服务（SDK 1.7 起）。</summary>
    public IHostKeyboardRemapService KeyboardRemap { get; }

    public IHostInfo Info { get; }

    public INotificationService Notify { get; }

    public IPluginEvents Events { get; }

    public IDispatcherFacade Dispatcher { get; }
}

/// <summary>
/// 动作贡献点注册表实现。
/// <para>
/// 契约要求每个 <c>Register</c> 都返回可释放 token：这是把 ALC 卸载从「靠插件自觉」
/// 变成「宿主可控」的关键 —— 实测证明只要宿主仍持有插件实例，卸载<b>必然失败</b>；
/// 而 token 让宿主永远有能力把引用摘干净。
/// </para>
/// </summary>
internal sealed class PluginActionRegistry : IActionRegistry
{
    private static readonly Regex IdPattern = new(
        "^[A-Za-z][A-Za-z0-9_]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly PluginRegistrationSession _session;
    private readonly string _pluginId;
    private readonly PluginCatalog _catalog;
    private readonly List<PluginActionRegistration> _owned = new();

    public PluginActionRegistry(PluginRegistrationSession session, string pluginId)
    {
        _session = session;
        _pluginId = pluginId;
        _catalog = PluginHost.Catalog;
    }

    /// <summary>本插件注册过的全部动作（供宿主展示与兜底撤销）。</summary>
    public IReadOnlyList<PluginActionRegistration> Owned => _owned;

    public IDisposable Register(IActionContribution contribution)
    {
        if (contribution == null)
        {
            throw new PluginContractException("Register(contribution) 传入了 null。");
        }

        ActionDescriptor? descriptor;
        try
        {
            descriptor = contribution.Descriptor;
        }
        catch (Exception ex)
        {
            throw new PluginContractException("读取 Descriptor 时插件抛出了异常。", ex);
        }

        if (descriptor == null)
        {
            throw new PluginContractException("Descriptor 返回了 null。");
        }

        string shortId = (descriptor.Id ?? "").Trim();
        if (shortId.Length == 0)
        {
            throw new PluginContractException("ActionDescriptor.Id 不能为空。");
        }
        if (!IdPattern.IsMatch(shortId))
        {
            throw new PluginContractException(
                $"ActionDescriptor.Id=\"{shortId}\" 非法：必须以字母开头，只允许字母、数字、下划线，最长 64 字符。");
        }

        string fullId = $"{_pluginId}.{shortId}";
        foreach (PluginActionRegistration staged in _session.StagedActions)
        {
            if (string.Equals(staged.FullId, fullId, StringComparison.OrdinalIgnoreCase))
            {
                throw new PluginContractException($"动作 ID 重复注册：{fullId}。");
            }
        }

        if (descriptor.TimeoutSeconds < 0 || descriptor.TimeoutSeconds > 300)
        {
            throw new PluginContractException(
                $"ActionDescriptor.TimeoutSeconds={descriptor.TimeoutSeconds} 超出范围（0 表示默认，可显式指定 1~300）。");
        }

        IReadOnlyList<ParameterField> parameters;
        try
        {
            parameters = contribution.Parameters ?? Array.Empty<ParameterField>();
        }
        catch (Exception ex)
        {
            throw new PluginContractException("读取 Parameters 时插件抛出了异常。", ex);
        }

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ParameterField field in parameters)
        {
            if (field == null)
            {
                throw new PluginContractException($"{fullId} 的 Parameters 里含有 null 项。");
            }
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                throw new PluginContractException($"{fullId} 的参数缺少 Key。");
            }
            if (!seenKeys.Add(field.Key))
            {
                throw new PluginContractException($"{fullId} 的参数 Key 重复：{field.Key}。");
            }
            if (field.Type == ParameterFieldType.Enum && (field.Options == null || field.Options.Count == 0))
            {
                throw new PluginContractException($"{fullId} 的参数 {field.Key} 是 Enum 类型，但没有提供 Options。");
            }
        }

        string displayName = ResolveDisplayName(_pluginId, descriptor, shortId, out bool displayNameFromI18n);

        var registration = new PluginActionRegistration
        {
            PluginId = _pluginId,
            ShortId = shortId,
            FullId = fullId,
            Contribution = contribution,
            DisplayName = displayName,
            DisplayNameKey = string.IsNullOrWhiteSpace(descriptor.DisplayNameKey) ? null : descriptor.DisplayNameKey!.Trim(),
            DisplayNameFromI18n = displayNameFromI18n,
            Description = descriptor.Description ?? "",
            Category = string.IsNullOrWhiteSpace(descriptor.Category) ? "插件" : descriptor.Category!.Trim(),
            IconKey = descriptor.IconKey,
            Kind = descriptor.Kind,
            TimeoutSeconds = descriptor.TimeoutSeconds,
            Parameters = parameters,
        };

        _session.StageAction(registration);
        _owned.Add(registration);

        return new RegistrationToken(() =>
        {
            _owned.Remove(registration);
            _catalog.RemoveAction(registration.FullId);
        });
    }

    /// <param name="fromI18n">是否命中插件词条。调用方据此区分「词条生效」与「退回字面文案」。</param>
    private static string ResolveDisplayName(
        string pluginId,
        ActionDescriptor descriptor,
        string fallback,
        out bool fromI18n)
    {
        // 优先用插件自己的词条；没有词条就用它给的显示名；都没有才退回短 ID。
        // 键的换算交给 PluginI18n —— 这里过去只补「plugin.」前缀，与登记侧
        // 「plugin.<pluginId>.」的归一化结果不相等，导致本地化名永远查不到。
        string? translated = PluginI18n.Resolve(pluginId, descriptor.DisplayNameKey);
        if (translated != null)
        {
            fromI18n = true;
            return translated;
        }

        fromI18n = false;
        return string.IsNullOrWhiteSpace(descriptor.DisplayName) ? fallback : descriptor.DisplayName!.Trim();
    }
}

/// <summary>多语言词条注册表实现。插件只写短键，前缀归一化在这里统一完成。</summary>
internal sealed class PluginI18nRegistry : II18nRegistry
{
    private static readonly Regex KeyPattern = new(
        "^[A-Za-z0-9_.-]{1,96}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly PluginRegistrationSession _session;
    private readonly string _pluginId;

    public PluginI18nRegistry(PluginRegistrationSession session, string pluginId)
    {
        _session = session;
        _pluginId = pluginId;
    }

    private string Normalize(string key) =>
        $"{PluginApi.I18nKeyPrefix}{_pluginId}.{key.Trim()}";

    public void Register(string key, string zhCn, string? en = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new PluginContractException("I18n.Register 的 key 不能为空。");
        }
        if (!KeyPattern.IsMatch(key.Trim()))
        {
            throw new PluginContractException(
                $"I18n.Register 的 key=\"{key}\" 非法：只允许字母、数字、下划线、点、连字符，最长 96 字符。");
        }
        if (string.IsNullOrEmpty(zhCn))
        {
            throw new PluginContractException($"I18n.Register(\"{key}\") 缺少简体中文文案（它是兜底语言，必填）。");
        }

        var values = new Dictionary<LanguageCode, string>
        {
            [LanguageCode.ZhCn] = zhCn,
        };

        if (!string.IsNullOrEmpty(en))
        {
            values[LanguageCode.En] = en!;
        }
        else
        {
            // 没给英文就让英文环境回退中文，而不是把 key 显示给用户
            values[LanguageCode.En] = zhCn;
        }

        StageOrMerge(Normalize(key), values);
    }

    public void RegisterTable(string languageCode, IReadOnlyDictionary<string, string> table)
    {
        if (table == null || table.Count == 0) return;

        LanguageCode language = ParseLanguage(languageCode);
        foreach (KeyValuePair<string, string> pair in table)
        {
            if (string.IsNullOrWhiteSpace(pair.Key)) continue;
            if (!KeyPattern.IsMatch(pair.Key.Trim()))
            {
                AppLogger.LogWarn($"[plugin:{_pluginId}] 跳过非法词条 key：{pair.Key}");
                continue;
            }

            StageOrMerge(Normalize(pair.Key), new Dictionary<LanguageCode, string> { [language] = pair.Value ?? "" });
        }
    }

    public string T(string key, string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return fallback ?? "";

        string fullKey = key.Trim().StartsWith(PluginApi.I18nKeyPrefix, StringComparison.Ordinal)
            ? key.Trim()
            : Normalize(key);

        string translated = I18n.GetString(fullKey);
        if (!string.Equals(translated, fullKey, StringComparison.Ordinal))
        {
            return translated;
        }

        return fallback ?? key.Trim();
    }

    private void StageOrMerge(string fullKey, Dictionary<LanguageCode, string> values)
    {
        // 同一 key 的多语言分批注册时合并，而不是后写覆盖前写
        foreach (PluginCatalog.PluginI18nRegistration staged in _session.StagedI18n)
        {
            if (string.Equals(staged.FullKey, fullKey, StringComparison.Ordinal))
            {
                foreach (KeyValuePair<LanguageCode, string> pair in values)
                {
                    staged.Values[pair.Key] = pair.Value;
                }
                return;
            }
        }

        _session.StageI18n(new PluginCatalog.PluginI18nRegistration
        {
            PluginId = _pluginId,
            FullKey = fullKey,
            Values = values,
        });
    }

    private static LanguageCode ParseLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return LanguageCode.ZhCn;

        return code!.Trim().ToLowerInvariant() switch
        {
            "zh-tw" or "zh-hk" or "zh-hant" => LanguageCode.ZhTw,
            "en" or "en-us" or "en-gb" => LanguageCode.En,
            "ja" or "ja-jp" => LanguageCode.Ja,
            _ => LanguageCode.ZhCn,
        };
    }
}

/// <summary>矢量图标注册表实现。</summary>
internal sealed class PluginIconRegistry : IIconRegistry
{
    private static readonly Regex KeyPattern = new(
        "^[A-Za-z0-9_-]{1,64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>单枚图标的 SVG path 数据长度上限。SVG 是紧凑格式，64KB 已经极其夸张。</summary>
    private const int MaxSvgLength = 64 * 1024;

    private readonly PluginRegistrationSession _session;
    private readonly string _pluginId;
    private readonly Dictionary<string, string> _shortToFull = new(StringComparer.OrdinalIgnoreCase);

    public PluginIconRegistry(PluginRegistrationSession session, string pluginId)
    {
        _session = session;
        _pluginId = pluginId;
    }

    public string RegisterSvg(string key, string svgPathData)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new PluginContractException("Icons.RegisterSvg 的 key 不能为空。");
        }
        if (!KeyPattern.IsMatch(key.Trim()))
        {
            throw new PluginContractException(
                $"Icons.RegisterSvg 的 key=\"{key}\" 非法：只允许字母、数字、下划线、连字符，最长 64 字符。");
        }
        if (string.IsNullOrWhiteSpace(svgPathData))
        {
            throw new PluginContractException($"Icons.RegisterSvg(\"{key}\") 的 svgPathData 为空。");
        }
        if (svgPathData.Length > MaxSvgLength)
        {
            throw new PluginContractException(
                $"Icons.RegisterSvg(\"{key}\") 的 path 数据长度 {svgPathData.Length} 超过 {MaxSvgLength} 上限。");
        }

        string shortKey = key.Trim();
        string fullKey = $"{PluginApi.IconKeyPrefix}{_pluginId}:{shortKey}";

        _session.StageIcon(new PluginIconRegistration
        {
            PluginId = _pluginId,
            FullKey = fullKey,
            SvgPathData = svgPathData.Trim(),
        });

        _shortToFull[shortKey] = fullKey;
        return fullKey;
    }

    public string? ResolveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        string raw = key.Trim();
        if (raw.StartsWith(PluginApi.IconKeyPrefix, StringComparison.Ordinal))
        {
            return _shortToFull.TryGetValue(raw, out string? direct) ? direct : raw;
        }

        return _shortToFull.TryGetValue(raw, out string? full) ? full : null;
    }
}

/// <summary>
/// 插件级参数页注册表实现（SDK 1.6）。
/// <para>
/// 校验口径与 <see cref="PluginActionRegistry"/> 对动作参数的要求<b>完全一致</b>：
/// 同一份 <see cref="ParameterField"/> 声明不该因为挂在动作上还是挂在插件上而受到不同的约束，
/// 差异只会让插件作者写出「一处能过、另一处抛契约异常」的代码。
/// </para>
/// </summary>
internal sealed class PluginSettingsPageRegistry : ISettingsPageRegistry
{
    private readonly PluginRegistrationSession _session;
    private readonly string _pluginId;
    private readonly PluginSettings _settings;
    private readonly PluginCatalog _catalog;

    /// <summary>本插件已暂存的页。「一个插件一页」的判定依据。</summary>
    private PluginSettingsPageRegistration? _staged;

    public PluginSettingsPageRegistry(PluginRegistrationSession session, string pluginId, PluginSettings settings)
    {
        _session = session;
        _pluginId = pluginId;
        _settings = settings;
        _catalog = PluginHost.Catalog;
    }

    public IDisposable Register(SettingsPageDescriptor page)
    {
        if (page == null)
        {
            throw new PluginContractException("SettingsPage.Register(page) 传入了 null。");
        }
        if (_staged != null)
        {
            throw new PluginContractException("本插件已声明过设置页：一个插件只允许一页，请把字段合并进同一张表。");
        }

        IReadOnlyList<ParameterField> fields;
        try
        {
            fields = page.Fields ?? Array.Empty<ParameterField>();
        }
        catch (Exception ex)
        {
            throw new PluginContractException("读取 Fields 时插件抛出了异常。", ex);
        }

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ParameterField field in fields)
        {
            if (field == null)
            {
                throw new PluginContractException($"{_pluginId} 的设置页 Fields 里含有 null 项。");
            }
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                throw new PluginContractException($"{_pluginId} 的设置页字段缺少 Key。");
            }
            if (!seenKeys.Add(field.Key))
            {
                throw new PluginContractException($"{_pluginId} 的设置页字段 Key 重复：{field.Key}。");
            }
            if (field.Type == ParameterFieldType.Enum && (field.Options == null || field.Options.Count == 0))
            {
                throw new PluginContractException($"{_pluginId} 的字段 {field.Key} 是 Enum 类型，但没有提供 Options。");
            }
        }

        var registration = new PluginSettingsPageRegistration
        {
            PluginId = _pluginId,
            Title = page.Title ?? "",
            TitleKey = TrimToNull(page.TitleKey),
            Description = page.Description ?? "",
            DescriptionKey = TrimToNull(page.DescriptionKey),
            // 复制成数组再留档：插件给的列表可能实现自插件自己程序集的类型，
            // 长期表留着它，插件停用后收集上下文就回收不掉了。
            Fields = ToArray(fields),
        };

        _session.StageSettingsPage(registration);
        _staged = registration;

        return new RegistrationToken(() =>
        {
            _staged = null;
            _catalog.RemoveSettingsPage(_pluginId);
        });
    }

    /// <summary>
    /// 读当前值：已保存的取值优先，从未填写过才回落到字段声明的默认值。
    /// <para>
    /// 这层回落是 <see cref="GetValue"/> 存在的全部理由：没有它，插件得在「字段声明里的
    /// DefaultValue」和「自己代码里的兜底常量」写两遍同一个数字，而这两处迟早会漂。
    /// 用户清空某个字段时宿主会删除该键（见 <c>PluginSettings.Set</c> 的 null 语义），
    /// 于是「清空」与「从未填过」在配置里是同一个状态，读回来也都是默认值 —— 这正是用户期望的。
    /// </para>
    /// </summary>
    public string? GetValue(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        string trimmed = key.Trim();
        string? saved = _settings.Get(trimmed);
        if (saved != null) return saved;

        PluginSettingsPageRegistration? page = _staged;
        if (page == null) return null;

        foreach (ParameterField field in page.Fields)
        {
            if (field != null && string.Equals(field.Key, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return field.DefaultValue;
            }
        }
        return null;
    }

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private static ParameterField[] ToArray(IReadOnlyList<ParameterField> fields)
    {
        var array = new ParameterField[fields.Count];
        for (int i = 0; i < array.Length; i++) array[i] = fields[i];
        return array;
    }
}

/// <summary>注册凭据。释放即摘除贡献点；宿主停用时也会兜底释放一次。</summary>
internal sealed class RegistrationToken : IDisposable
{
    private Action? _revoke;
    private bool _disposed;

    public RegistrationToken(Action revoke) => _revoke = revoke;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Action? action = _revoke;
        _revoke = null;
        try
        {
            action?.Invoke();
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] 撤销注册时发生异常（已忽略）：{ex.Message}");
        }
    }
}
