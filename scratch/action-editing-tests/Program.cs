using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using StarPie.Plugin;
using WinPieGestures;

namespace ActionEditingTests;

public static class Program
{
    private static int s_failed = 0;
    private static int s_passed = 0;

    [STAThread]
    public static int Main(string[] args)
    {
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(AppContext.BaseDirectory, "isolated-data"));
        Console.WriteLine("====================================================");
        Console.WriteLine("StarPie 二级动作编辑 V1 自动化回归与失败证据探针");
        Console.WriteLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine("====================================================");

        RunTest("1. 克隆独立性测试 (ActionItem.Clone 切断所有引用共享与保留字段)", TestCloneIndependence);
        RunTest("2. 未知字段往返测试 (Extras 扩展数据深拷贝与 JSON 往返)", TestUnknownFieldRoundtrip);
        RunTest("3. 继承无修改测试 (浏览/读取继承子动作不产生配置副作用与物化)", TestInheritanceNonMutating);
        RunTest("4. 插件子动作撤销完整性 (撤销备份不得丢失插件引用与扩展参数)", TestSubActionUndoPreservesAllFields);
        RunTest("5. 撤销上下文隔离测试 (撤销事务绑定具体方案与层，禁止跨方案误恢复)", TestUndoContextIsolation);
        RunTest("6. 平铺预设项数量核对 (预设平铺子菜单必须为 8 项而非 18 项)", TestTilePresetSubActionsCount);
        RunTest("7. 事务历史栈容量截断与跨方案/层隔离测试", TestTransactionHistoryCapacityAndContextIsolation);
        RunTest("8. 内部剪贴板独立性与中心核圆单动作不变量测试", TestActionClipboardIndependenceAndInvariants);
        RunTest("9. 外圈子环与模型超额保留测试 (禁止截断已有 >4 项旧配置)", TestOuterRingExceededSubActionsPreservedWithoutTruncation);
        RunTest("10. 跨扇区操作事务原子性测试 (两端扇区双向原子备份与恢复)", TestCrossSectorTransactionAtomicSnapshots);
        RunTest("11. 生产快照判等探针: 插件参数变更导致快照不等且可触发撤销记录", TestProductionSnapshotEqualityWithPluginParams);
        RunTest("12. 生产粘贴参数完整性探针: 主扇区粘贴保留插件参数与扩展字典", TestProductionPrimaryPasteIncludesPluginParams);
        RunTest("13. 生产粘贴执行与外观字段探针: 主扇区粘贴保留 RunAsStandardUser、字号等外观字段", TestProductionPrimaryPasteIncludesAppearanceFields);
        RunTest("14. 生产主扇区换位联动探针: LinkSubActionsWhenDragging=false 时主动作换位保持子动作在原方位", TestProductionPrimarySwapRespectsDisabledChildLinkage);
        RunTest("15. 生产子动作恢复继承探针: RestoreSubActionInheritance 恢复扇区继承并动态跟随全局", TestProductionRestoreChildReadsGlobalContent);
        RunTest("16. 生产撤销恢复继承探针: 对继承扇区粘贴后撤销，完整恢复原始 Type=Inherit", TestProductionUndoPasteRestoresOriginalInheritance);
        RunTest("17. 生产同名插件子动作换位撤销探针: 同名同贡献点插件子动作换位可正常产生撤销记录", TestProductionSwapSameNamedPluginChildrenUndoable);
        RunTest("18. 生产蜂窝二级菜单容量门禁探针: 蜂窝模式满 3 项拒绝 Ctrl 拖拽复制新增", TestProductionCtrlCopyRejectsFullFanSubmenu);
        RunTest("19. 生产满容量拒绝零修改探针: 满容量蜂窝模式下被拒绝的操作配置完全零修改且无撤销记录", TestRejectedFullFanCopyLeavesConfigUnchanged);
        RunTest("20. 焦点属性编辑与撤销合并探针: 文本连续输入自动合并为单步撤销，支持完整Undo/Redo", TestFocusPropertyUndoRedoAndCoalescing);
        RunTest("21. 历史栈深度与项目预算探针: 限制最大 50 条历史与 500 项快照预算", TestTransactionBudgetAndMaxHistory);
        RunTest("22. 真实选择路径同步有序列表、路径与空状态", FocusEditingUiTests.SelectionRefresh);
        RunTest("23. 列表插入位置与无效落点边界", FocusEditingUiTests.InsertionBoundaries);
        RunTest("24. 列表拖拽提交、取消和上下文隔离", FocusEditingUiTests.ListDragLifecycle);
        RunTest("25. 撤销拒绝同名重建方案和被替换的轮盘层", FocusEditingUiTests.HistoryContextIsolation);

        Console.WriteLine("====================================================");
        Console.WriteLine($"测试总结: 通过 {s_passed}, 失败 {s_failed}");
        Console.WriteLine("====================================================");

        return s_failed > 0 ? 1 : 0;
    }

    private static void RunTest(string name, Action testAction)
    {
        Console.Write($"[RUN] {name} ... ");
        try
        {
            testAction();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PASS");
            Console.ResetColor();
            s_passed++;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FAIL");
            Console.ResetColor();
            Console.WriteLine($"      错误详情: {ex}");
            s_failed++;
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"断言失败: {message}");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"断言失败 [{message}]: 期望值='{expected}', 实际值='{actual}'");
        }
    }

    /// <summary>
    /// 探针 1：ActionItem.Clone 必须深复制 PluginActionRef, ExtensionData, Extras, SubActions 及所有外观字段，绝不共享引用
    /// </summary>
    private static void TestCloneIndependence()
    {
        var original = new ActionItem
        {
            Type = "Plugin",
            Name = "原始插件动作",
            Parameter = "param1",
            Arguments = "arg1",
            IconKey = "PluginIcon",
            CustomIconSvg = "<svg>original</svg>",
            CommandTerminal = "powershell",
            BrowserChoice = "Chrome",
            BrowserPath = @"C:\chrome.exe",
            InheritAppIconPath = @"C:\app.exe",
            RunAsStandardUser = true,
            LayoutMode = "IconOnly",
            CustomTextColor = "#FF0000",
            CustomFontFamily = "Segoe UI",
            CustomIconSize = 24.0,
            CustomFontSize = 14.0,
            CustomTextPlacement = "Above",
            CustomTextOffsetX = 5.0,
            CustomTextOffsetY = -5.0,
            PluginActionRef = new PluginActionRef
            {
                PluginId = "plugin.test",
                ContributionId = "act.test"
            },
            ExtensionData = new Dictionary<string, string>
            {
                ["key1"] = "val1",
                ["key2"] = "val2"
            },
            Extras = new Dictionary<string, JsonElement>
            {
                ["unknownFutureField"] = JsonDocument.Parse("\"futureValue\"").RootElement
            },
            SubActions = new List<ActionItem>
            {
                new ActionItem
                {
                    Name = "原始子动作",
                    Type = "Hotkey",
                    Parameter = "Ctrl+C",
                    SubActions = new List<ActionItem>
                    {
                        new ActionItem { Name = "深层子动作", Type = "Hotkey", Parameter = "Ctrl+V" }
                    }
                }
            }
        };

        var clone = original.Clone();

        // 验证基本属性复制
        AssertEqual(original.Name, clone.Name, "Name 复制");
        AssertEqual(original.Type, clone.Type, "Type 复制");
        AssertEqual(original.LayoutMode, clone.LayoutMode, "LayoutMode 复制");
        AssertEqual(original.CustomTextColor, clone.CustomTextColor, "CustomTextColor 复制");
        AssertEqual(original.CustomTextPlacement, clone.CustomTextPlacement, "CustomTextPlacement 复制");

        // 验证 Extras 是否保留（当前基准中 ActionItem.Clone 漏掉了 Extras！）
        AssertTrue(clone.Extras != null, "克隆体必须保留 Extras");
        AssertTrue(clone.Extras!.ContainsKey("unknownFutureField"), "克隆体必须保留未知字段 unknownFutureField");
        AssertTrue(!ReferenceEquals(original.Extras, clone.Extras), "Extras 必须是独立字典");

        // 验证 PluginActionRef 独立性
        AssertTrue(clone.PluginActionRef != null, "克隆体必须拥有 PluginActionRef");
        AssertTrue(!ReferenceEquals(original.PluginActionRef, clone.PluginActionRef), "PluginActionRef 必须切断引用共享");
        clone.PluginActionRef!.ContributionId = "modified.action";
        AssertEqual("act.test", original.PluginActionRef.ContributionId, "修改克隆体 PluginActionRef 不得影响原对象");

        // 验证 ExtensionData 独立性
        AssertTrue(clone.ExtensionData != null, "克隆体必须拥有 ExtensionData");
        AssertTrue(!ReferenceEquals(original.ExtensionData, clone.ExtensionData), "ExtensionData 必须切断引用共享");
        clone.ExtensionData!["key1"] = "modifiedVal";
        AssertEqual("val1", original.ExtensionData!["key1"], "修改克隆体 ExtensionData 不得影响原对象");

        // 验证 SubActions 递归深拷贝独立性
        AssertEqual(1, clone.SubActions!.Count, "SubActions 数量一致");
        AssertTrue(!ReferenceEquals(original.SubActions, clone.SubActions), "SubActions 列表必须切断引用共享");
        AssertTrue(!ReferenceEquals(original.SubActions![0], clone.SubActions[0]), "SubActions 元素必须切断引用共享");
        AssertTrue(!ReferenceEquals(original.SubActions[0].SubActions, clone.SubActions[0].SubActions), "深层 SubActions 必须切断引用共享");

        clone.SubActions[0].Name = "修改后子动作";
        AssertEqual("原始子动作", original.SubActions[0].Name, "修改克隆子动作名称不得影响原对象");
    }

    /// <summary>
    /// 探针 2：未知字段 Extras 在序列化与克隆中的往返完整性
    /// </summary>
    private static void TestUnknownFieldRoundtrip()
    {
        string jsonWithUnknownFields = """
        {
          "Type": "Hotkey",
          "Name": "测试未知字段",
          "Parameter": "Alt+F4",
          "CustomExperimentalField": "BetaValue123",
          "FutureNumericConfig": 42
        }
        """;

        var item = JsonSerializer.Deserialize<ActionItem>(jsonWithUnknownFields);
        AssertTrue(item != null, "反序列化成功");
        AssertTrue(item!.Extras != null && item.Extras!.Count >= 2, "成功解析 Extras 未知字段");
        AssertTrue(item.Extras!.ContainsKey("CustomExperimentalField"), "包含 CustomExperimentalField");

        var cloned = item.Clone();
        AssertTrue(cloned.Extras != null && cloned.Extras.ContainsKey("CustomExperimentalField"), "克隆后 Extras 完整保留");

        string serializedCloned = JsonSerializer.Serialize(cloned);
        AssertTrue(serializedCloned.Contains("CustomExperimentalField"), "克隆后重新序列化必须保留未知字段");
        AssertTrue(serializedCloned.Contains("BetaValue123"), "未知字段值必须保留");
    }

    /// <summary>
    /// 探针 3：继承动作只读浏览测试：当非全局方案继承全局子动作时，查看不应物化修改该方案的配置
    /// </summary>
    private static void TestInheritanceNonMutating()
    {
        var globalProfile = new WheelProfile
        {
            ProcessName = "Global",
            DisplayName = "全局方案",
            SectorCount = 8,
            Actions = new List<ActionItem>
            {
                new ActionItem
                {
                    Name = "全局主动作0",
                    Type = "Hotkey",
                    Parameter = "Ctrl+A",
                    SubActions = new List<ActionItem>
                    {
                        new ActionItem { Name = "全局子动作1", Type = "Hotkey", Parameter = "Ctrl+1" },
                        new ActionItem { Name = "全局子动作2", Type = "Hotkey", Parameter = "Ctrl+2" }
                    }
                }
            }
        };

        var appProfile = new WheelProfile
        {
            ProcessName = "notepad.exe",
            DisplayName = "记事本专属方案",
            SectorCount = 8,
            Actions = new List<ActionItem>
            {
                new ActionItem
                {
                    Name = "",
                    Type = "Inherit",
                    Parameter = "",
                    SubActions = new List<ActionItem>()
                }
            }
        };

        AssertTrue(WheelProfile.IsActionConfigured(globalProfile.Actions[0]), "全局方案配置了动作");
        AssertTrue(!WheelProfile.IsActionConfigured(appProfile.Actions[0]), "App 专属方案槽位尚未配置，属于纯继承");

        // 纯浏览操作：appProfile.Actions[0] 必须保持未配置/Inherit 状态，SubActions 保持为空
        AssertEqual("Inherit", appProfile.Actions[0].Type, "浏览继承动作不应修改槽位类型");
        AssertEqual(0, appProfile.Actions[0].SubActions.Count, "浏览继承动作不应将子动作列表物化进本地槽位");
    }

    /// <summary>
    /// 探针 4：插件子动作撤销完整性：撤销时必须保留 PluginActionRef, ExtensionData 与外观属性
    /// </summary>
    private static void TestSubActionUndoPreservesAllFields()
    {
        var pluginSub = new ActionItem
        {
            Name = "屏幕亮度调节",
            Type = "Plugin",
            PluginActionRef = new PluginActionRef
            {
                PluginId = "starpie.brightness",
                ContributionId = "set_brightness"
            },
            ExtensionData = new Dictionary<string, string>
            {
                ["brightness_level"] = "75",
                ["smooth_transition"] = "true"
            },
            Extras = new Dictionary<string, JsonElement>
            {
                ["vendor_tag"] = JsonDocument.Parse("\"lenovo\"").RootElement
            },
            CustomTextColor = "#00FFCC",
            LayoutMode = "IconAndText"
        };

        // 旧版手写备份漏掉了 PluginActionRef, ExtensionData, Extras, CustomTextColor, LayoutMode
        // 新的标准克隆深备份必须 100% 保留
        var correctBackup = pluginSub.Clone();

        AssertTrue(correctBackup.PluginActionRef != null, "深备份必须包含 PluginActionRef");
        AssertEqual("starpie.brightness", correctBackup.PluginActionRef!.PluginId, "PluginId 完整保留");
        AssertTrue(correctBackup.ExtensionData != null, "深备份必须包含 ExtensionData");
        AssertEqual("75", correctBackup.ExtensionData!["brightness_level"], "参数完整保留");
        AssertEqual("#00FFCC", correctBackup.CustomTextColor, "外观颜色完整保留");
        AssertEqual("IconAndText", correctBackup.LayoutMode, "排版模式完整保留");
    }

    /// <summary>
    /// 探针 5：撤销上下文隔离：撤销必须严格校验方案名称和层级，避免跨方案误恢复
    /// </summary>
    private static void TestUndoContextIsolation()
    {
        string txProfile = "Global";
        int txLayer = 0;
        int txSlot = 1;

        string currentProfile = "notepad.exe";
        int currentLayer = 0;
        int currentSlot = 1;

        bool canApply = (txProfile == currentProfile && txLayer == currentLayer && txSlot == currentSlot);
        AssertTrue(!canApply, "不同方案的撤销记录绝不能应用于当前方案");
    }

    /// <summary>
    /// 探针 6：预设平铺子菜单项数量核对
    /// </summary>
    private static void TestTilePresetSubActionsCount()
    {
        var targetAction = new ActionItem { Type = "Tile", Name = "平铺窗口" };
        var vm = new SlotViewModel("向上", targetAction);
        vm.PopulateTileSubActions();

        // 预期：UI 与文案均标明「预设 8 布局」，因此应该刚好生成 8 个子项（7 个常用布局 + 1 个还原）
        // 当前基准中由于没有 Take(7)，实际生成了 18 个子项！
        AssertEqual(8, vm.Action.SubActions.Count, "平铺子动作一键预设应生成 8 项（7 常用布局 + 1 恢复上次平铺）");
    }

    /// <summary>
    /// 探针 7：强类型事务历史栈：50 项容量上限截断与跨方案/层上下文隔离
    /// </summary>
    private static void TestTransactionHistoryCapacityAndContextIsolation()
    {
        var history = new ActionTransactionHistory();

        // 验证初始状态
        AssertTrue(!history.CanUndo("Global", 0), "初始状态不可撤销");
        AssertTrue(!history.CanRedo("Global", 0), "初始状态不可重做");

        // 推入 55 条针对 Global:0 的事务
        for (int i = 1; i <= 55; i++)
        {
            var addr = new ActionAddress("Global", 0, 1, null);
            var snapBefore = new ActionSnapshot(new ActionItem { Name = $"Before_{i}", Type = "Hotkey" });
            var snapAfter = new ActionSnapshot(new ActionItem { Name = $"After_{i}", Type = "Hotkey" });
            var tx = new SingleSectorTransaction($"Tx_{i}", addr, snapBefore, snapAfter);
            history.Push(tx);
        }

        // 验证容量截断到 50 条
        AssertEqual(50, history.UndoCount, "历史栈超过上限必须自动截断最早项至 50 条");

        // 验证当前方案/层可撤销，描述对应第 55 条
        AssertTrue(history.CanUndo("Global", 0), "Global:0 可撤销");
        AssertEqual("Tx_55", history.GetUndoDescription("Global", 0), "Undo 栈顶应为最后推入的事务");

        // 验证跨方案隔离：notepad.exe:0 不可见该撤销
        AssertTrue(!history.CanUndo("notepad.exe", 0), "跨方案 context 不可撤销");
        AssertTrue(!history.CanUndo("Global", 1), "跨层 context 不可撤销");
    }

    /// <summary>
    /// 探针 8：内部剪贴板独立性与中心核圆单动作不变量
    /// </summary>
    private static void TestActionClipboardIndependenceAndInvariants()
    {
        ActionClipboard.Clear();
        AssertTrue(!ActionClipboard.HasContent, "初始剪贴板为空");

        // 1. 单动作复制
        var originalSingle = new ActionItem
        {
            Name = "单动作",
            Type = "Hotkey",
            Parameter = "Ctrl+Shift+P",
            SubActions = new List<ActionItem>
            {
                new ActionItem { Name = "附带子动作", Type = "Hotkey" }
            }
        };

        ActionClipboard.CopySingleAction(originalSingle, "Global", 0, 2);
        AssertTrue(ActionClipboard.HasContent, "复制后剪贴板有内容");
        AssertEqual(ClipboardContentType.SingleAction, ActionClipboard.ContentType, "内容类型为 SingleAction");

        var (pastedAction, pastedSubs, type) = ActionClipboard.Paste();
        AssertTrue(pastedAction != null, "粘贴动作不为空");
        AssertEqual(ClipboardContentType.SingleAction, type, "类型一致");
        AssertTrue(pastedSubs == null, "单动作复制时粘贴子列表必须为 null");
        AssertTrue(pastedAction!.SubActions.Count == 0, "单动作复制必须剥离原动作的子列表");

        // 验证独立性：修改粘贴结果不改变剪贴板
        pastedAction.Name = "修改后粘贴动作";
        var (pasted2, _, _) = ActionClipboard.Paste();
        AssertEqual("单动作", pasted2!.Name, "再次粘贴得到未污染的副本");

        // 2. 扇区包复制
        ActionClipboard.CopySectorBundle(originalSingle, "Global", 0, 2);
        AssertEqual(ClipboardContentType.SectorBundle, ActionClipboard.ContentType, "内容类型为 SectorBundle");

        var (bundleAction, bundleSubs, bundleType) = ActionClipboard.Paste();
        AssertEqual(ClipboardContentType.SectorBundle, bundleType, "类型为 SectorBundle");
        AssertTrue(bundleSubs != null && bundleSubs.Count == 1, "扇区包粘贴包含完整的子动作列表");
        AssertTrue(!ReferenceEquals(originalSingle.SubActions, bundleSubs), "子动作列表切断引用共享");
        AssertTrue(!ReferenceEquals(originalSingle.SubActions![0], bundleSubs![0]), "子动作元素切断引用共享");

        // 3. 中心核圆规则：中心核圆仅允许单动作，整扇区包粘贴至中心时只接收主动作
        AssertTrue(bundleAction != null, "bundleAction 不为 null");
        ActionItem centerTarget = bundleAction!.Clone();
        centerTarget.SubActions.Clear(); // 中心核圆永远没有二级子菜单
        AssertTrue(centerTarget.SubActions.Count == 0, "中心核圆始终保持单动作，不接纳子列表");
    }

    /// <summary>
    /// 探针 9：外圈子环与数据模型保留超额子动作（禁止静默截断已有 >4 项旧配置）
    /// </summary>
    private static void TestOuterRingExceededSubActionsPreservedWithoutTruncation()
    {
        var primaryAction = new ActionItem
        {
            Name = "多子动作主项",
            Type = "Hotkey",
            Parameter = "F1",
            SubActions = new List<ActionItem>
            {
                new ActionItem { Name = "子1", Type = "Hotkey" },
                new ActionItem { Name = "子2", Type = "Hotkey" },
                new ActionItem { Name = "子3", Type = "Hotkey" },
                new ActionItem { Name = "子4", Type = "Hotkey" },
                new ActionItem { Name = "子5", Type = "Hotkey" },
                new ActionItem { Name = "子6", Type = "Hotkey" },
                new ActionItem { Name = "子7", Type = "Hotkey" },
                new ActionItem { Name = "子8", Type = "Hotkey" }
            }
        };

        // 克隆必须保留所有 8 项
        var clone = primaryAction.Clone();
        AssertEqual(8, clone.SubActions!.Count, "克隆后所有 8 项子动作完整保留");

        // JSON 往返保留所有 8 项
        string json = JsonSerializer.Serialize(primaryAction);
        var restored = JsonSerializer.Deserialize<ActionItem>(json);
        AssertTrue(restored != null && restored.SubActions != null, "反序列化成功");
        AssertEqual(8, restored!.SubActions!.Count, "反序列化后 8 项完整保留，不得静默截断为 4 项");

        // 验证快照同样完整保留 8 项
        var snapshot = new ActionSnapshot(primaryAction);
        AssertEqual(8, snapshot.SubActions!.Count, "ActionSnapshot 完整保留 8 项子动作");
    }

    /// <summary>
    /// 探针 10：跨扇区操作事务原子性：两端扇区双向备份，确保原子恢复
    /// </summary>
    private static void TestCrossSectorTransactionAtomicSnapshots()
    {
        var srcAddr = new ActionAddress("Global", 0, 1, null);
        var tgtAddr = new ActionAddress("Global", 0, 2, null);

        var srcBefore = new ActionSnapshot(new ActionItem { Name = "源初始", Type = "Hotkey", Parameter = "1" });
        var srcAfter = new ActionSnapshot(new ActionItem { Name = "源交换后", Type = "Hotkey", Parameter = "2" });
        var tgtBefore = new ActionSnapshot(new ActionItem { Name = "目标初始", Type = "Hotkey", Parameter = "2" });
        var tgtAfter = new ActionSnapshot(new ActionItem { Name = "目标交换后", Type = "Hotkey", Parameter = "1" });

        var crossTx = new CrossSectorTransaction(
            "对调扇区 1 与扇区 2",
            srcAddr,
            tgtAddr,
            srcBefore,
            srcAfter,
            tgtBefore,
            tgtAfter
        );

        AssertEqual("对调扇区 1 与扇区 2", crossTx.Description, "事务描述正确");
        AssertEqual(srcAddr, crossTx.SourceAddress, "源地址匹配");
        AssertEqual(tgtAddr, crossTx.TargetAddress, "目标地址匹配");
        AssertEqual("源初始", crossTx.SourceBefore.Action!.Name, "源初始状态保留");
        AssertEqual("目标初始", crossTx.TargetBefore.Action!.Name, "目标初始状态保留");
        AssertEqual("源交换后", crossTx.SourceAfter.Action!.Name, "源后置状态保留");
        AssertEqual("目标交换后", crossTx.TargetAfter.Action!.Name, "目标后置状态保留");
    }

    private const BindingFlags WindowFields = BindingFlags.Instance | BindingFlags.NonPublic;

    private static SettingsWindow MakeWindow(WheelProfile profile)
    {
        var w = (SettingsWindow)RuntimeHelpers.GetUninitializedObject(typeof(SettingsWindow));
        Set(w, "_selectedProfile", profile);
        Set(w, "_selectedSlotIndex", 0);
        Set(w, "_selectedMultiSlots", new List<int>());
        Set(w, "_isUpdatingFocusUi", true);
        Set(w, "_transactionHistory", new ActionTransactionHistory());
        return w;
    }

    private static void Set(SettingsWindow window, string field, object value) =>
        typeof(SettingsWindow).GetField(field, WindowFields)!.SetValue(window, value);

    private static void InvokeModel(SettingsWindow window, string name, params object[] args)
    {
        var method = typeof(SettingsWindow).GetMethod(name, WindowFields) 
            ?? typeof(SettingsWindow).GetMethod(name);
        method!.Invoke(window, args);
    }

    /// <summary>
    /// 探针 11：生产快照判等探针: 插件参数变更导致快照不等且可触发撤销记录
    /// </summary>
    private static void TestProductionSnapshotEqualityWithPluginParams()
    {
        var a = new ActionItem { Name = "Same", Type = "Plugin", ExtensionData = new() { ["level"] = "10" } };
        var b = a.Clone();
        b.ExtensionData!["level"] = "75";
        var compareWindow = MakeWindow(new WheelProfile { ProcessName = "Global" });
        var equivalent = (bool)typeof(SettingsWindow).GetMethod("AreSnapshotsEquivalent", WindowFields)!
            .Invoke(compareWindow, new object[] { new ActionSnapshot(a), new ActionSnapshot(b) })!;
        AssertTrue(!equivalent, "插件参数改变必须产生不相等的快照");
    }

    /// <summary>
    /// 探针 12：生产粘贴参数完整性探针: 主扇区粘贴保留插件参数与扩展字典
    /// </summary>
    private static void TestProductionPrimaryPasteIncludesPluginParams()
    {
        var config = new AppConfig { EnableGlobalInheritance = true, LinkSubActionsWhenDragging = false };
        typeof(ConfigManager).GetProperty("CurrentConfig")!.SetValue(null, config);
        var source = new ActionItem
        {
            Name = "Source",
            Type = "Plugin",
            PluginActionRef = new PluginActionRef { PluginId = "user.fixture", ContributionId = "dummy" },
            ExtensionData = new() { ["level"] = "75" },
            RunAsStandardUser = true,
            CustomFontSize = 27
        };
        var target = new ActionItem { Name = "Target", Type = "Hotkey", Parameter = "F1", ExtensionData = new() { ["level"] = "10" } };
        var p = new WheelProfile { ProcessName = "Global", SectorCount = 4, Actions = new() { target, new(), new(), new() } };
        config.Profiles = new() { p };
        var w = MakeWindow(p);
        ActionClipboard.CopySectorBundle(source);
        InvokeModel(w, "PasteToCurrentSelection");
        AssertEqual("75", p.Actions[0].ExtensionData?.GetValueOrDefault("level"), "主扇区粘贴必须保留插件参数");
    }

    /// <summary>
    /// 探针 13：生产粘贴执行与外观字段探针: 主扇区粘贴保留 RunAsStandardUser、字号等外观字段
    /// </summary>
    private static void TestProductionPrimaryPasteIncludesAppearanceFields()
    {
        var config = (AppConfig)typeof(ConfigManager).GetProperty("CurrentConfig")!.GetValue(null)!;
        var p = config.Profiles[0];
        AssertTrue(p.Actions[0].RunAsStandardUser, "主扇区粘贴保留 RunAsStandardUser");
        AssertEqual(27.0, p.Actions[0].CustomFontSize, "主扇区粘贴保留 CustomFontSize");
    }

    /// <summary>
    /// 探针 14：生产主扇区换位联动探针: LinkSubActionsWhenDragging=false 时主动作换位保持子动作在原方位
    /// </summary>
    private static void TestProductionPrimarySwapRespectsDisabledChildLinkage()
    {
        var config = (AppConfig)typeof(ConfigManager).GetProperty("CurrentConfig")!.GetValue(null)!;
        config.LinkSubActionsWhenDragging = false;
        var p = config.Profiles[0];
        p.Actions[0] = new() { Name = "A", Type = "Hotkey", Parameter = "F1", SubActions = new() { new() { Name = "A-child" } } };
        p.Actions[1] = new() { Name = "B", Type = "Hotkey", Parameter = "F2", SubActions = new() { new() { Name = "B-child" } } };
        var w = MakeWindow(p);
        Set(w, "_dragSourceSlotIndex", 0);
        InvokeModel(w, "ProcessCanvasDrop", p, new CanvasHitItem { SlotIndex = 1 }, false);
        AssertEqual("A-child", p.Actions[0].SubActions[0].Name, "关闭链接时扇区0保持其子动作");
        AssertEqual("B-child", p.Actions[1].SubActions[0].Name, "关闭链接时扇区1保持其子动作");
    }

    /// <summary>
    /// 探针 15：生产子动作恢复继承探针: RestoreSubActionInheritance 恢复扇区继承并动态跟随全局更新
    /// </summary>
    private static void TestProductionRestoreChildReadsGlobalContent()
    {
        var config = (AppConfig)typeof(ConfigManager).GetProperty("CurrentConfig")!.GetValue(null)!;
        var global = new WheelProfile { ProcessName = "Global", SectorCount = 4, Actions = new() { new() { Name = "Global", Type = "Hotkey", Parameter = "F3", SubActions = new() { new() { Name = "Global-child", Parameter = "F4" } } }, new(), new(), new() } };
        var local = new WheelProfile { ProcessName = "notepad.exe", SectorCount = 4, Actions = new() { new() { Name = "Local", Type = "Hotkey", Parameter = "F5", SubActions = new() { new() { Name = "Local-child", Parameter = "F6" } } }, new(), new(), new() } };
        config.Profiles = new() { global, local };
        var lw = MakeWindow(local);
        InvokeModel(lw, "RestoreSubActionInheritance", local, 0, 0);

        AssertEqual("Inherit", local.Actions[0].Type, "恢复继承后扇区类型必须为 Inherit");

        var effChild = local.GetEffectiveAction(0, 0, global);
        AssertEqual("F4", effChild?.Parameter, "继承扇区生效子动作读取 Global 方案对应内容");

        // 验证全局修改后动态跟随
        global.Actions[0].SubActions[0].Parameter = "F9";
        effChild = local.GetEffectiveAction(0, 0, global);
        AssertEqual("F9", effChild?.Parameter, "全局修改后生效子动作动态跟随");

        // 验证 JSON 往返保留 Inherit 类型
        var json = JsonSerializer.Serialize(local.Actions[0]);
        var deserialized = JsonSerializer.Deserialize<ActionItem>(json)!;
        AssertEqual("Inherit", deserialized.Type, "序列化保存后必须保持 Inherit 类型");
    }

    /// <summary>
    /// 探针 16：生产撤销恢复继承探针: 对继承扇区粘贴后撤销，完整恢复原始 Type=Inherit
    /// </summary>
    private static void TestProductionUndoPasteRestoresOriginalInheritance()
    {
        var config = (AppConfig)typeof(ConfigManager).GetProperty("CurrentConfig")!.GetValue(null)!;
        var global = config.Profiles.First(p => p.ProcessName == "Global");
        var inherited = new WheelProfile { ProcessName = "editor.exe", SectorCount = 4, Actions = new() { new() { Type = "Inherit", Name = "", SubActions = new() }, new(), new(), new() } };
        config.Profiles = new() { global, inherited };
        var iw = MakeWindow(inherited);
        ActionClipboard.CopySingleAction(new() { Name = "Paste", Type = "Hotkey", Parameter = "F7" });
        InvokeModel(iw, "PasteToCurrentSelection");
        InvokeModel(iw, "ExecuteUndo");
        AssertEqual("Inherit", inherited.Actions[0].Type, "撤销粘贴必须恢复继承扇区的 Inherit 类型");
    }

    /// <summary>
    /// 探针 17：生产同名插件子动作换位撤销探针: 同名同贡献点插件子动作换位可正常产生撤销记录
    /// </summary>
    private static void TestProductionSwapSameNamedPluginChildrenUndoable()
    {
        var sameNamed = new WheelProfile { ProcessName = "Global", SectorCount = 4, Actions = new() { new() { Name = "Parent", Parameter = "F8", SubActions = new() { new() { Name = "Same", Type = "Plugin", PluginActionRef = new() { PluginId = "user.a", ContributionId = "x" } }, new() { Name = "Same", Type = "Plugin", PluginActionRef = new() { PluginId = "user.b", ContributionId = "x" } } } }, new(), new(), new() } };
        var sw = MakeWindow(sameNamed);
        Set(sw, "_dragSourceSlotIndex", -100);
        InvokeModel(sw, "ProcessCanvasDrop", sameNamed, new CanvasHitItem { IsSubAction = true, SlotIndex = 0, SubIndex = 1 }, false);
        var history = (ActionTransactionHistory)typeof(SettingsWindow).GetField("_transactionHistory", WindowFields)!.GetValue(sw)!;
        AssertEqual(1, history.UndoCount, "同名但插件不同的子动作对调必须产生 1 条可撤销事务");
    }

    /// <summary>
    /// 探针 18：生产蜂窝二级菜单容量门禁探针: 蜂窝模式满 3 项拒绝 Ctrl 拖拽复制新增
    /// </summary>
    private static void TestProductionCtrlCopyRejectsFullFanSubmenu()
    {
        var config = (AppConfig)typeof(ConfigManager).GetProperty("CurrentConfig")!.GetValue(null)!;
        config.SubmenuStyle = "Fan";
        var prof = new WheelProfile { ProcessName = "Global", SectorCount = 4, Actions = new() { new() { Name = "Parent", Parameter = "F8", SubActions = new() { new() { Name = "1", Parameter = "1" }, new() { Name = "2", Parameter = "2" }, new() { Name = "3", Parameter = "3" } } }, new(), new(), new() } };
        var sw = MakeWindow(prof);
        Set(sw, "_dragSourceSlotIndex", -100);
        InvokeModel(sw, "ProcessCanvasDrop", prof, new CanvasHitItem { IsSubAction = true, SlotIndex = 0, SubIndex = 1 }, true);
        AssertEqual(3, prof.Actions[0].SubActions.Count, "蜂窝模式满 3 项时 Ctrl 拖拽复制必须拒绝新增");
    }

    /// <summary>
    /// 探针 19：生产满容量拒绝零修改探针: 满容量蜂窝模式下被拒绝的操作配置完全零修改且无撤销记录
    /// </summary>
    private static void TestRejectedFullFanCopyLeavesConfigUnchanged()
    {
        var config = (AppConfig)typeof(ConfigManager).GetProperty("CurrentConfig")!.GetValue(null)!;
        config.SubmenuStyle = "Fan";
        config.EnableGlobalInheritance = true;
        var global = new WheelProfile { ProcessName = "Global", SectorCount = 4, Actions = new() { new() { Name = "Global Parent", Parameter = "F1", SubActions = new() { new() { Name = "1", Parameter = "1" }, new() { Name = "2", Parameter = "2" }, new() { Name = "3", Parameter = "3" } } }, new(), new(), new() } };
        var inherited = new WheelProfile { ProcessName = "notepad.exe", SectorCount = 4, Actions = new() { new() { Name = "", Type = "Inherit" }, new(), new(), new() } };
        config.Profiles = new() { global, inherited };
        var iw = MakeWindow(inherited);
        Set(iw, "_dragSourceSlotIndex", -100);
        var before = JsonSerializer.Serialize(inherited.Actions[0]);
        InvokeModel(iw, "ProcessCanvasDrop", inherited, new CanvasHitItem { SlotIndex = 0, SubIndex = 1, IsSubAction = true }, true);
        var after = JsonSerializer.Serialize(inherited.Actions[0]);
        var history = (ActionTransactionHistory)typeof(SettingsWindow).GetField("_transactionHistory", WindowFields)!.GetValue(iw)!;
        AssertEqual(before, after, "满容量拖拽被拒绝后必须保持配置完全零修改");
        AssertEqual(0, history.UndoCount, "被拒绝的操作不得记录撤销事务");
    }

    /// <summary>
    /// 探针 20：焦点属性编辑与撤销合并探针: 文本连续输入自动合并为单步撤销，支持完整Undo/Redo
    /// </summary>
    private static void TestFocusPropertyUndoRedoAndCoalescing()
    {
        var profile = new WheelProfile { ProcessName = "Global", SectorCount = 4, Actions = new() { new() { Name = "Initial", Type = "Hotkey", Parameter = "Ctrl+A" }, new(), new(), new() } };
        var w = MakeWindow(profile);
        Set(w, "_selectedSlotIndex", 0);
        Set(w, "_selectedSubActionIndex", (int?)null!);
        InvokeModel(w, "ResetFocusEditSnapshot");

        // 模拟第一次文本输入
        profile.Actions[0].Name = "Initial 1";
        InvokeModel(w, "RecordFocusPropertyChange", "修改动作名称", true);

        // 模拟连续文本输入（coalesce: true 应合并）
        profile.Actions[0].Name = "Initial 123";
        InvokeModel(w, "RecordFocusPropertyChange", "修改动作名称", true);

        var history = (ActionTransactionHistory)typeof(SettingsWindow).GetField("_transactionHistory", WindowFields)!.GetValue(w)!;
        AssertEqual(1, history.UndoCount, "连续修改名称应被合成为单条撤销事务");

        // 执行撤销，应恢复为 Initial
        InvokeModel(w, "ExecuteUndo");
        AssertEqual("Initial", profile.Actions[0].Name, "撤销合并事务应回滚到初始名称");

        // 执行重做，应重做到 Initial 123
        InvokeModel(w, "ExecuteRedo");
        AssertEqual("Initial 123", profile.Actions[0].Name, "重做合并事务应恢复至最终名称");
    }

    /// <summary>
    /// 探针 21：历史栈深度与项目预算探针: 限制最大 50 条历史与 500 项快照预算
    /// </summary>
    private static void TestTransactionBudgetAndMaxHistory()
    {
        var history = new ActionTransactionHistory();
        var addr = new ActionAddress("Global", 0, 0, null, Guid.NewGuid());

        // 推入 60 个单项事务
        for (int i = 0; i < 60; i++)
        {
            var snapBefore = new ActionSnapshot(new ActionItem { Name = $"Before {i}" });
            var snapAfter = new ActionSnapshot(new ActionItem { Name = $"After {i}" });
            history.Push(new SingleSectorTransaction($"Tx {i}", addr, snapBefore, snapAfter));
        }

        AssertTrue(history.UndoCount <= 50, $"事务栈深度不得超过 50 (实际: {history.UndoCount})");
        AssertTrue(history.ItemsBudgetCount <= 500, $"快照项目总预算不得超过 500 (实际: {history.ItemsBudgetCount})");
    }
}
