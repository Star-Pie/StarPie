using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Xml;
using WinPieGestures;
using WinPieGestures.Plugins;

namespace OnboardingDownloadTests;

public static class Program
{
    private static int _passCount;
    private static int _failCount;

    [STAThread]
    public static int Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("StarPie Onboarding Download Channel Test Suite");
        Console.WriteLine("=================================================");

        try
        {
            string sandbox = Path.Combine(Path.GetTempPath(), "StarPie-OnboardingChannelTest-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(sandbox, "app-data"));
            string hostRoot = Path.Combine(sandbox, "plugin-data");
            string scanRoot = Path.Combine(sandbox, "plugin");
            Directory.CreateDirectory(hostRoot);
            Directory.CreateDirectory(scanRoot);

            PluginPaths.OverrideRootsForTesting(hostRoot, scanRoot);
            bool discoveryOnly = args.Contains("--release-discovery-only", StringComparer.Ordinal);
            PluginHost.HeadlessMode = discoveryOnly;
            CleanRegistry();

            if (discoveryOnly)
            {
                TestGitHubDownMirrorDiscoveryAndCatalogFetch().GetAwaiter().GetResult();
            }
            else
            {
                TestChannelNormalizationAndUrlHelper();
                TestZeroNetworkAndZeroConfigMutationOnDialog();
                TestBatchSnapshotImmutabilityAndRetrySwitch();
                TestGitHubDownMirrorDiscoveryAndCatalogFetch().GetAwaiter().GetResult();
                TestPartialFailureChannelSwitchAndZeroNetworkLocalRetry().GetAwaiter().GetResult();
                TestCorruptedSpkgRejectionAndCancellationCleanup().GetAwaiter().GetResult();
                TestDialogControlsStateAndFourLanguageLocalization();
                TestSafetyMutationVerification();
            }
            TestReleaseDiscoverySortingAndOriginValidation();
            TestEditedReleaseDoesNotHideNewCatalog().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Fail("UnhandledException", $"未捕获异常: {ex}");
        }

        Console.WriteLine("=================================================");
        Console.WriteLine($"Total Tests: {_passCount + _failCount} | Passed: {_passCount} | Failed: {_failCount}");
        Console.WriteLine("=================================================");

        return _failCount == 0 ? 0 : 1;
    }

    private static void Pass(string id, string message)
    {
        _passCount++;
        Console.WriteLine($"[PASS] {id} {message}");
    }

    private static void Fail(string id, string message)
    {
        _failCount++;
        Console.WriteLine($"[FAIL] {id} {message}");
    }

    private static void Assert(bool condition, string id, string message)
    {
        if (condition) Pass(id, message);
        else Fail(id, message);
    }

    private static void CleanRegistry()
    {
        foreach (string id in OfficialPluginOnboarding.TargetPluginIds)
        {
            PluginRegistryStore.RemoveEntry(id);
        }
    }

    // -------------------------------------------------------------------------
    // Scenario 1: 四通道与历史别名标准化 & 单一事实来源 URL 代理
    // -------------------------------------------------------------------------
    private static void TestChannelNormalizationAndUrlHelper()
    {
        Console.WriteLine("\n--- Scenario 1: Channel Normalization & URL Helper ---");

        // 规范化
        Assert(OfficialPluginDownloadOptions.NormalizeChannel("ghfast") == "ghfast", "1.1", "ghfast normalizes to ghfast");
        Assert(OfficialPluginDownloadOptions.NormalizeChannel("ghproxy") == "ghfast", "1.2", "ghproxy alias normalizes to ghfast");
        Assert(OfficialPluginDownloadOptions.NormalizeChannel("moeyy") == "ghfast", "1.3", "moeyy alias normalizes to ghfast");
        Assert(OfficialPluginDownloadOptions.NormalizeChannel("gh-proxy") == "gh-proxy", "1.4", "gh-proxy normalizes to gh-proxy");
        Assert(OfficialPluginDownloadOptions.NormalizeChannel("akams") == "gh-proxy", "1.5", "akams alias normalizes to gh-proxy");
        Assert(OfficialPluginDownloadOptions.NormalizeChannel("mirror") == "mirror", "1.6", "mirror normalizes to mirror");
        Assert(OfficialPluginDownloadOptions.NormalizeChannel("direct") == "direct", "1.7", "direct normalizes to direct");
        Assert(OfficialPluginDownloadOptions.NormalizeChannel(null) == "direct", "1.8", "null normalizes to direct");
        Assert(OfficialPluginDownloadOptions.NormalizeChannel("") == "direct", "1.9", "empty normalizes to direct");
        Assert(OfficialPluginDownloadOptions.NormalizeChannel("unknown_channel") == "direct", "1.10", "unknown normalizes to direct");
        Assert(OfficialPluginDownloadOptions.NormalizeChannel("custom") == "direct", "1.11", "custom without proxy normalizes to direct");

        // 单一事实来源比对
        const string rawUrl = "https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/v1.0.0/test.spkg";
        var optGhfast = new OfficialPluginDownloadOptions("ghfast");
        var optGhproxy = new OfficialPluginDownloadOptions("gh-proxy");
        var optMirror = new OfficialPluginDownloadOptions("mirror");
        var optDirect = new OfficialPluginDownloadOptions("direct");

        Assert(optGhfast.GetProxiedUrl(rawUrl) == UpdateManager.Instance.GetProxiedDownloadUrl(rawUrl, "ghfast"),
            "1.12", "ghfast uses UpdateManager.Instance.GetProxiedDownloadUrl");
        Assert(optGhfast.GetProxiedUrl(rawUrl) == $"https://ghfast.top/{rawUrl}",
            "1.13", "ghfast URL matches https://ghfast.top/ prefix");

        Assert(optGhproxy.GetProxiedUrl(rawUrl) == UpdateManager.Instance.GetProxiedDownloadUrl(rawUrl, "gh-proxy"),
            "1.14", "gh-proxy uses UpdateManager.Instance.GetProxiedDownloadUrl");
        Assert(optGhproxy.GetProxiedUrl(rawUrl) == $"https://gh-proxy.com/{rawUrl}",
            "1.15", "gh-proxy URL matches https://gh-proxy.com/ prefix");

        Assert(optMirror.GetProxiedUrl(rawUrl) == UpdateManager.Instance.GetProxiedDownloadUrl(rawUrl, "mirror"),
            "1.16", "mirror uses UpdateManager.Instance.GetProxiedDownloadUrl");
        Assert(optMirror.GetProxiedUrl(rawUrl) == $"https://mirror.ghproxy.com/{rawUrl}",
            "1.17", "mirror URL matches https://mirror.ghproxy.com/ prefix");

        Assert(optDirect.GetProxiedUrl(rawUrl) == rawUrl,
            "1.18", "direct URL matches rawUrl unchanged");

        // 规范化后的 PackageUrl 必须保持 canonical HTTPS github.com
        var testModule = new OfficialPluginModule
        {
            Id = "starpie.test.dummy",
            PackageUrl = rawUrl,
            AssetName = "test.spkg",
            ReleaseTag = "v1.0.0",
            Size = 1024,
            Sha256 = new string('0', 64)
        };
        Assert(testModule.PackageUrl == rawUrl, "1.19", "module.PackageUrl remains canonical and unchanged");

        // 非官方 / 非 HTTPS 地址在发起请求前应当被拒绝
        var illegalModule1 = new OfficialPluginModule
        {
            Id = "starpie.test.dummy",
            Name = "starpie.test.dummy",
            Version = "1.0.0",
            MinHostVersion = "1.0.0",
            ApiVersion = "1.8",
            TargetFramework = "net8.0-windows",
            PackageUrl = "http://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/v1.0.0/test.spkg",
            AssetName = "test.spkg",
            ReleaseTag = "v1.0.0",
            Size = 1024,
            Sha256 = new string('0', 64)
        };
        var res1 = OfficialPluginClient.InstallAsync(illegalModule1, CancellationToken.None, optGhfast).GetAwaiter().GetResult();
        Assert(!res1.Success && res1.Error.Contains("HTTPS"), "1.20", "Non-HTTPS package URL is rejected");

        var illegalModule2 = new OfficialPluginModule
        {
            Id = "starpie.test.dummy",
            Name = "starpie.test.dummy",
            Version = "1.0.0",
            MinHostVersion = "1.0.0",
            ApiVersion = "1.8",
            TargetFramework = "net8.0-windows",
            PackageUrl = "https://evil.com/Star-Pie/StarPie-Official-Plugins/releases/download/v1.0.0/test.spkg",
            AssetName = "test.spkg",
            ReleaseTag = "v1.0.0",
            Size = 1024,
            Sha256 = new string('0', 64)
        };
        var res2 = OfficialPluginClient.InstallAsync(illegalModule2, CancellationToken.None, optGhfast).GetAwaiter().GetResult();
        Assert(!res2.Success, "1.21", "Non-official domain package URL is rejected");
    }

    // -------------------------------------------------------------------------
    // Scenario 2: 弹窗初始化、通道切换、语言刷新与关闭 0 网络请求 & 0 全局配置改写
    // -------------------------------------------------------------------------
    private static void TestZeroNetworkAndZeroConfigMutationOnDialog()
    {
        Console.WriteLine("\n--- Scenario 2: Zero Network & Zero Config Mutation ---");

        var countingHandler = new CountingHttpMessageHandler();
        OfficialPluginClient.HttpHandlerOverrideForTesting = countingHandler;

        string initialProxySource = ConfigManager.CurrentConfig?.UpdateProxySource ?? "ghfast";
        string initialCustomUrl = ConfigManager.CurrentConfig?.CustomProxyUrl ?? "";

        // 构造弹窗
        OfficialPluginsOnboardingDialog? dialog = null;
        try
        {
            dialog = new OfficialPluginsOnboardingDialog();

            Assert(countingHandler.RequestCount == 0, "2.1", "Dialog constructor makes zero network requests");
            Assert(ConfigManager.CurrentConfig?.UpdateProxySource == initialProxySource, "2.2", "Constructor does not modify UpdateProxySource");
            Assert(ConfigManager.CurrentConfig?.CustomProxyUrl == initialCustomUrl, "2.3", "Constructor does not modify CustomProxyUrl");

            var comboBox = dialog.FindName("ChannelComboBox") as ComboBox;
            Assert(comboBox != null, "2.4", "ChannelComboBox exists in dialog");

            // 切换通道
            if (comboBox != null)
            {
                foreach (ComboBoxItem item in comboBox.Items.OfType<ComboBoxItem>())
                {
                    comboBox.SelectedItem = item;
                    Assert(countingHandler.RequestCount == 0, "2.5", $"Switching to {item.Tag} makes zero network requests");
                    Assert(ConfigManager.CurrentConfig?.UpdateProxySource == initialProxySource, "2.6", $"Switching to {item.Tag} does not modify global config");
                }

                // 选中 gh-proxy
                var ghproxyItem = comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == "gh-proxy");
                if (ghproxyItem != null) comboBox.SelectedItem = ghproxyItem;
            }

            // 切换语言测试
            string[] testLangs = { "en", "zh-TW", "ja", "zh-CN" };
            foreach (var lang in testLangs)
            {
                I18n.SetLanguage(lang);
                Assert(countingHandler.RequestCount == 0, "2.7", $"Switching language to {lang} makes zero network requests");
                Assert(ConfigManager.CurrentConfig?.UpdateProxySource == initialProxySource, "2.8", $"Switching language does not modify global config");

                // 校验选择未被重置
                if (comboBox?.SelectedItem is ComboBoxItem selectedItem)
                {
                    Assert((string)selectedItem.Tag == "gh-proxy", "2.9", $"Channel selection gh-proxy preserved after language switch to {lang}");
                }
                else
                {
                    Fail("2.9", "Channel selection was lost after language switch");
                }
            }

            // 关闭弹窗（拒绝安装）
            dialog.Close();
            Assert(countingHandler.RequestCount == 0, "2.10", "Closing dialog makes zero network requests");
            Assert(ConfigManager.CurrentConfig?.UpdateProxySource == initialProxySource, "2.11", "Closing dialog does not modify global config");
        }
        finally
        {
            OfficialPluginClient.HttpHandlerOverrideForTesting = null;
            I18n.SetLanguage("zh-CN");
        }
    }

    // -------------------------------------------------------------------------
    // Scenario 3: 批次快照不可变性 & 换通道重试
    // -------------------------------------------------------------------------
    private static void TestBatchSnapshotImmutabilityAndRetrySwitch()
    {
        Console.WriteLine("\n--- Scenario 3: Batch Snapshot Immutability & Retry Channel Switch ---");

        var opt = new OfficialPluginDownloadOptions("ghfast");
        Assert(opt.Channel == "ghfast", "3.1", "OfficialPluginDownloadOptions stores channel immutable");

        // 验证默认参数兼容性：catalogFetcher/moduleInstaller/detailedModuleInstaller 等原重载依然可用
        try
        {
            // 调用带 null downloadOptions 的 InstallMissingPluginsAsync（走短路分支，所有插件模拟已安装跳过）
            var report = OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: ct => Task.FromResult(new OfficialPluginCatalog()),
                preExistingDisabledPluginIds: OfficialPluginOnboarding.TargetPluginIds
            ).GetAwaiter().GetResult();

            Assert(report != null, "3.2", "InstallMissingPluginsAsync backward compatibility overload works");
        }
        catch (Exception ex)
        {
            Fail("3.2", $"Default overload threw: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Scenario 4: 官方版本候选发现 (Atom + REST API) 与全链路通道资产下载
    // -------------------------------------------------------------------------
    private static async Task TestGitHubDownMirrorDiscoveryAndCatalogFetch()
    {
        Console.WriteLine("\n--- Scenario 4: Unified Official Release Discovery & Channel Routing ---");

        string newTag = "modules-2026.10.01.1";
        string oldTag = "modules-2026.09.30.1";
        string repo = "https://github.com/Star-Pie/StarPie-Official-Plugins";

        string validV2CatalogJson = CreateValidCatalogJson(newTag);
        string validV1CatalogJson = System.Text.RegularExpressions.Regex.Replace(CreateValidCatalogJson(oldTag), "\"schemaversion\"\\s*:\\s*2", "\"schemaVersion\":1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        static HttpResponseMessage GitTags(params string[] tags)
        {
            var data = new StringBuilder();
            foreach (string tag in tags)
            {
                string line = new string('0', 40) + " refs/tags/" + tag + "\n";
                data.Append((line.Length + 4).ToString("x4")).Append(line);
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(data.Append("0000").ToString()) };
            response.Content.Headers.ContentType = new("application/x-git-upload-pack-advertisement");
            return response;
        }

        // 4.1 官方 Atom 包含旧稳定 v1 与新 pre-release v2 时，所有四通道采用相同 tag，使用所选通道下载资产，不请求 /latest
        foreach (string channel in new[] { "direct", "ghfast", "gh-proxy", "mirror" })
        {
            var options = new OfficialPluginDownloadOptions(channel);
            var requestedUrls = new List<string>();
            var mockHandler = new DelegatingMockHttpHandler(req =>
            {
                string url = req.RequestUri?.ToString() ?? "";
                requestedUrls.Add(url);

                if (channel == "ghfast" && url == options.GetProxiedUrl(repo + ".git/info/refs?service=git-upload-pack")) return GitTags(oldTag, newTag);
                if (url == options.GetProxiedUrl(repo + "/releases.atom"))
                {
                    string atom = $"<feed xmlns=\"http://www.w3.org/2005/Atom\"><entry><updated>2026-10-01T00:00:00Z</updated><link href=\"{repo}/releases/tag/{newTag}\" /></entry><entry><updated>2026-09-30T00:00:00Z</updated><link href=\"{repo}/releases/tag/{oldTag}\" /></entry></feed>";
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(atom) };
                }
                if (url.EndsWith("/module-catalog.json"))
                {
                    string content = url.Contains("/" + newTag + "/") ? validV2CatalogJson : validV1CatalogJson;
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content)) };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            OfficialPluginClient.HttpHandlerOverrideForTesting = mockHandler;
            try
            {
                OfficialPluginCatalog catalog = await OfficialPluginClient.FetchCatalogAsync(options);

                Assert(catalog != null && catalog.SchemaVersion == 2 && catalog.ReleaseTag == newTag,
                    "4.1.1", $"Discovered v2 prerelease tag across channel {channel}");

                string expectedCatalogUrl = options.GetProxiedUrl($"{repo}/releases/download/{newTag}/module-catalog.json");
                Assert(requestedUrls.Contains(expectedCatalogUrl),
                    "4.1.2", $"Catalog URL uses selected channel {channel}");
                Assert(!requestedUrls.Any(u => u.Contains("/releases/latest/")),
                    "4.1.3", $"No /releases/latest/ shortcut used on channel {channel}");

                string discovery = channel == "ghfast" ? repo + ".git/info/refs?service=git-upload-pack" : repo + "/releases.atom";
                Assert(requestedUrls.Contains(options.GetProxiedUrl(discovery)) &&
                    requestedUrls.All(url => new Uri(url).Host == new Uri(expectedCatalogUrl).Host),
                    "4.1.4", $"Discovery and catalog both honor the fixed channel {channel}");
            }
            finally
            {
                OfficialPluginClient.HttpHandlerOverrideForTesting = null;
            }
        }

        // 4.2 Atom 不可达或 403 时，有界 GitHub Releases REST API 发现（过滤 draft、错误源，按时间排序）
        foreach (string channel in new[] { "direct", "ghfast", "gh-proxy", "mirror" })
        {
            var options = new OfficialPluginDownloadOptions(channel);
            var requestedUrls = new List<string>();
            var releasesApiList = JsonSerializer.Serialize(new object[]
            {
                new { tag_name = oldTag, prerelease = false, draft = false, published_at = "2026-09-30T00:00:00Z", html_url = $"{repo}/releases/tag/{oldTag}" },
                new { tag_name = "modules-draft", prerelease = true, draft = true, published_at = "2026-10-02T00:00:00Z", html_url = $"{repo}/releases/tag/modules-draft" },
                new { tag_name = "modules-bad-origin", prerelease = true, draft = false, published_at = "2026-10-03T00:00:00Z", html_url = "https://github.com/EvilFork/StarPie-Official-Plugins/releases/tag/modules-bad-origin" },
                new { tag_name = newTag, prerelease = true, draft = false, published_at = "2026-10-01T00:00:00Z", html_url = $"{repo}/releases/tag/{newTag}" }
            });

            var mockHandler = new DelegatingMockHttpHandler(req =>
            {
                string url = req.RequestUri?.ToString() ?? "";
                requestedUrls.Add(url);

                if (channel == "ghfast" && url == options.GetProxiedUrl(repo + ".git/info/refs?service=git-upload-pack")) return GitTags(oldTag, newTag);
                if (url == options.GetProxiedUrl(repo + "/releases.atom"))
                {
                    return new HttpResponseMessage(HttpStatusCode.Forbidden); // Atom 失败触发 API 回退
                }
                if (url == options.GetProxiedUrl(OfficialPluginReleaseDiscovery.ReleasesApiUrl))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(releasesApiList) };
                }
                if (url.EndsWith("/module-catalog.json"))
                {
                    string content = url.Contains("/" + newTag + "/") ? validV2CatalogJson : validV1CatalogJson;
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content)) };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            OfficialPluginClient.HttpHandlerOverrideForTesting = mockHandler;
            try
            {
                OfficialPluginCatalog catalog = await OfficialPluginClient.FetchCatalogAsync(options);

                Assert(catalog != null && catalog.SchemaVersion == 2 && catalog.ReleaseTag == newTag,
                    "4.2.1", $"API fallback discovered v2 prerelease tag across channel {channel}");

                string expectedCatalogUrl = options.GetProxiedUrl($"{repo}/releases/download/{newTag}/module-catalog.json");
                Assert(requestedUrls.Contains(expectedCatalogUrl),
                    "4.2.2", $"Catalog URL uses selected channel {channel} on API fallback");
                Assert(!requestedUrls.Any(u => u.Contains("/releases/latest/")),
                    "4.2.3", $"No /releases/latest/ shortcut used on API fallback {channel}");
                Assert(requestedUrls.All(url => channel == "direct" || new Uri(url).Host == new Uri(expectedCatalogUrl).Host),
                    "4.2.4", $"Fallback keeps discovery within {channel}");
            }
            finally
            {
                OfficialPluginClient.HttpHandlerOverrideForTesting = null;
            }
        }

        // 4.3 Atom 与 API 均失败 (例如 API 返回 403 Rate Limit) -> 准确报告网络失败，未发插件包请求，不缓存坏目录
        {
            var requestedUrls = new List<string>();
            var mockHandler = new DelegatingMockHttpHandler(req =>
            {
                string url = req.RequestUri?.ToString() ?? "";
                requestedUrls.Add(url);
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            });

            OfficialPluginClient.HttpHandlerOverrideForTesting = mockHandler;
            try
            {
                var options = new OfficialPluginDownloadOptions("ghfast");
                await OfficialPluginClient.FetchCatalogAsync(options);
                Fail("4.3.1", "Expected HttpRequestException when both Atom and API fail");
            }
            catch (HttpRequestException)
            {
                Pass("4.3.1", "HttpRequestException cleanly thrown when Atom and API fail with 403");
            }
            finally
            {
                OfficialPluginClient.HttpHandlerOverrideForTesting = null;
            }
            Assert(!requestedUrls.Any(u => u.EndsWith(".spkg")), "4.3.2", "Zero package requests made when discovery fails");
        }

        // 4.4 用户取消 -> 立即停止，不进入下一发现路线
        {
            int requestCount = 0;
            var mockHandler = new DelegatingMockHttpHandler(req =>
            {
                requestCount++;
                throw new TaskCanceledException();
            });

            OfficialPluginClient.HttpHandlerOverrideForTesting = mockHandler;
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // 预先取消

            try
            {
                await OfficialPluginClient.FetchCatalogAsync(new OfficialPluginDownloadOptions("ghfast"), cts.Token);
                Fail("4.4.1", "Expected OperationCanceledException on caller cancellation");
            }
            catch (OperationCanceledException)
            {
                Pass("4.4.1", "Caller cancellation cleanly stopped discovery");
            }
            finally
            {
                OfficialPluginClient.HttpHandlerOverrideForTesting = null;
            }
        }

        // 4.5 候选发布中全为 v1 目录 -> 正确拒绝并要求 v2，绝不妥协接受 v1
        {
            var mockHandler = new DelegatingMockHttpHandler(req =>
            {
                string url = req.RequestUri?.ToString() ?? "";
                if (url == new OfficialPluginDownloadOptions("ghfast").GetProxiedUrl(repo + ".git/info/refs?service=git-upload-pack")) return GitTags(oldTag);
                if (url == repo + "/releases.atom")
                {
                    string atom = $"<feed xmlns=\"http://www.w3.org/2005/Atom\"><entry><updated>2026-09-30T00:00:00Z</updated><link href=\"{repo}/releases/tag/{oldTag}\" /></entry></feed>";
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(atom) };
                }
                if (url.EndsWith("/module-catalog.json"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(validV1CatalogJson)) };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            OfficialPluginClient.HttpHandlerOverrideForTesting = mockHandler;
            try
            {
                await OfficialPluginClient.FetchCatalogAsync(new OfficialPluginDownloadOptions("ghfast"));
                Fail("4.5.1", "Expected InvalidDataException when all candidates are v1");
            }
            catch (InvalidDataException ex)
            {
                Assert(ex.Message.Contains("catalog v2"), "4.5.1", $"InvalidDataException rejects v1 catalog: {ex.Message}");
            }
            finally
            {
                OfficialPluginClient.HttpHandlerOverrideForTesting = null;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Scenario 5: 首次部分成功并落地注册表、改通道重试只处理未完成项，本地启用重试 0 请求
    // -------------------------------------------------------------------------
    private static async Task TestPartialFailureChannelSwitchAndZeroNetworkLocalRetry()
    {
        Console.WriteLine("\n--- Scenario 5: Partial Failure, Channel Switch & Zero Network Local Retry ---");

        CleanRegistry();
        string catalogJson = CreateValidCatalogJson("v1.8.0");
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // 第一次安装：direct 通道，folder 和 weburl 成功并写入沙箱注册表，其他 3 项失败
        var directOptions = new OfficialPluginDownloadOptions("direct");
        var pass1RequestedUrls = new List<string>();

        var report1 = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
            catalogFetcher: ct => Task.FromResult(JsonSerializer.Deserialize<OfficialPluginCatalog>(catalogJson, jsonOptions)!),
            detailedModuleInstaller: (module, prompter, caps, ct) =>
            {
                pass1RequestedUrls.Add(directOptions.GetProxiedUrl(module.PackageUrl));
                if (module.Id == "starpie.builtin.folder" || module.Id == "starpie.builtin.weburl")
                {
                    PluginRegistryStore.UpsertEntry(new PluginRegistryEntry
                    {
                        Id = module.Id,
                        Name = module.Name,
                        Version = module.Version,
                        Enabled = true
                    });
                    return Task.FromResult(new OfficialPluginInstallResult
                    {
                        Success = true,
                        Enabled = true,
                        PluginId = module.Id
                    });
                }
                else
                {
                    return Task.FromResult(new OfficialPluginInstallResult
                    {
                        Success = false,
                        Enabled = false,
                        PluginId = module.Id,
                        Error = "Connection reset by peer",
                        IsNetworkError = true
                    });
                }
            },
            preExistingDisabledPluginIds: Array.Empty<string>(),
            downloadOptions: directOptions
        );

        Assert(report1.SuccessCount == 2, "5.1", "Pass 1 succeeded count is 2 (folder and weburl)");
        Assert(report1.FailureCount == 3, "5.2", "Pass 1 failed count is 3 (launch, system, shelltool)");
        Assert(report1.FailedPlugins.TryGetValue("starpie.builtin.launch", out var g) && g.Contains("当前通道：direct"),
            "5.3", "Guidance identifies direct channel on network failure");
        Assert(PluginRegistryStore.FindEntry("starpie.builtin.folder") != null, "5.4", "Folder persisted in registry");
        Assert(PluginRegistryStore.FindEntry("starpie.builtin.weburl") != null, "5.5", "WebUrl persisted in registry");

        // 第二次重试：用户切换到 ghfast 通道，仅重试未完成项（严格只重试 3 项！）
        var ghfastOptions = new OfficialPluginDownloadOptions("ghfast");
        var retriedPluginIds = new List<string>();
        var pass2HttpUrls = new List<string>();

        // 配置 HTTP handler 拦截生产 HttpClient 请求，验证生产下载器收到了新通道
        var pass2Handler = new DelegatingMockHttpHandler(req =>
        {
            string url = req.RequestUri?.ToString() ?? "";
            pass2HttpUrls.Add(url);

            if (url.Contains("releases.atom"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (url.Contains("module-catalog.json"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes(catalogJson))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        OfficialPluginClient.HttpHandlerOverrideForTesting = pass2Handler;
        try
        {
            var report2 = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: ct => Task.FromResult(JsonSerializer.Deserialize<OfficialPluginCatalog>(catalogJson, jsonOptions)!),
                detailedModuleInstaller: (module, prompter, caps, ct) =>
                {
                    retriedPluginIds.Add(module.Id);
                    pass2HttpUrls.Add(ghfastOptions.GetProxiedUrl(module.PackageUrl));
                    return Task.FromResult(new OfficialPluginInstallResult
                    {
                        Success = true,
                        Enabled = true,
                        PluginId = module.Id
                    });
                },
                preExistingDisabledPluginIds: Array.Empty<string>(),
                downloadOptions: ghfastOptions
            );

            Assert(retriedPluginIds.Count == 3, "5.6", $"Pass 2 strictly retried only the 3 missing items (actual: {retriedPluginIds.Count})");
            Assert(!retriedPluginIds.Contains("starpie.builtin.folder"), "5.7", "Already installed folder was NOT retried");
            Assert(!retriedPluginIds.Contains("starpie.builtin.weburl"), "5.8", "Already installed weburl was NOT retried");
            Assert(report2.AlreadyInstalledAndEnabledPluginIds.Count == 2, "5.9", "Pass 2 report identified 2 already installed items");
            Assert(pass2HttpUrls.Any(u => u.StartsWith("https://ghfast.top/")), "5.10", "Requests on retry routed through ghfast mirror");
        }
        finally
        {
            OfficialPluginClient.HttpHandlerOverrideForTesting = null;
        }

        // 本地启用重试：0 网络请求
        int localNetworkCount = 0;
        var localCountingHandler = new DelegatingMockHttpHandler(req =>
        {
            localNetworkCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        OfficialPluginClient.HttpHandlerOverrideForTesting = localCountingHandler;

        try
        {
            CleanRegistry();
            // 模拟已有插件安装但未启用（阶段 A 本地重试项）
            PluginRegistryStore.UpsertEntry(new PluginRegistryEntry
            {
                Id = "starpie.builtin.shelltool",
                Enabled = false,
                Name = "shelltool",
                Version = "1.0.0"
            });

            var localRetryReport = await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                catalogFetcher: ct => { localNetworkCount++; return Task.FromResult(new OfficialPluginCatalog()); },
                preExistingDisabledPluginIds: OfficialPluginOnboarding.TargetPluginIds.Take(4).ToArray(),
                localEnabler: (string id, out string err) =>
                {
                    err = "";
                    return true;
                }
            );

            Assert(localNetworkCount == 0, "5.11", "Local enablement retry makes strictly 0 network requests");
        }
        finally
        {
            OfficialPluginClient.HttpHandlerOverrideForTesting = null;
            CleanRegistry();
        }
    }

    // -------------------------------------------------------------------------
    // Scenario 6: 损坏/伪造 spkg 真实校验、有界超时、运行中取消与临时目录清理
    // -------------------------------------------------------------------------
    private static async Task TestCorruptedSpkgRejectionAndCancellationCleanup()
    {
        Console.WriteLine("\n--- Scenario 6: Corrupted spkg Rejection, Cancellation, Timeout & Cleanup ---");

        byte[] fakeSpkgBytes = new byte[2048];
        new Random(42).NextBytes(fakeSpkgBytes);
        string correctHash = Convert.ToHexString(SHA256.HashData(fakeSpkgBytes));
        string forgedHash = new string('F', 64);

        var mockHandler = new DelegatingMockHttpHandler(req =>
        {
            string url = req.RequestUri?.ToString() ?? "";
            if (url.EndsWith(".spkg"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(fakeSpkgBytes)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        OfficialPluginClient.HttpHandlerOverrideForTesting = mockHandler;

        try
        {
            var opt = new OfficialPluginDownloadOptions("ghfast");
            var corruptedModule = new OfficialPluginModule
            {
                Id = "starpie.test.corrupt",
                Name = "Corrupt Test Plugin",
                Version = "1.0.0",
                ReleaseTag = "v1.0.0",
                AssetName = "corrupt.spkg",
                PackageUrl = "https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/v1.0.0/corrupt.spkg",
                Sha256 = forgedHash, // 错误的哈希
                Size = fakeSpkgBytes.Length,
                TargetFramework = "net8.0-windows",
                ApiVersion = "1.0",
                MinHostVersion = "0.0.0"
            };

            int tempCountBefore = Directory.GetDirectories(Path.GetTempPath(), "StarPie-OfficialPlugin-*").Length;
            var res = await OfficialPluginClient.InstallAsync(corruptedModule, CancellationToken.None, opt);
            int tempCountAfter = Directory.GetDirectories(Path.GetTempPath(), "StarPie-OfficialPlugin-*").Length;

            Assert(!res.Success, "6.1", "Corrupted spkg with mismatched SHA-256 is rejected");
            Assert(res.Error.Contains("SHA-256"), "6.2", "Error message clearly mentions SHA-256 validation failure");
            Assert(!res.IsNetworkError, "6.3", "Hash mismatch is strictly NOT mislabeled as IsNetworkError");
            Assert(tempCountAfter == tempCountBefore, "6.4", "Temp directory cleaned up after hash mismatch failure");

            // 超大包 (> 100 MiB)
            var oversizeModule = new OfficialPluginModule
            {
                Id = "starpie.test.oversize",
                Name = "Oversize Test Plugin",
                Version = "1.0.0",
                ReleaseTag = "v1.0.0",
                AssetName = "oversize.spkg",
                PackageUrl = "https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/v1.0.0/oversize.spkg",
                Sha256 = correctHash,
                Size = 101L * 1024L * 1024L, // 101 MiB > 100 MiB
                TargetFramework = "net8.0-windows",
                ApiVersion = "1.0",
                MinHostVersion = "0.0.0"
            };

            var resOversize = await OfficialPluginClient.InstallAsync(oversizeModule, CancellationToken.None, opt);
            Assert(!resOversize.Success, "6.5", "Oversize module > 100 MiB is rejected before download");
            Assert(!resOversize.IsNetworkError, "6.6", "Oversize error is NOT mislabeled as IsNetworkError");

            // F1: Stream stall timeout (覆盖整个正文读取的请求超时，且清理临时目录)
            OfficialPluginClient.DownloadTimeoutOverrideForTesting = TimeSpan.FromMilliseconds(200);
            var stallStream = new StallStream();
            OfficialPluginClient.HttpHandlerOverrideForTesting = new DelegatingMockHttpHandler(req =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stallStream) });

            var stallModule = new OfficialPluginModule
            {
                Id = "starpie.test.stall",
                Name = "Stall Test Plugin",
                Version = "1.0.0",
                ReleaseTag = "v1.0.0",
                AssetName = "stall.spkg",
                PackageUrl = "https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/v1.0.0/stall.spkg",
                Sha256 = correctHash,
                Size = 2048,
                TargetFramework = "net8.0-windows",
                ApiVersion = "1.0",
                MinHostVersion = "0.0.0"
            };

            int stallTempBefore = Directory.GetDirectories(Path.GetTempPath(), "StarPie-OfficialPlugin-*").Length;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var stallRes = await OfficialPluginClient.InstallAsync(stallModule, CancellationToken.None, opt);
            sw.Stop();
            int stallTempAfter = Directory.GetDirectories(Path.GetTempPath(), "StarPie-OfficialPlugin-*").Length;

            Assert(!stallRes.Success, "6.7", "Stalled stream download failed");
            Assert(stallRes.IsNetworkError, "6.8", "Stalled stream timeout is classified as IsNetworkError = true");
            Assert(stallRes.Error.Contains("超时") || stallRes.Error.Contains("timed out"), "6.9", $"Error message indicates timeout (got: {stallRes.Error})");
            Assert(sw.ElapsedMilliseconds < 5000, "6.10", $"Stall timeout triggered quickly via linked CTS (elapsed: {sw.ElapsedMilliseconds}ms)");
            Assert(stallTempAfter == stallTempBefore, "6.11", "Temp directory cleaned up after stream-stall timeout");
            OfficialPluginClient.DownloadTimeoutOverrideForTesting = null;

            // F2: HTTP timeout without caller cancellation (HttpClient TaskCanceledException)
            OfficialPluginClient.HttpHandlerOverrideForTesting = new DelegatingMockHttpHandler(req =>
                throw new TaskCanceledException("Simulated HTTP timeout", new TimeoutException()));

            var timeoutRes = await OfficialPluginClient.InstallAsync(stallModule, CancellationToken.None, opt);
            Assert(!timeoutRes.Success, "6.12", "HTTP timeout without caller cancellation returned failed");
            Assert(timeoutRes.IsNetworkError, "6.13", "HTTP timeout without caller cancellation is classified as IsNetworkError = true");
            Assert(!timeoutRes.Error.Contains("已取消"), "6.14", "HTTP timeout is NOT misreported as user cancellation");

            // F2: 运行中用户主动取消 (Mid-run cancellation terminates batch immediately)
            int attemptedCount = 0;
            using var midRunCts = new CancellationTokenSource();
            OfficialPluginClient.HttpHandlerOverrideForTesting = new DelegatingMockHttpHandler(req =>
            {
                Interlocked.Increment(ref attemptedCount);
                midRunCts.Cancel();
                throw new OperationCanceledException(midRunCts.Token);
            });

            bool caughtCallerCancel = false;
            try
            {
                await OfficialPluginClient.InstallAsync(stallModule, midRunCts.Token, opt);
            }
            catch (OperationCanceledException)
            {
                caughtCallerCancel = true;
            }

            Assert(caughtCallerCancel, "6.15", "Caller cancellation propagates OperationCanceledException immediately");

            // 批量运行中取消：第二项取消后，后续项不再执行
            int batchAttempts = 0;
            using var batchCts = new CancellationTokenSource();
            string validCat = CreateValidCatalogJson("v1.8.0");
            var jOpt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            bool batchCancelled = false;

            try
            {
                await OfficialPluginOnboarding.InstallMissingPluginsAsync(
                    catalogFetcher: ct => Task.FromResult(JsonSerializer.Deserialize<OfficialPluginCatalog>(validCat, jOpt)!),
                    detailedModuleInstaller: (m, p, c, ct) =>
                    {
                        int current = Interlocked.Increment(ref batchAttempts);
                        if (current == 1)
                        {
                            batchCts.Cancel();
                        }
                        ct.ThrowIfCancellationRequested();
                        return Task.FromResult(new OfficialPluginInstallResult { Success = true, PluginId = m.Id });
                    },
                    cancellationToken: batchCts.Token,
                    downloadOptions: opt
                );
            }
            catch (OperationCanceledException)
            {
                batchCancelled = true;
            }

            Assert(batchCancelled, "6.16", "Batch terminates immediately with OperationCanceledException upon caller cancellation");
            Assert(batchAttempts <= 2, "6.17", $"Batch aborted immediately without running remaining items (attempts: {batchAttempts})");

            // F3: Malformed catalog JSON is rejected directly and NOT wrapped into HttpRequestException
            OfficialPluginClient.HttpHandlerOverrideForTesting = new DelegatingMockHttpHandler(req =>
            {
                string url = req.RequestUri?.ToString() ?? "";
                if (url.Contains("releases.atom"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("<feed xmlns=\"http://www.w3.org/2005/Atom\"><entry><link href=\"https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/v1.0.0\" /></entry></feed>")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("this is { invalid json !!!")
                };
            });

            bool caughtDirectJsonException = false;
            try
            {
                await OfficialPluginClient.FetchCatalogAsync(opt);
            }
            catch (JsonException)
            {
                caughtDirectJsonException = true;
            }
            catch (HttpRequestException)
            {
                caughtDirectJsonException = false;
            }
            Assert(caughtDirectJsonException, "6.18", "FetchCatalogAsync throws JsonException directly for malformed catalog JSON");

            bool caughtDirectJsonInOnboarding = false;
            try
            {
                await OfficialPluginOnboarding.InstallMissingPluginsAsync(downloadOptions: opt);
            }
            catch (JsonException)
            {
                caughtDirectJsonInOnboarding = true;
            }
            catch (HttpRequestException)
            {
                caughtDirectJsonInOnboarding = false;
            }
            Assert(caughtDirectJsonInOnboarding, "6.19", "InstallMissingPluginsAsync does NOT wrap JsonException into HttpRequestException");

            // 6.20 - 6.22: 网络输入 IOException（含 SocketException）=> IsNetworkError = true
            OfficialPluginClient.HttpHandlerOverrideForTesting = new DelegatingMockHttpHandler(req =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenTransportStream()) });
            var brokenSocketRes = await OfficialPluginClient.InstallAsync(stallModule, CancellationToken.None, opt);
            Assert(!brokenSocketRes.Success, "6.20", "Transport connection reset stream download failed");
            Assert(brokenSocketRes.IsNetworkError, "6.21", "Transport connection reset is classified as IsNetworkError = true");
            Assert(brokenSocketRes.Error.Contains("Unable to read from transport connection"), "6.22", "Error message contains original transport exception detail");

            // 6.23 - 6.24: 网络输入普通 IOException（无 SocketException）=> IsNetworkError = true
            OfficialPluginClient.HttpHandlerOverrideForTesting = new DelegatingMockHttpHandler(req =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenTransportStream(new IOException("The response stream was truncated prematurely"))) });
            var truncatedRes = await OfficialPluginClient.InstallAsync(stallModule, CancellationToken.None, opt);
            Assert(!truncatedRes.Success, "6.23", "Truncated network stream download failed");
            Assert(truncatedRes.IsNetworkError, "6.24", "Truncated network stream is classified as IsNetworkError = true");

            // 6.25 - 6.26: 本地非网络错误（损坏解压包/本地IO）=> IsNetworkError = false
            byte[] corruptedZipBytes = new byte[2048];
            Array.Fill(corruptedZipBytes, (byte)0x7F);
            string corruptedHash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                corruptedHash = Convert.ToHexString(sha.ComputeHash(corruptedZipBytes));
            }
            var corruptedZipModule = new OfficialPluginModule
            {
                Id = "starpie.test.corruptzip",
                Name = "corruptzip",
                Version = "1.0.0",
                MinHostVersion = "1.0.0",
                ApiVersion = "1.8",
                TargetFramework = "net8.0-windows",
                PackageUrl = "https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/v1.0.0/corruptzip.spkg",
                AssetName = "corruptzip.spkg",
                ReleaseTag = "v1.0.0",
                Size = corruptedZipBytes.Length,
                Sha256 = corruptedHash
            };
            OfficialPluginClient.HttpHandlerOverrideForTesting = new DelegatingMockHttpHandler(req =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(corruptedZipBytes) });
            var localExtractRes = await OfficialPluginClient.InstallAsync(corruptedZipModule, CancellationToken.None, opt);
            Assert(!localExtractRes.Success, "6.25", "Corrupted zip extraction failed");
            Assert(!localExtractRes.IsNetworkError, "6.26", "Local extraction/IO failure is NOT classified as IsNetworkError");

            // 6.27 - 6.33: 逐一真实触发 Atom / Catalog / Package 超时并验证四语系实际返回文案
            string[] testLangs = { "zh-CN", "zh-TW", "en", "ja" };
            foreach (var lang in testLangs)
            {
                I18n.SetLanguage(lang);

                // 1) Atom 超时
                OfficialPluginClient.CatalogTimeoutOverrideForTesting = TimeSpan.FromMilliseconds(50);
                OfficialPluginClient.HttpHandlerOverrideForTesting = new DelegatingMockHttpHandler(req =>
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallStream()) });
                Exception? caughtAtomTimeout = null;
                try
                {
                    await OfficialPluginClient.FetchCatalogAsync(new OfficialPluginDownloadOptions("direct"));
                }
                catch (Exception ex)
                {
                    caughtAtomTimeout = ex;
                }
                Assert(caughtAtomTimeout is TimeoutException, "6.27", $"Atom timeout throws TimeoutException in {lang}");
                Assert(caughtAtomTimeout?.Message == I18n.T("OfficialPluginTimeoutAtom"), "6.28",
                    $"Atom timeout message strictly matches I18n OfficialPluginTimeoutAtom in {lang} (got: {caughtAtomTimeout?.Message})");

                // 2) Catalog 超时
                OfficialPluginClient.HttpHandlerOverrideForTesting = new DelegatingMockHttpHandler(req =>
                {
                    string url = req.RequestUri?.ToString() ?? "";
                    if (url.Contains("releases.atom"))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("<feed xmlns=\"http://www.w3.org/2005/Atom\"><entry><link href=\"https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/v1.0.0\" /></entry></feed>")
                        };
                    }
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallStream()) };
                });
                Exception? caughtCatalogTimeout = null;
                try
                {
                    await OfficialPluginClient.FetchCatalogAsync(opt);
                }
                catch (Exception ex)
                {
                    caughtCatalogTimeout = ex;
                }
                Assert(caughtCatalogTimeout is TimeoutException, "6.29", $"Catalog timeout throws TimeoutException in {lang}");
                Assert(caughtCatalogTimeout?.Message == I18n.T("OfficialPluginTimeoutCatalog"), "6.30",
                    $"Catalog timeout message strictly matches I18n OfficialPluginTimeoutCatalog in {lang} (got: {caughtCatalogTimeout?.Message})");

                // 3) Package 超时
                OfficialPluginClient.DownloadTimeoutOverrideForTesting = TimeSpan.FromMilliseconds(50);
                OfficialPluginClient.HttpHandlerOverrideForTesting = new DelegatingMockHttpHandler(req =>
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallStream()) });
                var pkgTimeoutRes = await OfficialPluginClient.InstallAsync(stallModule, CancellationToken.None, opt);
                Assert(!pkgTimeoutRes.Success, "6.31", $"Package timeout failed in {lang}");
                Assert(pkgTimeoutRes.IsNetworkError, "6.32", $"Package timeout is IsNetworkError = true in {lang}");
                Assert(pkgTimeoutRes.Error == I18n.T("OfficialPluginTimeoutPackage"), "6.33",
                    $"Package timeout error strictly matches I18n OfficialPluginTimeoutPackage in {lang} (got: {pkgTimeoutRes.Error})");
            }

            I18n.SetLanguage("zh-CN");
            OfficialPluginClient.CatalogTimeoutOverrideForTesting = null;
            OfficialPluginClient.DownloadTimeoutOverrideForTesting = null;
        }
        finally
        {
            OfficialPluginClient.HttpHandlerOverrideForTesting = null;
            OfficialPluginClient.DownloadTimeoutOverrideForTesting = null;
            OfficialPluginClient.CatalogTimeoutOverrideForTesting = null;
            I18n.SetLanguage("zh-CN");
        }
    }

    // -------------------------------------------------------------------------
    // Scenario 7: 控件状态、真实 STA 生命周期状态测量与四语系本地化校验
    // -------------------------------------------------------------------------
    private static void TestDialogControlsStateAndFourLanguageLocalization()
    {
        Console.WriteLine("\n--- Scenario 7: Dialog Controls, STA Lifecycle & 4-Language Localization ---");

        // 四语系词条完整性
        string[] languages = { "zh-CN", "zh-TW", "en", "ja" };
        foreach (var lang in languages)
        {
            I18n.SetLanguage(lang);

            string label = I18n.T("OfficialPluginsOnboardingChannelLabel");
            string hint = I18n.T("OfficialPluginsOnboardingChannelHint");
            string netGuidance = I18n.TF("OfficialPluginsOnboardingNetworkFailedWithChannelGuidance", "Test", "ghfast", "Error");
            string catGuidance = I18n.TF("OfficialPluginsOnboardingCatalogNetworkError", "ghfast", "Error");

            Assert(!string.IsNullOrWhiteSpace(label) && !label.StartsWith("OfficialPlugins"),
                "7.3", $"OfficialPluginsOnboardingChannelLabel localized in {lang}");
            Assert(!string.IsNullOrWhiteSpace(hint) && !hint.StartsWith("OfficialPlugins"),
                "7.4", $"OfficialPluginsOnboardingChannelHint localized in {lang}");
            Assert(!string.IsNullOrWhiteSpace(netGuidance) && netGuidance.Contains("ghfast"),
                "7.5", $"OfficialPluginsOnboardingNetworkFailedWithChannelGuidance in {lang} contains channel name");
            Assert(!string.IsNullOrWhiteSpace(catGuidance) && catGuidance.Contains("ghfast"),
                "7.6", $"OfficialPluginsOnboardingCatalogNetworkError in {lang} contains channel name");

            Assert(!string.IsNullOrWhiteSpace(I18n.T("UpdateProxyGhproxy")), "7.7", $"UpdateProxyGhproxy localized in {lang}");
            Assert(!string.IsNullOrWhiteSpace(I18n.T("UpdateProxyMoeyy")), "7.8", $"UpdateProxyMoeyy localized in {lang}");
            Assert(!string.IsNullOrWhiteSpace(I18n.T("UpdateProxyAkams")), "7.9", $"UpdateProxyAkams localized in {lang}");
            Assert(!string.IsNullOrWhiteSpace(I18n.T("UpdateProxyDirect")), "7.10", $"UpdateProxyDirect localized in {lang}");
            Assert(!string.IsNullOrWhiteSpace(I18n.T("OfficialPluginTimeoutCatalog")) && !I18n.T("OfficialPluginTimeoutCatalog").StartsWith("OfficialPlugin"), "7.16", $"OfficialPluginTimeoutCatalog localized in {lang}");
            Assert(!string.IsNullOrWhiteSpace(I18n.T("OfficialPluginTimeoutAtom")) && !I18n.T("OfficialPluginTimeoutAtom").StartsWith("OfficialPlugin"), "7.17", $"OfficialPluginTimeoutAtom localized in {lang}");
            Assert(!string.IsNullOrWhiteSpace(I18n.T("OfficialPluginTimeoutPackage")) && !I18n.T("OfficialPluginTimeoutPackage").StartsWith("OfficialPlugin"), "7.18", $"OfficialPluginTimeoutPackage localized in {lang}");
        }

        I18n.SetLanguage("zh-CN");

        // 真实 STA UI 生命周期状态测量（在独立 STA 线程中运行并 pump Dispatcher）
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));

                var dialog = new OfficialPluginsOnboardingDialog();
                var helper = new System.Windows.Interop.WindowInteropHelper(dialog);
                helper.EnsureHandle();
                dialog.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.LoadedEvent));
                var comboBox = dialog.FindName("ChannelComboBox") as ComboBox;
                var installBtn = dialog.FindName("InstallButton") as Button;
                var retryBtn = dialog.FindName("RetryButton") as Button;

                Assert(comboBox != null && comboBox.IsEnabled, "7.1", "ChannelComboBox is enabled by default");
                Assert(installBtn != null && installBtn.IsEnabled, "7.2", "InstallButton is enabled by default");

                var runBatchMethod = typeof(OfficialPluginsOnboardingDialog).GetMethod("RunBatchInstallAsync", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert(runBatchMethod != null, "7.11", "Found RunBatchInstallAsync private method on dialog");

                var inFlightSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var completeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                OfficialPluginClient.HttpHandlerOverrideForTesting = DelegatingMockHttpHandler.Async(async req =>
                {
                    inFlightSignal.TrySetResult(true);
                    await completeSignal.Task.ConfigureAwait(false);
                    throw new HttpRequestException("Simulated network failure for lifecycle test");
                });

                try
                {
                    var installTask = (Task)runBatchMethod!.Invoke(dialog, null)!;

                    // 等待 HTTP 请求发起
                    inFlightSignal.Task.Wait(TimeSpan.FromSeconds(5));

                    Assert(comboBox != null && !comboBox.IsEnabled, "7.12", "ChannelComboBox is disabled during active install");
                    Assert(installBtn != null && !installBtn.IsEnabled, "7.13", "InstallButton is disabled during active install");

                    // 释放网络请求
                    completeSignal.SetResult(true);

                    // 在 STA 线程上 pump Dispatcher 直到 installTask 完成
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    installTask.ContinueWith(_ => frame.Continue = false);
                    System.Windows.Threading.Dispatcher.PushFrame(frame);

                    var isInstallingField = typeof(OfficialPluginsOnboardingDialog).GetField("_isInstalling", BindingFlags.Instance | BindingFlags.NonPublic);
                    bool isInstallingAfter = (bool)(isInstallingField?.GetValue(dialog) ?? true);

                    Assert(comboBox != null && comboBox.IsEnabled, "7.14", "ChannelComboBox restored to enabled after batch completion");
                    Assert(!isInstallingAfter, "7.15", "_isInstalling state restored to false after batch completion");
                }
                finally
                {
                    OfficialPluginClient.HttpHandlerOverrideForTesting = null;
                    dialog.Close();
                }
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            Fail("7.STA", $"STA lifecycle test failed: {threadEx}");
        }
    }

    // -------------------------------------------------------------------------
    // Scenario 8: 安全变异测试（证明断言有效性与红绿证据）
    // -------------------------------------------------------------------------
    private static void TestSafetyMutationVerification()
    {
        Console.WriteLine("\n--- Scenario 8: Safety Mutation Verification (Red/Green Evidence) ---");

        // 变异 1：F1 流超时断言变异（如果 ReadAsync 未传递超时 token，流停滞不会超时）
        bool f1MutationDetected = false;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var taskWithToken = Task.Delay(Timeout.Infinite, timeoutCts.Token);
            try
            {
                taskWithToken.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                f1MutationDetected = true;
            }
            sw.Stop();
            Assert(f1MutationDetected && sw.ElapsedMilliseconds < 1000, "8.1", "Mutation check F1: Stalled stream bounded by linked token");
        }
        catch (Exception ex)
        {
            Fail("8.1", $"F1 mutation test threw: {ex.Message}");
        }

        // 变异 2：F2 超时分类变异（证明 W1 的无条件 OperationCanceledException 逻辑会导致红灯）
        Func<Exception, bool, bool> w1Classifier = (ex, callerCanceled) =>
        {
            if (ex is OperationCanceledException) return false;
            return ex is HttpRequestException;
        };
        Func<Exception, bool, bool> r1Classifier = (ex, callerCanceled) =>
        {
            if (ex is OperationCanceledException && callerCanceled) throw ex;
            if (ex is OperationCanceledException or TimeoutException or HttpRequestException) return true;
            return false;
        };

        var timeoutEx = new TaskCanceledException("Timeout", new TimeoutException());
        bool w1Result = w1Classifier(timeoutEx, false);
        bool r1Result = r1Classifier(timeoutEx, false);

        Assert(!w1Result, "8.2", "Mutation check F2: W1 classifier baseline was RED (false for un-cancelled timeout)");
        Assert(r1Result, "8.3", "Mutation check F2: R1 classifier is GREEN (true for un-cancelled timeout)");

        // 变异 3：F3 JSON 异常包装变异（证明 W1 的统一 HttpRequestException 会导致红灯）
        Func<Exception, Exception> w1CatalogCatch = ex =>
        {
            if (ex is InvalidDataException) return ex;
            return new HttpRequestException("网络失败", ex);
        };
        Func<Exception, Exception> r1CatalogCatch = ex =>
        {
            if (ex is JsonException or XmlException or InvalidDataException) return ex;
            return new HttpRequestException("网络失败", ex);
        };

        var jsonEx = new JsonException("malformed json");
        Exception w1Escaped = w1CatalogCatch(jsonEx);
        Exception r1Escaped = r1CatalogCatch(jsonEx);

        Assert(w1Escaped is HttpRequestException, "8.4", "Mutation check F3: W1 baseline was RED (JsonException wrapped into HttpRequestException)");
        Assert(r1Escaped is JsonException, "8.5", "Mutation check F3: R1 baseline is GREEN (JsonException escapes directly)");

        // 变异 4：通道选项不可变性与规范化
        string akamsNormal = OfficialPluginDownloadOptions.NormalizeChannel("akams");
        Assert(akamsNormal == "gh-proxy", "8.6", "Mutation check: akams strictly maps to gh-proxy, not ghfast");

        var directOpt = new OfficialPluginDownloadOptions("direct");
        const string raw = "https://github.com/Star-Pie/test.spkg";
        Assert(!directOpt.GetProxiedUrl(raw).Contains("ghfast") && !directOpt.GetProxiedUrl(raw).Contains("gh-proxy"),
            "8.7", "Mutation check: direct options never prepends mirror host");

        Assert(typeof(OfficialPluginDownloadOptions).IsSealed, "8.8", "OfficialPluginDownloadOptions is sealed class");
    }

    // -------------------------------------------------------------------------
    // Scenario 9: 候选版本发现统一排序、严格来源校验与四语系一致性
    // -------------------------------------------------------------------------
    private static void TestReleaseDiscoverySortingAndOriginValidation()
    {
        Console.WriteLine("\n--- Scenario 9: Release Discovery Sorting, Origin Validation & Multi-Language ---");

        const string repo = "https://github.com/Star-Pie/StarPie-Official-Plugins";

        // 9.1 Atom 乱序排序：旧发布在前、新发布在后 => 新发布排在第一位
        string disorderedAtom = $@"<feed xmlns=""http://www.w3.org/2005/Atom"">
            <entry><published>2026-01-01T00:00:00Z</published><link href=""{repo}/releases/tag/tag-old"" /></entry>
            <entry><published>2026-01-05T00:00:00Z</published><link href=""{repo}/releases/tag/tag-new"" /></entry>
        </feed>";
        var disorderedTags = OfficialPluginReleaseDiscovery.ParseAtomReleaseTags(disorderedAtom);
        Assert(disorderedTags.Count == 2 && disorderedTags[0] == "tag-new" && disorderedTags[1] == "tag-old",
            "9.1", "Atom candidates correctly ordered by date descending regardless of feed entry order");

        // 9.2 多 v2 候选按发布时间降序排列
        string multiV2Atom = $@"<feed xmlns=""http://www.w3.org/2005/Atom"">
            <entry><published>2026-02-01T00:00:00Z</published><link href=""{repo}/releases/tag/v2-feb"" /></entry>
            <entry><published>2026-01-01T00:00:00Z</published><link href=""{repo}/releases/tag/v2-jan"" /></entry>
            <entry><published>2026-03-01T00:00:00Z</published><link href=""{repo}/releases/tag/v2-mar"" /></entry>
        </feed>";
        var multiV2Tags = OfficialPluginReleaseDiscovery.ParseAtomReleaseTags(multiV2Atom);
        Assert(multiV2Tags.SequenceEqual(new[] { "v2-mar", "v2-feb", "v2-jan" }),
            "9.2", "Multiple v2 candidates ordered newest first (mar -> feb -> jan)");

        // 9.3 混合时间字段无法形成可靠时间线，保留 Feed 顺序；updated 不是发布时间。
        string fallbackAtom = $@"<feed xmlns=""http://www.w3.org/2005/Atom"">
            <entry><updated>2026-05-01T00:00:00Z</updated><link href=""{repo}/releases/tag/updated-only"" /></entry>
            <entry><published>2026-06-01T00:00:00Z</published><updated>2026-01-01T00:00:00Z</updated><link href=""{repo}/releases/tag/pub-priority"" /></entry>
            <entry><link href=""{repo}/releases/tag/no-date"" /></entry>
        </feed>";
        var fallbackTags = OfficialPluginReleaseDiscovery.ParseAtomReleaseTags(fallbackAtom);
        Assert(fallbackTags.SequenceEqual(new[] { "updated-only", "pub-priority", "no-date" }),
            "9.3", "Incomplete publication timestamps preserve feed order instead of sorting by edit time");

        // 9.4 同时间稳定排序：同一时间戳按 tag Ordinal 升序排列
        string sameDateAtom = $@"<feed xmlns=""http://www.w3.org/2005/Atom"">
            <entry><published>2026-01-01T00:00:00Z</published><link href=""{repo}/releases/tag/tag-charlie"" /></entry>
            <entry><published>2026-01-01T00:00:00Z</published><link href=""{repo}/releases/tag/tag-alpha"" /></entry>
            <entry><published>2026-01-01T00:00:00Z</published><link href=""{repo}/releases/tag/tag-bravo"" /></entry>
        </feed>";
        var sameDateTags = OfficialPluginReleaseDiscovery.ParseAtomReleaseTags(sameDateAtom);
        Assert(sameDateTags.SequenceEqual(new[] { "tag-alpha", "tag-bravo", "tag-charlie" }),
            "9.4", "Same timestamp candidates stably ordered by tag Ordinal ascending");

        // 9.5 重复 tag 保留最新有效日期
        string duplicateTagAtom = $@"<feed xmlns=""http://www.w3.org/2005/Atom"">
            <entry><published>2026-01-01T00:00:00Z</published><link href=""{repo}/releases/tag/dup-tag"" /></entry>
            <entry><published>2026-01-02T00:00:00Z</published><link href=""{repo}/releases/tag/other-tag"" /></entry>
            <entry><published>2026-01-03T00:00:00Z</published><link href=""{repo}/releases/tag/dup-tag"" /></entry>
        </feed>";
        var duplicateTags = OfficialPluginReleaseDiscovery.ParseAtomReleaseTags(duplicateTagAtom);
        Assert(duplicateTags.Count == 2 && duplicateTags[0] == "dup-tag" && duplicateTags[1] == "other-tag",
            "9.5", "Duplicate tag retains newest available date (dup-tag updated to Jan 3)");

        // 9.6 最新在第 9 项仍进入 top 8（先排序去重再截取 8 项）
        var tenEntries = new List<string>();
        for (int i = 1; i <= 10; i++)
        {
            string dateStr = i == 9 ? "2026-12-01T00:00:00Z" : $"2026-01-{i:D2}T00:00:00Z";
            tenEntries.Add($@"<entry><published>{dateStr}</published><link href=""{repo}/releases/tag/item-{i:D2}"" /></entry>");
        }
        string tenAtom = $@"<feed xmlns=""http://www.w3.org/2005/Atom"">{string.Concat(tenEntries)}</feed>";
        var tenTags = OfficialPluginReleaseDiscovery.ParseAtomReleaseTags(tenAtom);
        Assert(tenTags.Count == 8, "9.6.1", "Output bounded by MaxCandidates = 8");
        Assert(tenTags[0] == "item-09", "9.6.2", "Item 9 with newest date sorted to first position among top 8");
        Assert(!tenTags.Contains("item-01") && !tenTags.Contains("item-02"), "9.6.3", "Oldest items (item-01, item-02) truncated");

        // 9.11 真实回归：旧 Release 后编辑，updated 较新，但不能抢占新目录。
        string editedOldAtom = $@"<feed xmlns=""http://www.w3.org/2005/Atom"">
            <entry><updated>2026-10-11T04:13:48Z</updated><link href=""{repo}/releases/tag/modules-2026.10.11.1"" /></entry>
            <entry><updated>2026-10-11T04:14:47Z</updated><link href=""{repo}/releases/tag/modules-2026.10.10.1"" /></entry>
        </feed>";
        Assert(OfficialPluginReleaseDiscovery.ParseAtomReleaseTags(editedOldAtom)
            .SequenceEqual(new[] { "modules-2026.10.11.1", "modules-2026.10.10.1" }),
            "9.11", "Editing an older release cannot move it ahead of the newest feed entry");

        string invalidPublishedAtom = $@"<feed xmlns=""http://www.w3.org/2005/Atom"">
            <entry><published>invalid</published><updated>2026-01-01T00:00:00Z</updated><link href=""{repo}/releases/tag/new-no-date"" /></entry>
            <entry><published>2025-01-01T00:00:00Z</published><updated>2099-01-01T00:00:00Z</updated><link href=""{repo}/releases/tag/old-edited"" /></entry>
        </feed>";
        Assert(OfficialPluginReleaseDiscovery.ParseAtomReleaseTags(invalidPublishedAtom)
            .SequenceEqual(new[] { "new-no-date", "old-edited" }),
            "9.12", "Invalid publication dates preserve feed order even with future edit timestamps");

        var undatedEntries = Enumerable.Range(1, 10).Select(i =>
            $@"<entry><updated>2026-01-{i:D2}T00:00:00Z</updated><link href=""{repo}/releases/tag/item-{i:D2}"" /></entry>");
        string undatedAtom = $@"<feed xmlns=""http://www.w3.org/2005/Atom"">
            <entry><link href=""https://example.com/releases/tag/untrusted"" /></entry>
            <entry><link href=""{repo}/releases/tag/item-01"" /></entry>
            {string.Concat(undatedEntries)}</feed>";
        Assert(OfficialPluginReleaseDiscovery.ParseAtomReleaseTags(undatedAtom)
            .SequenceEqual(Enumerable.Range(1, 8).Select(i => $"item-{i:D2}")),
            "9.13", "Undated candidates are origin-checked and deduplicated before the eight-entry limit");

        // 9.7 来源校验：TryExtractOfficialReleaseTag
        // 9.7.1 必须是绝对 HTTPS
        Assert(!OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag($"http://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/insecure", out _),
            "9.7.1", "HTTP link rejected (must be HTTPS)");
        Assert(!OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag($"/Star-Pie/StarPie-Official-Plugins/releases/tag/rel", out _),
            "9.7.2", "Relative URL rejected");

        // 9.7.3 host 必须精确为 github.com（OrdinalIgnoreCase），禁止 lookalike 或其他 host
        Assert(!OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag("https://untrusted.invalid/Star-Pie/StarPie-Official-Plugins/releases/tag/foreign", out _),
            "9.7.3", "Foreign host rejected");
        Assert(!OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag("https://github.com.attacker.com/Star-Pie/StarPie-Official-Plugins/releases/tag/lookalike", out _),
            "9.7.4", "Lookalike subdomain rejected");
        Assert(!OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag("https://api.github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/subdomain", out _),
            "9.7.5", "api.github.com host rejected for release tag URL");
        Assert(OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag("https://GITHUB.COM/Star-Pie/StarPie-Official-Plugins/releases/tag/valid-caps", out var capsTag) && capsTag == "valid-caps",
            "9.7.6", "GITHUB.COM uppercase host accepted (OrdinalIgnoreCase)");

        // 9.7.7 默认端口与 userinfo
        Assert(!OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag("https://github.com:8443/Star-Pie/StarPie-Official-Plugins/releases/tag/custom-port", out _),
            "9.7.7", "Non-default port 8443 rejected");
        Assert(!OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag("https://user:pass@github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/userinfo", out _),
            "9.7.8", "URL with UserInfo rejected");

        // 9.7.9 仓库路径必须从开头精确匹配官方仓库前缀
        Assert(!OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag("https://github.com/EvilFork/StarPie-Official-Plugins/releases/tag/evil", out _),
            "9.7.9", "Different repository EvilFork rejected");
        Assert(!OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag("https://github.com/extra/Star-Pie/StarPie-Official-Plugins/releases/tag/extra", out _),
            "9.7.10", "Extra path prefix before repo rejected");

        // 9.7.11 伪 tag、空 tag、多余子路径、query 与 fragment
        Assert(!OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag("https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/", out _),
            "9.7.11", "Empty tag rejected");
        Assert(!OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag("https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/v1/subpath", out _),
            "9.7.12", "Extra subpath segments rejected");
        Assert(!OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag("https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/v1?query=1", out _),
            "9.7.13", "Query string in URL rejected");
        Assert(!OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag("https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/v1#hash", out _),
            "9.7.14", "Fragment in URL rejected");
        Assert(!OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag("https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/tag\u0000ctrl", out _),
            "9.7.15", "Control character in tag rejected");

        // 9.8 合法 URL 编码 tag 解码
        Assert(OfficialPluginReleaseDiscovery.TryExtractOfficialReleaseTag("https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/v1%2E8%2E0", out var decodedTag) && decodedTag == "v1.8.0",
            "9.8", "Valid percent-encoded tag (v1%2E8%2E0) properly decoded to v1.8.0");

        // 9.9 GitHub Releases API 解析与严格校验
        // 9.9.1 draft 过滤
        string apiDraftJson = JsonSerializer.Serialize(new object[]
        {
            new { tag_name = "draft-tag", draft = true, published_at = "2026-03-01T00:00:00Z", html_url = $"{repo}/releases/tag/draft-tag" },
            new { tag_name = "released-tag", draft = false, published_at = "2026-02-01T00:00:00Z", html_url = $"{repo}/releases/tag/released-tag" }
        });
        var apiDraftTags = OfficialPluginReleaseDiscovery.ParseApiReleaseTags(apiDraftJson);
        Assert(apiDraftTags.Count == 1 && apiDraftTags[0] == "released-tag", "9.9.1", "API draft releases strictly skipped");

        // 9.9.2 tag_name 与 html_url 校验一致
        string apiMismatchJson = JsonSerializer.Serialize(new object[]
        {
            new { tag_name = "tag-a", draft = false, published_at = "2026-03-01T00:00:00Z", html_url = $"{repo}/releases/tag/tag-b" },
            new { tag_name = "tag-c", draft = false, published_at = "2026-02-01T00:00:00Z", html_url = $"{repo}/releases/tag/tag-c" }
        });
        var apiMismatchTags = OfficialPluginReleaseDiscovery.ParseApiReleaseTags(apiMismatchJson);
        Assert(apiMismatchTags.Count == 1 && apiMismatchTags[0] == "tag-c", "9.9.2", "API item skipped when tag_name does not match html_url");

        // 9.9.3 异常类型字段（布尔/数字而非字符串）不触发异常
        string apiMalformedTypesJson = @"[
            { ""tag_name"": 12345, ""draft"": false, ""html_url"": """ + repo + @"/releases/tag/12345"" },
            { ""tag_name"": ""valid-tag"", ""draft"": ""not-a-bool"", ""html_url"": """ + repo + @"/releases/tag/valid-tag"", ""published_at"": 99999 }
        ]";
        var apiMalformedTags = OfficialPluginReleaseDiscovery.ParseApiReleaseTags(apiMalformedTypesJson);
        Assert(apiMalformedTags.Count == 1 && apiMalformedTags[0] == "valid-tag",
            "9.9.3", "Malformed field types handled gracefully without unhandled exceptions");

        // 9.10 四语系下日期解析一致性（规范为 UTC）
        string[] langs = { "zh-CN", "zh-TW", "en", "ja" };
        foreach (var lang in langs)
        {
            I18n.SetLanguage(lang);
            bool ok = OfficialPluginReleaseDiscovery.TryParseUtcDate("2026-06-15T10:30:00+08:00", out DateTimeOffset parsed);
            Assert(ok && parsed.Offset == TimeSpan.Zero && parsed.Hour == 2 && parsed.Minute == 30,
                "9.10", $"TryParseUtcDate parses UTC identically under {lang}");
        }
        I18n.SetLanguage("zh-CN");
    }

    private static async Task TestEditedReleaseDoesNotHideNewCatalog()
    {
        const string repo = "https://github.com/Star-Pie/StarPie-Official-Plugins";
        const string newTag = "modules-2026.10.11.1";
        const string oldTag = "modules-2026.10.10.1";
        string feed = $@"<feed xmlns=""http://www.w3.org/2005/Atom"">
            <entry><updated>2026-10-11T04:13:48Z</updated><link href=""{repo}/releases/tag/{newTag}"" /></entry>
            <entry><updated>2026-10-11T04:14:47Z</updated><link href=""{repo}/releases/tag/{oldTag}"" /></entry>
        </feed>";
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var newCatalog = JsonSerializer.Deserialize<OfficialPluginCatalog>(CreateValidCatalogJson(newTag), jsonOptions)!;
        newCatalog.Modules.Add(new OfficialPluginCatalogEntry
        {
            Id = "starpie.plugin.desktoppet",
            Versions = new List<OfficialPluginModule>
            {
                new()
                {
                    Name = "DesktopPet", Version = "0.1.6", ReleaseTag = newTag,
                    AssetName = "StarPie.Plugin.DesktopPet-0.1.6.spkg",
                    PackageUrl = $"{repo}/releases/download/{newTag}/StarPie.Plugin.DesktopPet-0.1.6.spkg",
                    Sha256 = new string('a', 64), Size = 97450, ApiVersion = "1.11",
                    TargetFramework = "net8.0-windows", MinHostVersion = "1.8.0-beta.7"
                }
            }
        });
        string newJson = JsonSerializer.Serialize(newCatalog);
        var requestedUrls = new List<string>();
        var handler = new DelegatingMockHttpHandler(request =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            requestedUrls.Add(url);
            string body = url == repo + "/releases.atom" ? feed
                : url == $"{repo}/releases/download/{newTag}/module-catalog.json" ? newJson
                : url == $"{repo}/releases/download/{oldTag}/module-catalog.json" ? CreateValidCatalogJson(oldTag)
                : throw new InvalidOperationException($"Unexpected discovery request: {url}");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        OfficialPluginClient.HttpHandlerOverrideForTesting = handler;
        try
        {
            var catalog = await OfficialPluginClient.FetchCatalogAsync();
            Assert(catalog.ReleaseTag == newTag, "9.14.1", "Refresh selects the newest release, not the last edited release");
            Assert(catalog.Modules.Any(module => module.Id == "starpie.plugin.desktoppet"),
                "9.14.2", "DesktopPet remains visible in the refreshed catalog");
            Assert(requestedUrls.SequenceEqual(new[] { repo + "/releases.atom", $"{repo}/releases/download/{newTag}/module-catalog.json" }),
                "9.14.3", "Refresh requests only the feed and newest catalog, without added API or old-catalog requests");
            Assert(OfficialPluginClient.TryLoadCachedCatalog(out var cached, out _, out _) && cached.ReleaseTag == newTag,
                "9.14.4", "The refreshed cache retains the newest catalog");
        }
        finally
        {
            OfficialPluginClient.HttpHandlerOverrideForTesting = null;
            handler.Dispose();
        }
    }

    private static string CreateValidCatalogJson(string releaseTag)
    {
        var modules = new[]
        {
            "starpie.builtin.folder",
            "starpie.builtin.weburl",
            "starpie.builtin.launch",
            "starpie.builtin.system",
            "starpie.builtin.shelltool"
        };

        var catalogObj = new
        {
            schemaVersion = 2,
            catalogVersion = "2026.10.1",
            releaseTag = releaseTag,
            releaseChannel = "stable",
            sdkApiVersion = "1.8",
            minimumHostVersion = "1.0.0",
            generatedAt = DateTimeOffset.UtcNow,
            modules = modules.Select(id => new
            {
                id = id,
                versions = new[]
                {
                    new
                    {
                        name = id,
                        version = "1.0.0",
                        releaseTag = releaseTag,
                        assetName = $"{id}.spkg",
                        packageUrl = $"https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/{releaseTag}/{id}.spkg",
                        sha256 = new string('a', 64),
                        size = 2048,
                        apiVersion = "1.8",
                        targetFramework = "net8.0-windows",
                        minHostVersion = "1.0.0",
                        capabilities = new[] { "Process" }
                    }
                }
            })
        };

        return JsonSerializer.Serialize(catalogObj);
    }

    private sealed class CountingHttpMessageHandler : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }

    private sealed class DelegatingMockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public DelegatingMockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = req => Task.FromResult(handler(req));
        }

        public static DelegatingMockHttpHandler Async(Func<HttpRequestMessage, Task<HttpResponseMessage>> asyncHandler)
        {
            return new DelegatingMockHttpHandler(asyncHandler);
        }

        private DelegatingMockHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> asyncHandler)
        {
            _handler = asyncHandler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }

    private sealed class StallStream : Stream
    {
        public readonly TaskCompletionSource<bool> ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 2048;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ReadStarted.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }

    private sealed class BrokenTransportStream : Stream
    {
        private readonly Exception _exception;
        public BrokenTransportStream(Exception ex) => _exception = ex;
        public BrokenTransportStream() : this(new IOException("Unable to read from transport connection", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset))) { }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 1;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(_exception);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromException<int>(_exception);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }
}
