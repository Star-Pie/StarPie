namespace WinPieGestures;

public static partial class I18n
{
    private static void RegisterWheelAppearanceStrings(Action<string, string, string, string, string> add)
    {
        add("WheelAppearanceTitle", "轮盘形状与尺寸", "輪盤形狀與尺寸", "Wheel shape and size", "ホイールの形状とサイズ");
        add("WheelAppearanceConfigure", "配置预览", "配置預覽", "Edit preview", "編集プレビュー");
        add("WheelAppearancePopup", "唤出预览", "喚出預覽", "Popup preview", "表示プレビュー");
        add("WheelAppearanceEffects", "视觉效果", "視覺效果", "Visual effects", "視覚効果");
        add("WheelAppearanceLegacy", "两级及以下", "兩級及以下", "One or two levels", "2 階層まで");
        add("WheelAppearanceMulti", "三级至五级", "三級至五級", "Three to five levels", "3～5 階層");
        add("WheelAppearanceLevel", "第 {0} 级", "第 {0} 級", "Level {0}", "第 {0} 階層");
        add("WheelAppearanceLevelCount", "{0} 级", "{0} 級", "{0} levels", "{0} 階層");
        add("WheelAppearanceDepthHint", "此处切换预览级数。实际菜单级数在「手势动作」中设置。", "此處切換預覽級數。實際選單級數在「手勢動作」中設定。", "Choose how many levels to preview. Set the menu depth in Gesture Actions.", "表示する階層数を選びます。実際の階層数はジェスチャー操作で設定します。");
        add("WheelAppearanceStructure", "轮盘布局", "輪盤版面配置", "Wheel layout", "ホイールの配置");
        add("WheelAppearancePresets", "尺寸预设", "尺寸預設", "Size presets", "サイズのプリセット");
        add("WheelAppearanceCompact", "紧凑", "緊湊", "Compact", "コンパクト");
        add("WheelAppearanceBalanced", "均衡", "均衡", "Balanced", "標準");
        add("WheelAppearanceSpacious", "宽松", "寬鬆", "Spacious", "ゆったり");
        add("WheelAppearanceScope", "调整范围", "調整範圍", "Adjust", "調整範囲");
        add("WheelAppearanceWhole", "整盘", "整個輪盤", "Whole wheel", "ホイール全体");
        add("WheelAppearancePrimary", "一级主轮盘", "一級主輪盤", "Primary wheel", "第 1 階層");
        add("WheelAppearanceSecondary", "二级子菜单", "二級子選單", "Secondary menu", "第 2 階層");
        add("WheelAppearanceResetScope", "恢复当前范围默认值", "還原目前範圍的預設值", "Reset current scope", "選択範囲を初期値に戻す");
        add("WheelAppearanceCompare", "对照默认", "對照預設", "Compare defaults", "初期値と比較");
        add("WheelAppearanceReturn", "返回当前", "返回目前設定", "Return to current", "現在の設定に戻る");
        add("WheelAppearanceShape", "扇区形状", "扇區形狀", "Sector shape", "セクターの形状");
        add("WheelAppearanceShapeInherit", "跟随一级", "跟隨一級", "Follow primary wheel", "第 1 階層に合わせる");
        add("WheelAppearanceHoneyHint", "固定蜂窝的各层共用格宽、格距和轮廓。在「整盘」中调整。", "固定蜂窩的各層共用格寬、格距和輪廓。在「整個輪盤」中調整。", "Fixed honeycomb levels share cell width, spacing, and shape. Adjust them under Whole wheel.", "固定ハニカムの各階層でセルの幅、間隔、形状を共有します。ホイール全体で調整できます。");
        add("WheelAppearanceDeepDefaults", "三级至五级通用尺寸", "三級至五級通用尺寸", "Shared dimensions for levels 3–5", "第 3～5 階層の共通サイズ");
        add("WheelAppearanceTierOverride", "独立微调本层厚度和间距", "單獨微調本層厚度和間距", "Fine-tune this level's thickness and spacing", "この階層の厚みと間隔を個別調整");
        add("WheelAppearanceTierInherited", "当前采用三级至五级的通用尺寸。", "目前採用三級至五級的通用尺寸。", "This level uses the shared dimensions.", "この階層には共通サイズが適用されています。");
        add("WheelAppearanceCellWidth", "格子宽度", "格子寬度", "Cell width", "セルの幅");
        add("WheelAppearanceCellGap", "格子间距", "格子間距", "Cell gap", "セルの間隔");
        add("WheelAppearanceCore", "中心圆半径", "中心圓半徑", "Core radius", "中心円の半径");
        add("WheelAppearanceOuter", "一级外半径", "一級外半徑", "Primary outer radius", "第 1 階層の外半径");
        add("WheelAppearanceInner", "一级内半径", "一級內半徑", "Primary inner radius", "第 1 階層の内半径");
        add("WheelAppearanceSecondarySize", "二级尺寸", "二級尺寸", "Secondary size", "第 2 階層のサイズ");
        add("WheelAppearanceThickness", "扇区厚度", "扇區厚度", "Sector thickness", "セクターの厚み");
        add("WheelAppearanceGap", "同层扇区间距", "同層扇區間距", "Gap within a level", "同じ階層内の間隔");
        add("WheelAppearanceSecondaryGap", "二级间距", "二級間距", "Secondary spacing", "第 2 階層の間隔");
        add("WheelAppearanceRingGap", "层间距离", "層間距離", "Gap between levels", "階層間の間隔");
        add("WheelAppearanceCorner", "边角圆润度", "邊角圓潤度", "Corner radius", "角の丸み");
        add("WheelAppearanceFine", "精细参数", "精細參數", "Fine adjustment", "詳細設定");
        add("WheelAppearanceCanvasHint", "点击扇区选择层级，拖动尺寸手柄可直接调整。虚线轮廓表示空槽。", "點擊扇區選擇階層，拖曳尺寸控點可直接調整。虛線輪廓代表空槽。", "Click a sector to select its level. Drag the size handle to adjust it. Dashed outlines mark empty slots.", "セクターをクリックして階層を選び、ハンドルをドラッグしてサイズを調整します。破線は空きスロットです。");
        add("WheelAppearancePopupHint", "显示已配置动作及通向有效子项的父扇区。", "顯示已配置動作及通往有效子項的父扇區。", "Shows configured actions and parent sectors that lead to available actions.", "設定済みの操作と、有効な子項目を持つ親セクターが表示されます。");
        add("WheelAppearancePopupFixedHint", "此处显示一级和二级。要预览第三级以上，请在「手势动作」中启用固定圈。", "此處顯示一級和二級。要預覽第三級以上，請在「手勢動作」中啟用固定圈。", "Showing levels 1 and 2. Enable fixed rings in Gesture Actions to preview level 3 and up.", "第1・第2階層を表示しています。第3階層以降をプレビューするには、ジェスチャー操作で固定リングを有効にしてください。");
    }
}
