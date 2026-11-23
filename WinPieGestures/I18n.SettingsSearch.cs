namespace WinPieGestures;

public static partial class I18n
{
    private static void RegisterSettingsSearchStrings(Action<string, string, string?, string?, string?> add)
    {
        add("SettingsSearchOpen", "搜索设置（Ctrl+F）", "搜尋設定（Ctrl+F）", "Search settings (Ctrl+F)", "設定を検索（Ctrl+F）");
        add("SettingsSearchClose", "关闭搜索", "關閉搜尋", "Close search", "検索を閉じる");
        add("SettingsSearchPrompt", "输入功能名称或关键词", "輸入功能名稱或關鍵字", "Enter a setting name or keyword", "設定名やキーワードを入力");
        add("SettingsSearchEmpty", "没有找到相关设置", "找不到相關設定", "No matching settings", "一致する設定はありません");
        add("SettingsSearchResults", "设置搜索结果", "設定搜尋結果", "Settings search results", "設定の検索結果");
        add("SettingsSearchGeometry", "轮盘形状、尺寸与间距", "輪盤形狀、尺寸與間距", "Wheel shape, size and spacing", "ホイールの形状・サイズ・間隔");
        add("SettingsSearchMarketplace", "插件市场", "外掛市集", "Plugin marketplace", "プラグインストア");
        add("SettingsSearchPluginSort", "插件筛选与排序", "外掛篩選與排序", "Filter and sort plugins", "プラグインの絞り込みと並べ替え");
        add("SettingsSearchPluginSource", "插件下载源", "外掛下載來源", "Plugin download source", "プラグインのダウンロード元");
        add("SettingsSearchInstalledPlugins", "已安装插件", "已安裝外掛", "Installed plugins", "インストール済みプラグイン");
        add("SettingsSearchTriggerWords", "热键 快捷键 组合键 录制 鼠标右键 唤醒", "快速鍵 組合鍵 錄製 滑鼠右鍵 喚醒", "hotkey shortcut keyboard recording right mouse button", "ホットキー ショートカット キーの記録 右クリック");
        add("SettingsSearchInvocationWords", "长按 拖动 仅长按 呼出 唤出 延迟", "長按 拖曳 僅長按 喚出 延遲", "long press hold drag summon delay", "長押し ドラッグ 呼び出し 遅延");
        add("SettingsSearchFullScreenWords", "全屏 游戏 独占 防误触", "全螢幕 遊戲 獨佔 防誤觸", "fullscreen full screen game exclusive bypass", "全画面 フルスクリーン ゲーム 排他");
        add("SettingsSearchScopeWords", "黑名单 白名单 排除名单 允许名单 生效范围 场景隔离", "黑名單 白名單 排除清單 允許清單 生效範圍 情境隔離", "blacklist whitelist blocklist allowlist exclude scope application rules", "ブラックリスト ホワイトリスト 除外リスト 許可リスト 適用範囲");
        add("SettingsSearchSubmenuWords", "二级轮盘 子菜单 外圈子环 蜂窝扇 同时展开 父级锁定", "二級輪盤 子選單 外圈子環 蜂巢扇 同時展開 父層鎖定", "submenu cascade outer ring honeycomb fan simultaneous expand parent lock", "サブメニュー 外周リング ハニカム 同時展開 親の固定");
        add("SettingsSearchToleranceWords", "中心死区 防抖 松键 灵敏度", "中心死區 防抖 放開按鍵 靈敏度", "dead zone debounce release sensitivity", "デッドゾーン チャタリング 感度");
        add("SettingsSearchThemeWords", "轮盘风格 艺术风格 主题 配色 颜色", "輪盤風格 藝術風格 主題 配色 色彩", "wheel theme art style colors colour palette", "ホイール テーマ アートスタイル 配色 色");
        add("SettingsSearchGeometryWords", "几何 扇区大小 外半径 内半径 倒角 圆角 蜂窝 间隙", "幾何 扇區大小 外半徑 內半徑 倒角 圓角 蜂巢 間隙", "geometry sector radius inner outer corner rounding hexagon honeycomb gap", "ジオメトリ 扇区 半径 角丸 ハニカム 隙間");
        add("SettingsSearchTypographyWords", "图文 图标 字体 字号 仅图标 文字颜色", "圖文 圖示 字型 字級 僅圖示 文字顏色", "typography icons text font size icon only text color", "タイポグラフィ アイコン 文字 フォント 文字色");
        add("SettingsSearchProfilesWords", "配置方案 方案库 方案图标 轮盘层", "設定方案 方案庫 方案圖示 輪盤層", "profiles profile library profile icon layers", "プロファイル ライブラリ プロファイルのアイコン レイヤー");
        add("SettingsSearchBindingWords", "应用绑定 进程 组件识别 生效条件", "應用程式繫結 處理程序 元件辨識 生效條件", "bind application process component identification conditions", "アプリの割り当て プロセス コンポーネント識別 条件");
        add("SettingsSearchDisplayWords", "全部常驻 常驻展开 逐级展开 多级轮盘 固定圈", "全部常駐 常駐展開 逐級展開 多層輪盤 固定圈", "always visible all resident progressive expand fixed rings multi level", "常時表示 段階展開 多階層 固定リング");
        add("SettingsSearchStartupWords", "Windows 开机启动 自动启动 登录启动", "Windows 開機啟動 自動啟動 登入啟動", "startup autostart boot login launch", "自動起動 スタートアップ ログイン起動");
        add("SettingsSearchOcrWords", "OCR 文字识别 屏幕识别", "OCR 文字辨識 螢幕辨識", "OCR text recognition screen capture", "OCR 文字認識 画面キャプチャ");
        add("SettingsSearchBackupWords", "备份 恢复 导入配置 导出配置", "備份 還原 匯入設定 匯出設定", "backup restore import export config", "バックアップ 復元 設定のインポート エクスポート");
        add("SettingsSearchUpdateWords", "检查更新 新版本 升级", "檢查更新 新版本 升級", "check updates version upgrade", "アップデート 更新確認 新しいバージョン");
        add("SettingsSearchDownloadSourceWords", "软件更新 更新源 下载源 镜像 AtomGit GitHub 延迟 测速", "軟體更新 更新來源 下載來源 鏡像 AtomGit GitHub 延遲 測速", "update download source mirror AtomGit GitHub latency", "更新のダウンロード ミラー AtomGit GitHub 遅延");
        add("SettingsSearchMarketplaceWords", "插件商店 安装插件 下载插件 官方插件", "外掛商店 安裝外掛 下載外掛 官方外掛", "plugin store install download official extensions", "プラグインストア インストール ダウンロード 公式");
        add("SettingsSearchPluginSortWords", "热度 下载量 分类 更新时间 发布日期", "熱門度 下載量 分類 更新時間 發布日期", "popularity downloads category updated published", "人気 ダウンロード数 カテゴリ 更新日時 公開日");
        add("SettingsSearchInstalledWords", "插件管理 卸载插件 禁用插件 已安装", "外掛管理 解除安裝 停用外掛 已安裝", "manage plugins uninstall disable installed extensions", "プラグイン管理 アンインストール 無効化");
    }
}
