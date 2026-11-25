using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    private const string OldTag = "modules-2026.09.01.1";
    private const string CurrentTag = "modules-2026.11.20.1";
    private const string NewTag = "modules-2026.11.21.1";
    private const string OldAsset = "StarPie.Plugin.Alpha-1.0.0.spkg";
    private const string CurrentAsset = "StarPie.Plugin.Alpha-1.1.0.spkg";
    private const string NewAsset = "StarPie.Plugin.Beta-1.0.0.spkg";
    private const string MissingAsset = "StarPie.Plugin.Gamma-1.0.0.spkg";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-22T00:00:00Z");
    private static int _checks;

    [STAThread]
    private static void Main(string[] args)
    {
        Check(args is [_, "--test-instance"] &&
            Hash(args[0]) == Hash(typeof(OfficialPluginReleaseStats).Assembly.Location), "exact candidate DLL and isolated entry point");
        string temp = Directory.CreateTempSubdirectory("starpie-github-stats-").FullName;
        foreach (string key in new[] { "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" })
            Environment.SetEnvironmentVariable(key, temp);
        try { Run(); }
        finally { Directory.Delete(temp, recursive: true); }
        Console.WriteLine($"{_checks} plugin marketplace stats checks passed.");
    }

    private static void Run()
    {
        var alpha = Entry("alpha", Version("Alpha", "1.0.0", OldTag, OldAsset, description: "Hotkey automation deck", tags: ["macro", "workflow"], featureName: "Step Runner"),
            Version("Alpha", "1.1.0", CurrentTag, CurrentAsset, minimumHostVersion: "99.0.0"));
        alpha.Versions.Add(alpha.Versions[0]); // Try to count the same attachment twice.
        var beta = Entry("beta", Version("Beta", "1.0.0", NewTag, NewAsset, description: "Window snap controller", tags: ["tiling"], featureName: "Grid Snap"));
        var gamma = Entry("gamma", Version("Gamma", "1.0.0", NewTag, MissingAsset, description: "Quick sound pad", tags: ["audio"], featureName: "Audio Cue"));
        var catalog = new OfficialPluginCatalog { Modules = [alpha, beta, gamma] };

        Check(OfficialPluginReleaseStats.LoadCached() == null && !Directory.Exists(PluginPaths.Root),
            "opening without a stats cache creates no directories");

        var pageOne = new List<object>
        {
            Release(NewTag, Now.AddDays(-1), [(NewAsset, 0), ("README.png", 50000)]),
            Release(CurrentTag, Now.AddDays(-2), [(CurrentAsset, 80)]),
            Release(OldTag, Now, [(OldAsset, 9999)], foreign: true),
            Release(OldTag, Now, [(OldAsset, 9999)], draft: true)
        };
        while (pageOne.Count < 50) pageOne.Add(new { });
        var handler = new QueueHandler(
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(pageOne)) },
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(
                new[] { Release(OldTag, Now.AddDays(-82), [(OldAsset, 45)]) })) });
        using var client = new HttpClient(handler);
        var snapshot = OfficialPluginReleaseStats.FetchAsync(catalog, client, default).GetAwaiter().GetResult();
        Check(handler.Requests.Count == 2 && handler.Requests[1].Query.Contains("page=2", StringComparison.Ordinal) &&
            handler.Requests.All(uri => uri.Scheme == Uri.UriSchemeHttps && uri.Host == "api.github.com"),
            "history paginates through the public GitHub API");
        Check(snapshot.Assets.Length == 3 && snapshot.Assets.All(item => item.DownloadCount is <= 80),
            "only matching official nondraft package assets are retained");

        var assets = snapshot.Assets.ToDictionary(item => item.Key);
        var a = PluginReleaseCardInfo.Build(alpha, assets);
        var b = PluginReleaseCardInfo.Build(beta, assets);
        var c = PluginReleaseCardInfo.Build(gamma, assets);
        Check(a.Downloads == 125 && a.FirstPublished == Now.AddDays(-82) && a.LastPublished == Now.AddDays(-2),
            "sum across versions with distinct first and latest publication dates");
        Check(b.Downloads == 0 && c.Downloads == null && c.FirstPublished == null,
            "zero downloads and missing history are distinguishable");
        var stale = new Dictionary<OfficialPluginAssetKey, OfficialPluginAssetStats> { [snapshot.Assets[0].Key] = snapshot.Assets[0] };
        Check(PluginReleaseCardInfo.Build(alpha, stale).Downloads == null,
            "partial histories never masquerade as the total");
        Check(a.ReleasePageUrl?.EndsWith("/releases/tag/" + CurrentTag, StringComparison.Ordinal) == true &&
            PluginReleaseCardInfo.Build(alpha, new Dictionary<OfficialPluginAssetKey, OfficialPluginAssetStats>()).ReleasePageUrl == a.ReleasePageUrl,
            "latest module release link also works from the catalog alone");
        Check(OfficialPluginReleaseStats.BuildReleaseUrl("modules-2026.11.20.1?bad") == null,
            "untrusted release paths are rejected");

        var compatible = new OfficialPluginEnvironment("2.0.1-beta.5", "1.15", "net8.0-windows10.0.19041.0");
        var rows = new[]
        {
            new OfficialPluginListItem(alpha, "1.0.0", compatible) { ReleaseInfo = a },
            new OfficialPluginListItem(beta, null, compatible) { ReleaseInfo = b },
            new OfficialPluginListItem(gamma, null, compatible) { ReleaseInfo = c }
        };
        Check(rows[0].Module.Version == "1.0.0" && !rows[0].CanInstall &&
            rows[0].ReleaseInfo.ReleasePageUrl?.EndsWith(CurrentTag) == true,
            "newer GitHub release link does not change compatible install selection");
        string[] Sort(string sort) => PluginLibraryQuery.Sort(rows, sort, row => row.ReleaseInfo,
            row => row.DisplayName, row => row.Module.Id).Select(row => row.Module.Id).ToArray();
        Check(Sort("downloads").SequenceEqual(["alpha", "beta", "gamma"]), "downloads sorted high to low with unknown last");
        Check(Sort("published").SequenceEqual(["beta", "alpha", "gamma"]) &&
            Sort("updated").SequenceEqual(["beta", "alpha", "gamma"]), "date sorts place unknown last");
        Check(!PluginLibraryQuery.Matches(rows[0], "Tools", "recentPublished", Now) &&
            PluginLibraryQuery.Matches(rows[0], "TOOLS", "recentUpdated", Now) &&
            !PluginLibraryQuery.Matches(rows[0], "other", "recentUpdated", Now) &&
            PluginLibraryQuery.Matches(rows[1], "", "available", Now) &&
            PluginLibraryQuery.Matches(rows[0], "", "installed", Now) &&
            PluginLibraryQuery.Matches(rows[0], "", "downloads100", Now) &&
            !PluginLibraryQuery.Matches(rows[0], "", "downloads1000", Now) &&
            !PluginLibraryQuery.Matches(rows[2], "", "downloads100", Now),
            "category, recent versions, installation and popularity filters compose");

        Check(PluginLibraryQuery.MatchesSearch(rows[0], "  ") &&
            PluginLibraryQuery.MatchesSearch(rows[0], "ALPHA") &&
            PluginLibraryQuery.MatchesSearch(rows[0], "hotkey deck") &&
            PluginLibraryQuery.MatchesSearch(rows[0], "WORKFLOW") &&
            PluginLibraryQuery.MatchesSearch(rows[0], "step runner") &&
            !PluginLibraryQuery.MatchesSearch(rows[0], "alpha nonexistent") &&
            PluginLibraryQuery.Matches(rows[1], "Tools", "available", Now, "snap tiling") &&
            !PluginLibraryQuery.Matches(rows[1], "Tools", "available", Now, "workflow"),
            "search query matches multi-token name, description, tags and features and composes with filters");

        var installedItem = new PluginListItem
        {
            PluginId = "starpie.plugin.macro",
            DisplayName = "按键宏",
            CardDescription = "自动化组合键序列",
            SummaryText = "作者：StarPie | 3 个动作",
            IsOfficial = true
        };
        Check(PluginLibraryQuery.MatchesSearch(installedItem, "宏 组合键") &&
            PluginLibraryQuery.MatchesSearch(installedItem, "starpie.plugin.macro") &&
            !PluginLibraryQuery.MatchesSearch(installedItem, "宏 不存在"),
            "installed plugin search matches display name, id, description and summary");

        OfficialPluginReleaseStats.SaveCached(snapshot);
        var cached = OfficialPluginReleaseStats.LoadCached();
        Check(cached?.Assets.SequenceEqual(snapshot.Assets) == true && cached.UpdatedAt == snapshot.UpdatedAt,
            "cached statistics roundtrip");
        using var denied = new HttpClient(new QueueHandler(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        try
        {
            _ = OfficialPluginReleaseStats.FetchAsync(catalog, denied, default).GetAwaiter().GetResult();
            throw new InvalidOperationException("rate-limit failure was accepted");
        }
        catch (HttpRequestException)
        {
            Check(OfficialPluginReleaseStats.LoadCached()?.Assets.SequenceEqual(snapshot.Assets) == true,
                "rate limit leaves the previous cached statistics intact");
        }
        using var incomplete = new HttpClient(new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new[] { Release(CurrentTag, Now.AddDays(-2), [(CurrentAsset, 93)]) }))
        }));
        var updated = OfficialPluginReleaseStats.RefreshAsync(catalog, cached, incomplete, default).GetAwaiter().GetResult();
        var merged = updated.Assets.ToDictionary(item => item.Key);
        Check(updated.IsPartial && PluginReleaseCardInfo.Build(alpha, merged).Downloads == 138 &&
            PluginReleaseCardInfo.Build(beta, merged).Downloads == 0 &&
            OfficialPluginReleaseStats.LoadCached()?.IsPartial == true,
            "incomplete refresh combines fresh assets and previously cached versions with an explicit partial state");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var unusedHandler = new QueueHandler();
        using var unused = new HttpClient(unusedHandler);
        try
        {
            _ = OfficialPluginReleaseStats.RefreshAsync(catalog, updated, unused, cancelled.Token).GetAwaiter().GetResult();
            throw new InvalidOperationException("cancelled refresh was accepted");
        }
        catch (OperationCanceledException)
        {
            Check(unusedHandler.Requests.Count == 0 && OfficialPluginReleaseStats.LoadCached()?.UpdatedAt == updated.UpdatedAt,
                "closed settings session does not refresh or replace the cache");
        }
        var unknownCount = snapshot with
        {
            Assets = snapshot.Assets.Select(item => item.AssetName == OldAsset ? item with { DownloadCount = null } : item).ToArray()
        };
        using var noHistory = new HttpClient(new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]")
        }));
        var retained = OfficialPluginReleaseStats.RefreshAsync(catalog, unknownCount, noHistory, default).GetAwaiter().GetResult();
        var retainedAlpha = PluginReleaseCardInfo.Build(alpha, retained.Assets.ToDictionary(item => item.Key));
        Check(retainedAlpha.FirstPublished == a.FirstPublished && retainedAlpha.Downloads == null,
            "partial refresh retains previously known dates even if there was no download count");

        ConfigManager.DisableAutoStartSync = true;
        ConfigManager.CurrentConfig.AutoCheckUpdate = false;
        var application = new App();
        application.InitializeComponent();
        var window = new SettingsWindow();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(SettingsWindow).GetField("_officialPluginCatalog", flags)!.SetValue(window, catalog);
        typeof(SettingsWindow).GetField("_releaseStats", flags)!.SetValue(window, snapshot);
        foreach (LanguageCode language in new[] { LanguageCode.ZhCn, LanguageCode.ZhTw, LanguageCode.En, LanguageCode.Ja })
        {
            I18n.CurrentLanguage = language;
            typeof(SettingsWindow).GetMethod("ApplyPluginsPageLocalization", flags)!.Invoke(window, null);
            typeof(SettingsWindow).GetMethod("RenderOfficialPluginItems", flags)!.Invoke(window, null);
            var sort = (ComboBox)window.FindName("MarketSortCombo");
            var market = (ItemsControl)window.FindName("OfficialPluginItemsControl");
            sort.SelectedValue = "downloads";
            Check(((IEnumerable<OfficialPluginListItem>)market.ItemsSource).First().Module.Id == "alpha" &&
                sort.Items.Count == 4 && !a.DownloadsText.Contains("PluginStats", StringComparison.Ordinal) &&
                !a.ReleaseButtonText.Contains("PluginStats", StringComparison.Ordinal),
                "WPF market sort and release metadata localized " + language);
            var filter = (ComboBox)window.FindName("MarketStatusCombo");
            filter.SelectedValue = "downloads1000";
            Check(market.Items.Count == 0 &&
                ((TextBlock)window.FindName("MarketEmptyText")).Text == I18n.T("PluginStatsNoMatches"),
                "filtered empty state is distinct from empty catalog " + language);
            filter.SelectedValue = "";

            var searchBox = (TextBox)window.FindName("PluginLibrarySearchBox");
            var placeholder = (TextBlock)window.FindName("PluginLibrarySearchPlaceholder");
            var clearButton = (Button)window.FindName("PluginLibrarySearchClearButton");
            Check(placeholder.Text == I18n.T("PluginLibrarySearchPlaceholder") &&
                !placeholder.Text.Contains("PluginLibrarySearch", StringComparison.Ordinal),
                "search placeholder localized " + language);
            searchBox.Text = "grid snap";
            var filteredRows = ((IEnumerable<OfficialPluginListItem>)market.ItemsSource).ToList();
            Check(filteredRows.Count == 1 && filteredRows[0].Module.Id == "beta" &&
                placeholder.Visibility == Visibility.Collapsed && clearButton.Visibility == Visibility.Visible,
                "live search filters market cards and toggles clear button " + language);
            searchBox.Text = "does-not-exist";
            Check(market.Items.Count == 0 &&
                ((TextBlock)window.FindName("MarketEmptyText")).Text == I18n.T("PluginStatsNoMatches"),
                "search empty state localized " + language);
            clearButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(searchBox.Text == "" && market.Items.Count == 3 &&
                placeholder.Visibility == Visibility.Visible && clearButton.Visibility == Visibility.Collapsed,
                "clear search restores all cards and placeholder " + language);
        }
        Check(new WindowInteropHelper(window).Handle == IntPtr.Zero && !PluginHost.IsInitialized,
            "no shown settings window, live plugin calls, or input hooks");
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static OfficialPluginCatalogEntry Entry(string id, params OfficialPluginModule[] versions)
    {
        foreach (var version in versions) version.Id = id;
        return new() { Id = id, Versions = versions.ToList() };
    }

    private static OfficialPluginModule Version(string name, string version, string tag, string asset,
        string minimumHostVersion = "1.0.0", string description = "", string[]? tags = null, string featureName = "") => new()
    {
        Name = name, Version = version, ReleaseTag = tag, AssetName = asset, ApiVersion = "1.4",
        TargetFramework = "net8.0-windows", MinHostVersion = minimumHostVersion, Description = description,
        Tags = tags?.ToList() ?? [],
        Features = [new() { Category = "Tools", Name = featureName }]
    };

    private static object Release(string tag, DateTimeOffset published, (string Name, long DownloadCount)[] assets,
        bool foreign = false, bool draft = false) => new
    {
        tag_name = tag, published_at = published, draft,
        html_url = (foreign ? "https://github.com/Elsewhere/Plugins/releases/tag/" :
            "https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/") + tag,
        assets = assets.Select(asset => new { name = asset.Name, download_count = asset.DownloadCount })
    };

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        _checks++;
        Console.WriteLine("PASS " + name);
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
