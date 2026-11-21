namespace WinPieGestures;

public static partial class I18n
{
    private static void RegisterStudioPaletteStrings(Action<string, string, string?, string?, string?> Add)
    {
        Add("StudioPaletteTitle", "常用动作", "常用動作", "Action dock", "よく使うアクション");
        Add("StudioAddChild", "添加下级子动作", "新增下級子動作", "Add child action", "子アクションを追加");
        Add("StudioWorkflow", "工作流 / 宏", "工作流程 / 巨集", "Workflow / macro", "ワークフロー / マクロ");
        Add("StudioPaletteHint", "点击以应用到当前选择，或拖至画布位置。", "點選以套用至目前選取項目，或拖至畫布位置。", "Click to apply to the selection or drag to a canvas slot.", "クリックで選択項目に適用、またはキャンバスのスロットにドラッグ。");
        Add("StudioProviderUnavailable", "提供此动作的插件尚未就绪。", "提供此動作的外掛尚未就緒。", "The plugin providing this action is unavailable.", "このアクションを提供するプラグインは利用できません。");
        Add("StudioPlaceAction", "添加 {0} → {1}", "新增 {0} → {1}", "Add {0} → {1}", "{0} を追加 → {1}");
        Add("StudioReplaceAction", "替换 {0} → {1}", "取代 {0} → {1}", "Replace with {0} → {1}", "{0} に置換 → {1}");
        Add("StudioDropUnavailable", "此位置无法放置该动作", "此位置無法放置此動作", "This action cannot be placed here", "ここにはこのアクションを配置できません");
        Add("StudioApplyAction", "应用常用动作", "套用常用動作", "Apply dock action", "ドックのアクションを適用");
        Add("StudioDragInsert", "插入", "插入", "Insert", "挿入");
        Add("StudioDragSwap", "交换", "交換", "Swap", "交換");
        Add("StudioDragMove", "移动", "移動", "Move", "移動");
    }
}
