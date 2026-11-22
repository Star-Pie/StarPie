namespace WinPieGestures;

public static partial class I18n
{
    private static void RegisterPluginLibraryStatsStrings(Action<string, string, string?, string?, string?> add)
    {
        add("PluginStatsDownloads", "GitHub 下载 {0}", "GitHub 下載 {0}", "{0} GitHub downloads", "GitHub ダウンロード {0}");
        add("PluginStatsDownloadsUnknown", "GitHub 下载量暂无", "GitHub 下載量暫無", "GitHub downloads unavailable", "GitHub ダウンロード数不明");
        add("PluginStatsDates", "首次发布 {0} · 最近更新 {1}", "首次發佈 {0} · 最近更新 {1}", "First published {0} · Updated {1}", "初回公開 {0} · 最終更新 {1}");
        add("PluginStatsReleaseLink", "GitHub 发布页", "GitHub 發佈頁", "GitHub release", "GitHub リリース");
        add("PluginStatsReleaseOpenFailed", "无法打开浏览器。", "無法開啟瀏覽器。", "Could not open the browser.", "ブラウザーを開けませんでした。");
        add("PluginStatsAllPlugins", "全部插件", "全部外掛", "All plugins", "すべてのプラグイン");
        add("PluginStatsHaveUpdates", "可更新", "可更新", "Updates available", "更新あり");
        add("PluginStatsInstalled", "已安装", "已安裝", "Installed", "インストール済み");
        add("PluginStatsNotInstalled", "未安装", "未安裝", "Not installed", "未インストール");
        add("PluginStatsRecentPublished", "近 30 天发布", "近 30 天發佈", "Published in last 30 days", "過去30日以内に公開");
        add("PluginStatsRecentUpdated", "近 30 天更新", "近 30 天更新", "Updated in last 30 days", "過去30日以内に更新");
        add("PluginStatsDownloads100", "下载量至少 100", "下載量至少 100", "At least 100 downloads", "ダウンロード数 100 以上");
        add("PluginStatsDownloads1000", "下载量至少 1,000", "下載量至少 1,000", "At least 1,000 downloads", "ダウンロード数 1,000 以上");
        add("PluginStatsNoMatches", "没有符合当前筛选条件的插件。", "沒有符合目前篩選條件的外掛。", "No plugins match the current filters.", "現在の条件に一致するプラグインがありません。");
        add("PluginStatsSortLabel", "插件排序", "外掛排序", "Sort plugins", "プラグインの並べ替え");
        add("PluginStatsSortName", "名称排序", "依名稱排序", "Sort by name", "名前順");
        add("PluginStatsSortPublished", "最新首次发布", "最新首次發佈", "Newest first release", "初回公開が新しい順");
        add("PluginStatsSortUpdated", "最近更新", "最近更新", "Recently updated", "更新が新しい順");
        add("PluginStatsSortDownloads", "下载量从高到低", "依下載量排序", "Most downloaded", "ダウンロード数順");
        add("PluginStatsRefreshFailed", "GitHub 统计暂不可用，可通过「刷新目录」重试。", "GitHub 統計暫不可用，可透過「重新整理目錄」重試。", "GitHub stats unavailable. Use Refresh catalog to retry.", "GitHub の統計を取得できません。カタログを更新して再試行できます。");
        add("PluginStatsUsingCache", "GitHub 统计刷新失败，显示 {0} 的缓存。", "GitHub 統計重新整理失敗，顯示 {0} 的快取。", "GitHub stats could not refresh. Showing data from {0}.", "GitHub の統計を更新できません。{0} のデータを表示しています。");
        add("PluginStatsUsingPartialCache", "GitHub 统计刷新失败，保留 {0} 刷新时的部分数据。", "GitHub 統計重新整理失敗，保留 {0} 更新時的部分資料。", "GitHub stats could not refresh. Keeping partial data from the refresh at {0}.", "GitHub の統計を更新できません。{0} に更新した一部のデータを表示しています。");
        add("PluginStatsNotLoaded", "点击「刷新目录」获取 GitHub 下载量和发布日期。", "點選「重新整理目錄」取得 GitHub 下載量及發佈日期。", "Refresh the catalog to load GitHub download counts and dates.", "カタログを更新すると GitHub のダウンロード数と公開日を取得できます。");
        add("PluginStatsLastRefresh", "GitHub 统计更新于 {0}", "GitHub 統計更新於 {0}", "GitHub stats refreshed {0}", "GitHub 統計の更新日時 {0}");
        add("PluginStatsPartialRefresh", "GitHub 统计于 {0} 刷新，部分数据暂缺或沿用缓存。", "GitHub 統計於 {0} 更新，部分資料暫缺或沿用快取。", "GitHub stats refreshed {0}. Some data is cached or unavailable.", "GitHub 統計を {0} に更新しました。一部はキャッシュまたは取得できていないデータです。");
    }
}
