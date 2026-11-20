using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace WinPieGestures.Plugins;

/// <summary>
/// 官方插件发布候选版本发现服务。
/// <para>
/// 负责以有界、安全、独立于具体资产下载通道的策略发现官方已发布的候选版本列表。
/// 优先查询官方 Atom Feed，失败或受限时以有界官方 Releases API 补充，
/// 严格校验来源，按发布时间或 Feed 顺序发现候选，不将编辑时间或 latest 稳定发布与全集等同。
/// </para>
/// </summary>
internal static class OfficialPluginReleaseDiscovery
{
    internal const string RepositoryUrl = "https://github.com/Star-Pie/StarPie-Official-Plugins";
    internal const string ReleasesFeedUrl = RepositoryUrl + "/releases.atom";
    internal const string ReleasesApiUrl = "https://api.github.com/repos/Star-Pie/StarPie-Official-Plugins/releases?per_page=8";
    internal const int MaxCandidates = 8;

    /// <summary>
    /// 发现官方发布候选标签（有可靠时间线时按发布时间排序，否则保留 Feed 顺序，最多 MaxCandidates 项）。
    /// </summary>
    public static async Task<IReadOnlyList<string>> DiscoverCandidateTagsAsync(
        HttpClient client,
        CancellationToken cancellationToken, Func<string, string>? transportUrl = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 1. 首先尝试官方 Atom Feed
        try
        {
            using var atomRequest = new HttpRequestMessage(HttpMethod.Get, transportUrl?.Invoke(ReleasesFeedUrl) ?? ReleasesFeedUrl);
            atomRequest.Headers.Accept.Clear();
            atomRequest.Headers.Accept.ParseAdd("application/atom+xml");

            using var atomResponse = await client.SendAsync(atomRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (atomResponse.IsSuccessStatusCode)
            {
                string feed = await atomResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var atomTags = ParseAtomReleaseTags(feed);
                if (atomTags.Count > 0)
                {
                    return atomTags;
                }
            }
            else
            {
                AppLogger.LogInfo($"[plugin] 官方 releases.atom 返回 {(int)atomResponse.StatusCode}，尝试通过 GitHub Releases API 发现候选发布...");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.LogInfo($"[plugin] 获取官方 releases.atom 失败 ({ex.Message})，尝试通过 GitHub Releases API 发现候选发布...");
        }

        // 2. 若 Atom 未发现任何候选标签，使用官方 Releases API 列表补足
        cancellationToken.ThrowIfCancellationRequested();

        using var apiRequest = new HttpRequestMessage(HttpMethod.Get, transportUrl?.Invoke(ReleasesApiUrl) ?? ReleasesApiUrl);
        apiRequest.Headers.Accept.Clear();
        apiRequest.Headers.Accept.ParseAdd("application/vnd.github+json");
        apiRequest.Headers.Accept.ParseAdd("application/json");

        using var apiResponse = await client.SendAsync(apiRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        apiResponse.EnsureSuccessStatusCode();

        string json = await apiResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseApiReleaseTags(json);
    }

    /// <summary>
    /// 统一校验官方 Release Tag URL 并安全提取解码后的 Tag 名称。
    /// 规则：
    /// 1. 绝对 HTTPS、host 精确为 github.com（忽略大小写）、无 userinfo、默认 HTTPS 端口 (443)；
    /// 2. 无 query 或 fragment 混入；
    /// 3. AbsolutePath 必须从开头匹配精确官方仓库前缀 /Star-Pie/StarPie-Official-Plugins/releases/tag/；
    /// 4. Tag 不能为空、不能含额外斜杠子路径、不能含控制字符或空白；
    /// 5. 正确解码合法 URL 编码 Tag。
    /// </summary>
    internal static bool TryExtractOfficialReleaseTag(string? rawUrl, out string tag)
    {
        tag = "";
        if (string.IsNullOrWhiteSpace(rawUrl)) return false;

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out Uri? uri)) return false;

        // 1. 绝对 HTTPS、host 精确 github.com (OrdinalIgnoreCase)、无 userinfo、默认 HTTPS 端口
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (!uri.IsDefaultPort && uri.Port != 443) return false;

        // 2. 无 query 或 fragment 混入
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;

        // 3. 仓库 release tag 路径必须从开头匹配精确官方仓库前缀
        const string expectedPrefix = "/Star-Pie/StarPie-Official-Plugins/releases/tag/";
        string path = uri.AbsolutePath;
        if (!path.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        // 4. 提取 tag 部分，不接受空 tag 或额外子路径
        string rawTag = path[expectedPrefix.Length..];
        if (string.IsNullOrEmpty(rawTag) || rawTag.Contains('/'))
        {
            return false;
        }

        // 5. 正确解码合法 URL 编码 tag，保持原 tag 语义
        string decodedTag;
        try
        {
            decodedTag = Uri.UnescapeDataString(rawTag);
        }
        catch
        {
            return false;
        }

        // 6. 不接受空 tag、控制字符或空白字符
        if (string.IsNullOrWhiteSpace(decodedTag)) return false;
        if (decodedTag.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;

        tag = decodedTag;
        return true;
    }

    /// <summary>
    /// 使用 InvariantCulture 解析日期字符串并规范化为 UTC。
    /// </summary>
    internal static bool TryParseUtcDate(string? text, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed))
        {
            result = parsed.ToUniversalTime();
            return true;
        }

        return false;
    }

    /// <summary>
    /// 解析 Atom Feed 中的发布条目。
    /// published 全部有效时按发布时间排序，否则保留 Feed 顺序；updated 仅表示编辑时间。
    /// </summary>
    internal static List<string> ParseAtomReleaseTags(string feed)
    {
        if (string.IsNullOrWhiteSpace(feed)) return new List<string>();

        XDocument document;
        try
        {
            document = XDocument.Parse(feed, LoadOptions.None);
        }
        catch
        {
            return new List<string>();
        }

        XNamespace atom = "http://www.w3.org/2005/Atom";
        var candidates = new List<(string Tag, DateTimeOffset? Date)>();

        foreach (XElement entry in document.Descendants(atom + "entry"))
        {
            string? href = entry.Elements(atom + "link")
                .Select(link => (string?)link.Attribute("href"))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            if (!TryExtractOfficialReleaseTag(href, out string tag))
            {
                continue;
            }

            DateTimeOffset? date = null;
            string? pubText = entry.Element(atom + "published")?.Value;
            if (TryParseUtcDate(pubText, out DateTimeOffset pubDate))
            {
                date = pubDate;
            }

            candidates.Add((tag, date));
        }

        // GitHub 的 Release Feed 通常只给 updated。编辑旧 Release 会改变该时间，
        // 但不能据此将旧目录提升为最新；缺少完整发布时间线时沿用 Feed 的候选顺序。
        if (candidates.All(candidate => candidate.Date.HasValue))
        {
            return SortAndDeduplicateCandidates(candidates);
        }
        return candidates.Select(candidate => candidate.Tag)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxCandidates)
            .ToList();
    }

    /// <summary>
    /// 解析 GitHub Releases API JSON 中的发布条目。
    /// 优先取 published_at，缺失/无效时回退至 created_at。过滤 draft，校验 tag_name 与 html_url 一致。
    /// </summary>
    internal static List<string> ParseApiReleaseTags(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();

        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        var candidates = new List<(string Tag, DateTimeOffset? Date)>();

        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;

            // 过滤 draft
            if (element.TryGetProperty("draft", out JsonElement draftProp)
                && draftProp.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            // 获取 tag_name
            if (!element.TryGetProperty("tag_name", out JsonElement tagProp)
                || tagProp.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            string? tagName = tagProp.GetString();
            if (string.IsNullOrWhiteSpace(tagName)) continue;

            // 核验来源仓库 html_url（必须为合法官方仓库 tag URL）
            if (!element.TryGetProperty("html_url", out JsonElement htmlProp)
                || htmlProp.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            string? htmlUrl = htmlProp.GetString();
            if (!TryExtractOfficialReleaseTag(htmlUrl, out string urlTag))
            {
                continue;
            }

            // API 的 tag_name 必须与校验后 html_url 中的 tag 一致
            if (!string.Equals(tagName, urlTag, StringComparison.Ordinal))
            {
                continue;
            }

            // 解析时间（优先 published_at，回落 created_at）
            DateTimeOffset? date = null;
            if (element.TryGetProperty("published_at", out JsonElement pubProp)
                && pubProp.ValueKind == JsonValueKind.String
                && TryParseUtcDate(pubProp.GetString(), out DateTimeOffset pubDate))
            {
                date = pubDate;
            }
            else if (element.TryGetProperty("created_at", out JsonElement createProp)
                && createProp.ValueKind == JsonValueKind.String
                && TryParseUtcDate(createProp.GetString(), out DateTimeOffset createDate))
            {
                date = createDate;
            }

            candidates.Add((tagName, date));
        }

        return SortAndDeduplicateCandidates(candidates);
    }

    /// <summary>
    /// 对候选条目进行统一的去重、时间排序与上限截取。
    /// 1. 相同 tag 重复出现时，保留可用的最新日期；
    /// 2. 按可信时间降序排列；日期全部无效/缺失的候选放最后；同时间按 tag 的 Ordinal 顺序稳定排列；
    /// 3. 先解析全部合法候选、去重并排序，最后截取最多 MaxCandidates (8) 项。
    /// </summary>
    private static List<string> SortAndDeduplicateCandidates(IEnumerable<(string Tag, DateTimeOffset? Date)> candidates)
    {
        var tagMap = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
        foreach (var (tag, date) in candidates)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;

            if (!tagMap.TryGetValue(tag, out DateTimeOffset? existingDate))
            {
                tagMap[tag] = date;
            }
            else
            {
                if (date.HasValue)
                {
                    if (!existingDate.HasValue || date.Value > existingDate.Value)
                    {
                        tagMap[tag] = date;
                    }
                }
            }
        }

        var list = new List<KeyValuePair<string, DateTimeOffset?>>(tagMap);
        list.Sort(CandidateComparer.Instance);

        return list.Take(MaxCandidates).Select(kvp => kvp.Key).ToList();
    }

    private sealed class CandidateComparer : IComparer<KeyValuePair<string, DateTimeOffset?>>
    {
        public static readonly CandidateComparer Instance = new();

        public int Compare(KeyValuePair<string, DateTimeOffset?> x, KeyValuePair<string, DateTimeOffset?> y)
        {
            bool xHas = x.Value.HasValue;
            bool yHas = y.Value.HasValue;
            if (xHas && !yHas) return -1;
            if (!xHas && yHas) return 1;
            if (xHas && yHas)
            {
                int dateCmp = y.Value!.Value.CompareTo(x.Value!.Value);
                if (dateCmp != 0) return dateCmp;
            }

            return string.Compare(x.Key, y.Key, StringComparison.Ordinal);
        }
    }
}
