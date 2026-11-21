namespace WinPieGestures;

public static partial class I18n
{
    private static void RegisterStudioRailStrings(Action<string, string, string, string, string> add)
    {
        add("StudioRailLibrary", "方案库", "方案庫", "Profile library", "プロファイル一覧");
        add("StudioRailSearch", "搜索名称、进程、#标签", "搜尋名稱、處理程序、#標籤", "Search name, process, #tag", "名前・プロセス・#タグを検索");
        add("StudioRailTagFilter", "筛选标签", "篩選標籤", "Filter by tag", "タグで絞り込む");
        add("StudioRailAllTags", "全部标签", "所有標籤", "All tags", "すべてのタグ");
        add("StudioRailNoMatch", "没有匹配的方案", "沒有符合的方案", "No matching profiles", "該当するプロファイルはありません");
        add("StudioRailLayers", "{0} 层", "{0} 層", "{0} layers", "{0} 層");
        add("StudioRailDepth", "{0} 级", "{0} 級", "Depth {0}", "深さ {0}");
        add("StudioRailApp", "程序情景", "程式情境", "Application", "アプリ別");
        add("StudioRailGlobal", "全局", "全域", "Global", "グローバル");
        add("StudioRailCopy", "复制方案", "複製方案", "Duplicate profile", "プロファイルを複製");
        add("StudioRailDelete", "删除方案", "刪除方案", "Delete profile", "プロファイルを削除");
        add("StudioRailExport", "导出方案", "匯出方案", "Export profile", "プロファイルを書き出す");
        add("StudioRailImport", "导入方案", "匯入方案", "Import profile", "プロファイルを読み込む");
        add("StudioRailImportName", "将方案添加到当前配置。请输入方案名称。", "將方案新增至目前設定。請輸入方案名稱。", "Choose a name to add this profile to the current configuration.", "現在の設定に追加するプロファイルの名前を入力してください。");
        add("StudioRailFileType", "StarPie 单方案包 (*.starpie-config)|*.starpie-config", "StarPie 單方案套件 (*.starpie-config)|*.starpie-config", "StarPie profile package (*.starpie-config)|*.starpie-config", "StarPie 単一プロファイル (*.starpie-config)|*.starpie-config");
        add("StudioRailWrongScope", "请选择通过方案卡片导出的单方案包。整份配置请在设置中导入。", "請選擇由方案卡片匯出的單方案套件。完整設定請在設定頁匯入。", "Choose a package exported from a profile card. Import full configurations in Settings.", "カードから書き出した単一プロファイルを選択してください。設定全体は設定画面で読み込めます。");
        add("StudioRailConflict", "导入方案的触发键或程序绑定与现有方案冲突。是否清除导入副本的触发键及绑定后添加？", "匯入方案的觸發鍵或程式綁定與既有方案衝突。是否清除匯入副本的觸發鍵及綁定後新增？", "This profile has conflicting triggers or application bindings. Add the imported copy with those triggers and bindings cleared?", "トリガーまたはアプリの割り当てが競合しています。読み込むコピーのトリガーと割り当てを解除して追加しますか？");
        add("StudioRailTags", "标签", "標籤", "Tags", "タグ");
        add("StudioRailTagPrompt", "用逗号或 # 分隔标签", "以逗號或 # 分隔標籤", "Separate tags with commas or #", "コンマまたは # でタグを区切ります");
        add("StudioRailIcon", "更换方案图标", "變更方案圖示", "Change profile icon", "プロファイルのアイコンを変更");
        add("StudioRailIconFilter", "程序和图标 (*.exe;*.ico)|*.exe;*.ico", "程式與圖示 (*.exe;*.ico)|*.exe;*.ico", "Programs and icons (*.exe;*.ico)|*.exe;*.ico", "プログラムとアイコン (*.exe;*.ico)|*.exe;*.ico");
        add("StudioRailLayerMenu", "层操作", "層操作", "Layer actions", "レイヤー操作");
    }
}
