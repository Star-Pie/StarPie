using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace WinPieGestures;

internal sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
	private readonly string[] _prereleaseIdentifiers;

	private ReleaseVersion(int major, int minor, int patch, string[] prereleaseIdentifiers)
	{
		Major = major;
		Minor = minor;
		Patch = patch;
		_prereleaseIdentifiers = prereleaseIdentifiers;
	}

	public int Major { get; }
	public int Minor { get; }
	public int Patch { get; }
	public bool IsPrerelease => _prereleaseIdentifiers.Length > 0;
	public Version CoreVersion => new Version(Major, Minor, Patch);

	public static bool TryParse(string? value, out ReleaseVersion? version)
	{
		version = null;
		if (string.IsNullOrWhiteSpace(value)) return false;

		string clean = value.Trim().TrimStart('v', 'V');
		int metadataIndex = clean.IndexOf('+');
		if (metadataIndex >= 0) clean = clean.Substring(0, metadataIndex);

		string[] versionParts = clean.Split(new[] { '-' }, 2, StringSplitOptions.None);
		string[] coreParts = versionParts[0].Split('.');
		if (coreParts.Length != 3 ||
			!int.TryParse(coreParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
			!int.TryParse(coreParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
			!int.TryParse(coreParts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
		{
			return false;
		}

		string[] prereleaseIdentifiers = Array.Empty<string>();
		if (versionParts.Length == 2)
		{
			if (string.IsNullOrWhiteSpace(versionParts[1])) return false;
			prereleaseIdentifiers = versionParts[1].Split('.');
			if (prereleaseIdentifiers.Any(string.IsNullOrWhiteSpace)) return false;
		}

		version = new ReleaseVersion(major, minor, patch, prereleaseIdentifiers);
		return true;
	}

	public static ReleaseVersion FromAssemblyVersion(Version version)
	{
		return new ReleaseVersion(
			Math.Max(version.Major, 0),
			Math.Max(version.Minor, 0),
			Math.Max(version.Build, 0),
			Array.Empty<string>());
	}

	public int CompareTo(ReleaseVersion? other)
	{
		if (other == null) return 1;

		int comparison = Major.CompareTo(other.Major);
		if (comparison == 0) comparison = Minor.CompareTo(other.Minor);
		if (comparison == 0) comparison = Patch.CompareTo(other.Patch);
		if (comparison != 0) return comparison;

		if (!IsPrerelease && !other.IsPrerelease) return 0;
		if (!IsPrerelease) return 1;
		if (!other.IsPrerelease) return -1;

		int sharedLength = Math.Min(_prereleaseIdentifiers.Length, other._prereleaseIdentifiers.Length);
		for (int i = 0; i < sharedLength; i++)
		{
			string left = _prereleaseIdentifiers[i];
			string right = other._prereleaseIdentifiers[i];
			bool leftIsNumber = long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out long leftNumber);
			bool rightIsNumber = long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out long rightNumber);

			if (leftIsNumber && rightIsNumber)
			{
				comparison = leftNumber.CompareTo(rightNumber);
			}
			else if (leftIsNumber)
			{
				comparison = -1;
			}
			else if (rightIsNumber)
			{
				comparison = 1;
			}
			else
			{
				comparison = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
			}

			if (comparison != 0) return comparison;
		}

		return _prereleaseIdentifiers.Length.CompareTo(other._prereleaseIdentifiers.Length);
	}

	public override string ToString()
	{
		string core = $"{Major}.{Minor}.{Patch}";
		return IsPrerelease ? $"{core}-{string.Join(".", _prereleaseIdentifiers)}" : core;
	}
}

public class ReleaseInfo
{
	public string TagName { get; set; } = "";

	public Version? ParsedVersion { get; set; }

	internal ReleaseVersion? ComparableVersion { get; set; }

	public string Title { get; set; } = "";

	public string Body { get; set; } = "";

	public DateTime PublishedAt { get; set; }

	public bool IsPrerelease { get; set; }

	public string HtmlUrl { get; set; } = "";

	public bool IsNewerVersion { get; set; }

	public string? StandaloneAssetUrl { get; set; }

	public long StandaloneAssetSize { get; set; }

	public string? LightweightAssetUrl { get; set; }

	public long LightweightAssetSize { get; set; }
}

public class UpdateProgressInfo
{
	public int Percent { get; set; }

	public long BytesReceived { get; set; }

	public long TotalBytesToReceive { get; set; }

	public double SpeedBytesPerSecond { get; set; }

	public string FormattedSpeed => SpeedBytesPerSecond switch
	{
		>= 1024 * 1024 => $"{SpeedBytesPerSecond / (1024 * 1024):F1} MB/s",
		>= 1024 => $"{SpeedBytesPerSecond / 1024:F0} KB/s",
		_ => $"{SpeedBytesPerSecond:F0} B/s"
	};

	public string FormattedProgress => TotalBytesToReceive > 0
		? $"{BytesReceived / (1024.0 * 1024.0):F1} MB / {TotalBytesToReceive / (1024.0 * 1024.0):F1} MB"
		: $"{BytesReceived / (1024.0 * 1024.0):F1} MB";
}

public class UpdateManager
{
	private static readonly Lazy<UpdateManager> _instance = new Lazy<UpdateManager>(() => new UpdateManager());
	public static UpdateManager Instance => _instance.Value;

	private readonly HttpClient _httpClient;
	private const string RepoOwner = "SoftBlack42";
	private const string RepoName = "StarPie";

	private UpdateManager()
	{
		HttpClientHandler handler = new HttpClientHandler
		{
			AllowAutoRedirect = true,
			MaxAutomaticRedirections = 5
		};
		_httpClient = new HttpClient(handler)
		{
			Timeout = TimeSpan.FromSeconds(15)
		};
		// User-Agent 里的版本号从 AppVersionInfo 取，不要再写一份字面量：
		// 那份字面量不在任何发版检查清单上，发版时必然漏改，而且
		// 这里原来取的是 AssemblyVersion（1.8.0.0 → "1.8.0"），会把预发布标识丢掉，
		// 与设置页里另一个 UA 的写法也不一致。
		_httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StarPie-Updater", AppVersionInfo.DisplayVersion));
	}

	public bool IsCurrentInstallationStandalone()
	{
		try
		{
			string appDir = AppDomain.CurrentDomain.BaseDirectory;
			return !File.Exists(Path.Combine(appDir, "StarPie.dll"));
		}
		catch
		{
			return true;
		}
	}

	public Version GetCurrentVersion()
	{
		return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 6, 8);
	}

	private ReleaseVersion GetCurrentReleaseVersion()
	{
		Assembly assembly = Assembly.GetExecutingAssembly();
		string? informationalVersion = assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
			.InformationalVersion;

		if (ReleaseVersion.TryParse(informationalVersion, out ReleaseVersion? parsedVersion) && parsedVersion != null)
		{
			return parsedVersion;
		}

		return ReleaseVersion.FromAssemblyVersion(GetCurrentVersion());
	}

	public string GetProxiedDownloadUrl(string rawUrl, string proxySource, string customProxy = "")
	{
		if (string.IsNullOrWhiteSpace(rawUrl)) return "";

		return proxySource?.ToLowerInvariant() switch
		{
			"ghfast" or "ghproxy" or "moeyy" => $"https://ghfast.top/{rawUrl}",
			"gh-proxy" or "akams" => $"https://gh-proxy.com/{rawUrl}",
			"mirror" => $"https://mirror.ghproxy.com/{rawUrl}",
			"custom" when !string.IsNullOrWhiteSpace(customProxy) => $"{customProxy.TrimEnd('/')}/{rawUrl}",
			_ => rawUrl
		};
	}

	public async Task<List<ReleaseInfo>> FetchAllReleasesAsync(string proxySource = "ghfast", string customProxy = "", CancellationToken ct = default)
	{
		List<ReleaseInfo> allReleases = new List<ReleaseInfo>();
		try
		{
			string apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=30";
			string? json = null;

			// Tier 1: GitHub REST API (5 秒快速超时)
			try
			{
				using var apiCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
				apiCts.CancelAfter(TimeSpan.FromSeconds(5));
				using var apiReq = new HttpRequestMessage(HttpMethod.Get, apiUrl);
				apiReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
				using HttpResponseMessage response = await _httpClient.SendAsync(apiReq, apiCts.Token).ConfigureAwait(false);
				if (response.IsSuccessStatusCode)
				{
					json = await response.Content.ReadAsStringAsync(apiCts.Token).ConfigureAwait(false);
				}
				else
				{
					AppLogger.LogWarn($"GitHub REST API returned non-success code: {(int)response.StatusCode} {response.ReasonPhrase}");
				}
			}
			catch (Exception ex)
			{
				AppLogger.LogWarn($"Direct GitHub REST API fetch failed ({ex.Message}), falling back to GitHub Atom feed...");
			}

			if (!string.IsNullOrEmpty(json))
			{
				try
				{
					using JsonDocument doc = JsonDocument.Parse(json);
					JsonElement root = doc.RootElement;
					if (root.ValueKind == JsonValueKind.Array)
					{
						foreach (JsonElement item in root.EnumerateArray())
						{
							ReleaseInfo? rel = ParseReleaseElement(item);
							if (rel != null)
							{
								allReleases.Add(rel);
							}
						}
					}
					else if (root.ValueKind == JsonValueKind.Object)
					{
						ReleaseInfo? rel = ParseReleaseElement(root);
						if (rel != null)
						{
							allReleases.Add(rel);
						}
					}
				}
				catch (Exception ex)
				{
					AppLogger.LogWarn($"ParseReleaseElement JSON error: {ex.Message}");
				}
			}

			// Tier 2: 降级至 GitHub 官方 Releases Atom XML Feed
			if (allReleases.Count == 0)
			{
				try
				{
					using var atomCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
					atomCts.CancelAfter(TimeSpan.FromSeconds(6));
					string atomUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases.atom";
					using var atomReq = new HttpRequestMessage(HttpMethod.Get, atomUrl);
					atomReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/atom+xml"));
					atomReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
					using HttpResponseMessage atomResponse = await _httpClient.SendAsync(atomReq, atomCts.Token).ConfigureAwait(false);
					if (atomResponse.IsSuccessStatusCode)
					{
						string atomXml = await atomResponse.Content.ReadAsStringAsync(atomCts.Token).ConfigureAwait(false);
						allReleases = ParseAllAtomFeed(atomXml);
						AppLogger.LogInfo($"Successfully fetched {allReleases.Count} releases via GitHub Atom Feed.");
					}
				}
				catch (Exception ex)
				{
					AppLogger.LogWarn($"GitHub Atom feed fetch failed ({ex.Message}), falling back to latest release redirect probe...");
				}
			}

			// Tier 3: 降级至 GitHub Releases /latest 网页 302 重定向探测
			if (allReleases.Count == 0)
			{
				try
				{
					using var redirectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
					redirectCts.CancelAfter(TimeSpan.FromSeconds(5));
					string latestUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";

					using var handler = new HttpClientHandler { AllowAutoRedirect = false };
					using var probeClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
					probeClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StarPie-Updater", Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.6.8"));

					using var req = new HttpRequestMessage(HttpMethod.Head, latestUrl);
					using var resp = await probeClient.SendAsync(req, redirectCts.Token).ConfigureAwait(false);
					if ((int)resp.StatusCode is 301 or 302 && resp.Headers.Location != null)
					{
						string loc = resp.Headers.Location.ToString();
						int tagIdx = loc.LastIndexOf("/tag/", StringComparison.OrdinalIgnoreCase);
						if (tagIdx >= 0)
						{
							string tag = loc.Substring(tagIdx + 5).Trim();
							AppLogger.LogInfo($"Successfully checked update tag via Latest Web Redirect: {tag}");
							allReleases.Add(CreateSynthesizedReleaseInfo(tag, "GitHub Releases 网页探测"));
						}
					}
				}
				catch (Exception ex)
				{
					AppLogger.LogWarn($"Latest release redirect probe failed: {ex.Message}");
				}
			}

			// 按版本倒序排列（最高/最新排在最前）
			allReleases.Sort((a, b) =>
			{
				if (a.ComparableVersion != null && b.ComparableVersion != null)
				{
					return b.ComparableVersion.CompareTo(a.ComparableVersion);
				}
				return b.PublishedAt.CompareTo(a.PublishedAt);
			});
		}
		catch (Exception ex)
		{
			AppLogger.LogError("FetchAllReleasesAsync error", ex);
		}

		return allReleases;
	}

	public ReleaseInfo? GetLatestUpdateRelease(List<ReleaseInfo> allReleases, string channel = "Stable")
	{
		bool isBetaChannel = string.Equals(channel, "Beta", StringComparison.OrdinalIgnoreCase);
		return allReleases.FirstOrDefault(r => isBetaChannel || !r.IsPrerelease);
	}

	public List<ReleaseInfo> GetRollbackCandidates(List<ReleaseInfo> allReleases, string channel = "Stable")
	{
		ReleaseVersion currentVer = GetCurrentReleaseVersion();
		bool isBetaChannel = string.Equals(channel, "Beta", StringComparison.OrdinalIgnoreCase);

		var previous = allReleases
			.Where(r => r.ComparableVersion != null && r.ComparableVersion.CompareTo(currentVer) < 0)
			.ToList();

		if (isBetaChannel)
		{
			// 测试版 / Beta 通道：允许回退到最近 5 个历史版本（含测试版与正式版）
			return previous.Take(5).ToList();
		}
		else
		{
			// 正式版 / Stable 通道：允许回退到最近 2 个历史正式稳定版（严格过滤 pre-release）
			return previous.Where(r => !r.IsPrerelease).Take(2).ToList();
		}
	}

	public async Task<ReleaseInfo?> CheckForUpdateAsync(string channel = "Stable", string proxySource = "ghfast", string customProxy = "", CancellationToken ct = default)
	{
		var all = await FetchAllReleasesAsync(proxySource, customProxy, ct).ConfigureAwait(false);
		return GetLatestUpdateRelease(all, channel);
	}

	public List<ReleaseInfo> ParseAllAtomFeed(string xml)
	{
		List<ReleaseInfo> result = new List<ReleaseInfo>();
		try
		{
			XDocument doc = XDocument.Parse(xml);
			XNamespace ns = "http://www.w3.org/2005/Atom";
			var entries = doc.Root?.Elements(ns + "entry");
			if (entries == null) return result;

			ReleaseVersion currentVer = GetCurrentReleaseVersion();

			foreach (var entry in entries)
			{
				string id = entry.Element(ns + "id")?.Value ?? "";
				string title = entry.Element(ns + "title")?.Value ?? "";
				string updatedStr = entry.Element(ns + "updated")?.Value ?? "";
				DateTime.TryParse(updatedStr, out DateTime publishedAt);

				// 从 id 提取 tag，例如: tag:github.com,2008:Repository/1343746076/v1.6.5
				string tag = "";
				int slashIdx = id.LastIndexOf('/');
				if (slashIdx >= 0 && slashIdx < id.Length - 1)
				{
					tag = id.Substring(slashIdx + 1).Trim();
				}

				// 若 id 未提取到，尝试从 link 提取: <link rel="alternate" type="text/html" href="https://github.com/.../releases/tag/v1.6.5"/>
				if (string.IsNullOrEmpty(tag))
				{
					var linkElem = entry.Element(ns + "link");
					string href = linkElem?.Attribute("href")?.Value ?? "";
					int tagIdx = href.LastIndexOf("/tag/", StringComparison.OrdinalIgnoreCase);
					if (tagIdx >= 0)
					{
						tag = href.Substring(tagIdx + 5).Trim();
					}
				}

				if (string.IsNullOrEmpty(tag)) continue;

				ReleaseVersion? releaseVersion = ParseReleaseVersionFromTag(tag);
				if (releaseVersion == null) continue;
				Version parsedVer = releaseVersion.CoreVersion;

				// 「内测」「尝鲜」匹配的是 GitHub Release 的<b>标题</b> —— 那串字是发布者
				// （本项目自己）写在 tag / release 上的数据，不是本程序的界面文案，
				// 也不随用户切语言而变化。做 i18n 清理时按可接受项排除。
				bool isPrerelease = releaseVersion.IsPrerelease ||
					tag.Contains("beta", StringComparison.OrdinalIgnoreCase) ||
					tag.Contains("alpha", StringComparison.OrdinalIgnoreCase) ||
					tag.Contains("rc", StringComparison.OrdinalIgnoreCase) ||
					title.Contains("beta", StringComparison.OrdinalIgnoreCase) ||
					title.Contains("alpha", StringComparison.OrdinalIgnoreCase) ||
					title.Contains("内测", StringComparison.OrdinalIgnoreCase) ||
					title.Contains("尝鲜", StringComparison.OrdinalIgnoreCase);

				// 提取更新日志 HTML 并转换为纯文本
				string contentHtml = entry.Element(ns + "content")?.Value ?? "";
				string body = ConvertHtmlToMarkdown(contentHtml);

				string htmlUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/tag/{tag}";
				bool isNewer = releaseVersion.CompareTo(currentVer) > 0;

				var rel = new ReleaseInfo
				{
					TagName = tag,
					ParsedVersion = parsedVer,
					ComparableVersion = releaseVersion,
					Title = string.IsNullOrWhiteSpace(title) ? tag : title,
					Body = body,
					PublishedAt = publishedAt != default ? publishedAt : DateTime.Now,
					IsPrerelease = isPrerelease || releaseVersion.IsPrerelease,
					HtmlUrl = htmlUrl,
					IsNewerVersion = isNewer,
					StandaloneAssetUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{tag}/StarPie-{tag}-Standalone-win-x64.zip",
					LightweightAssetUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{tag}/StarPie-{tag}-Lightweight-win-x64.zip"
				};

				result.Add(rel);
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError("ParseAllAtomFeed failed", ex);
		}
		return result;
	}

	public ReleaseInfo? ParseAtomFeed(string xml, bool isBetaChannel)
	{
		var list = ParseAllAtomFeed(xml);
		return GetLatestUpdateRelease(list, isBetaChannel ? "Beta" : "Stable");
	}

	public static string ConvertHtmlToMarkdown(string html)
	{
		if (string.IsNullOrWhiteSpace(html)) return "";

		string text = html;
		text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
		text = Regex.Replace(text, @"</p>", "\n\n", RegexOptions.IgnoreCase);
		text = Regex.Replace(text, @"</li>", "\n", RegexOptions.IgnoreCase);
		text = Regex.Replace(text, @"<li>", "• ", RegexOptions.IgnoreCase);
		text = Regex.Replace(text, @"<h[1-6][^>]*>", "\n### ", RegexOptions.IgnoreCase);
		text = Regex.Replace(text, @"</h[1-6]>", "\n", RegexOptions.IgnoreCase);
		text = Regex.Replace(text, @"<[^>]+>", "", RegexOptions.IgnoreCase);
		text = WebUtility.HtmlDecode(text);
		text = Regex.Replace(text, @"\n{3,}", "\n\n");
		return text.Trim();
	}

	private ReleaseInfo CreateSynthesizedReleaseInfo(string tag, string sourceTitle)
	{
		ReleaseVersion? releaseVersion = ParseReleaseVersionFromTag(tag);
		Version? parsedVer = releaseVersion?.CoreVersion;
		ReleaseVersion currentVer = GetCurrentReleaseVersion();
		bool isNewer = releaseVersion != null && releaseVersion.CompareTo(currentVer) > 0;

		return new ReleaseInfo
		{
			TagName = tag,
			ParsedVersion = parsedVer,
			ComparableVersion = releaseVersion,
			Title = $"StarPie {tag}",
			Body = $"（通过 {sourceTitle} 探测到最新版本，详细更新日志请查看 GitHub Releases 页面）",
			PublishedAt = DateTime.Now,
			IsPrerelease = releaseVersion?.IsPrerelease == true,
			HtmlUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/tag/{tag}",
			IsNewerVersion = isNewer,
			StandaloneAssetUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{tag}/StarPie-{tag}-Standalone-win-x64.zip",
			LightweightAssetUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{tag}/StarPie-{tag}-Lightweight-win-x64.zip"
		};
	}

	private ReleaseInfo? ParseReleaseElement(JsonElement elem)
	{
		try
		{
			string tagName = elem.TryGetProperty("tag_name", out JsonElement tagElem) ? tagElem.GetString() ?? "" : "";
			string title = elem.TryGetProperty("name", out JsonElement nameElem) ? nameElem.GetString() ?? "" : tagName;
			string body = elem.TryGetProperty("body", out JsonElement bodyElem) ? bodyElem.GetString() ?? "" : "";
			string htmlUrl = elem.TryGetProperty("html_url", out JsonElement urlElem) ? urlElem.GetString() ?? "" : "";
			bool isPrerelease = elem.TryGetProperty("prerelease", out JsonElement preElem) && preElem.GetBoolean();
			DateTime publishedAt = elem.TryGetProperty("published_at", out JsonElement pubElem) && pubElem.TryGetDateTime(out DateTime dt) ? dt : DateTime.Now;

			ReleaseVersion? releaseVersion = ParseReleaseVersionFromTag(tagName);
			Version? parsedVer = releaseVersion?.CoreVersion;
			ReleaseVersion currentVer = GetCurrentReleaseVersion();
			bool isNewer = releaseVersion != null && releaseVersion.CompareTo(currentVer) > 0;

			ReleaseInfo info = new ReleaseInfo
			{
				TagName = tagName,
				ParsedVersion = parsedVer,
				ComparableVersion = releaseVersion,
				Title = string.IsNullOrWhiteSpace(title) ? tagName : title,
				Body = body,
				PublishedAt = publishedAt,
				IsPrerelease = isPrerelease || releaseVersion?.IsPrerelease == true,
				HtmlUrl = htmlUrl,
				IsNewerVersion = isNewer
			};

			if (elem.TryGetProperty("assets", out JsonElement assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement asset in assetsElem.EnumerateArray())
				{
					string assetName = asset.TryGetProperty("name", out JsonElement an) ? an.GetString() ?? "" : "";
					string downloadUrl = asset.TryGetProperty("browser_download_url", out JsonElement ad) ? ad.GetString() ?? "" : "";
					long size = asset.TryGetProperty("size", out JsonElement asz) ? asz.GetInt64() : 0;

					if (assetName.IndexOf("Standalone", StringComparison.OrdinalIgnoreCase) >= 0 && assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
					{
						info.StandaloneAssetUrl = downloadUrl;
						info.StandaloneAssetSize = size;
					}
					else if (assetName.IndexOf("Lightweight", StringComparison.OrdinalIgnoreCase) >= 0 && assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
					{
						info.LightweightAssetUrl = downloadUrl;
						info.LightweightAssetSize = size;
					}
				}
			}

			if (string.IsNullOrEmpty(info.StandaloneAssetUrl) && !string.IsNullOrEmpty(tagName))
			{
				info.StandaloneAssetUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{tagName}/StarPie-{tagName}-Standalone-win-x64.zip";
			}
			if (string.IsNullOrEmpty(info.LightweightAssetUrl) && !string.IsNullOrEmpty(tagName))
			{
				info.LightweightAssetUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{tagName}/StarPie-{tagName}-Lightweight-win-x64.zip";
			}

			return info;
		}
		catch (Exception ex)
		{
			AppLogger.LogError("ParseReleaseElement error", ex);
			return null;
		}
	}

	private static ReleaseVersion? ParseReleaseVersionFromTag(string tag)
	{
		return ReleaseVersion.TryParse(tag, out ReleaseVersion? version) ? version : null;
	}

	public static Version? ParseVersionFromTag(string tag)
	{
		return ParseReleaseVersionFromTag(tag)?.CoreVersion;
	}

	private static bool IsBetterRelease(ReleaseInfo candidate, ReleaseInfo? currentBest)
	{
		if (candidate.ComparableVersion == null) return false;
		if (currentBest?.ComparableVersion == null) return true;

		int comparison = candidate.ComparableVersion.CompareTo(currentBest.ComparableVersion);
		return comparison > 0 || (comparison == 0 && candidate.PublishedAt > currentBest.PublishedAt);
	}

	public async Task DownloadAssetAsync(string downloadUrl, string destinationZipPath, IProgress<UpdateProgressInfo>? progress, CancellationToken ct)
	{
		string tempDir = Path.GetDirectoryName(destinationZipPath)!;
		if (!Directory.Exists(tempDir))
		{
			Directory.CreateDirectory(tempDir);
		}

		if (File.Exists(destinationZipPath))
		{
			try { File.Delete(destinationZipPath); } catch { }
		}

		// 构建候选下载源列表，若主镜像源不可用自动无缝故障转移 (Failover)
		List<string> candidateUrls = new List<string> { downloadUrl };

		string rawGithubUrl = downloadUrl;
		int ghIndex = downloadUrl.IndexOf("https://github.com/", StringComparison.OrdinalIgnoreCase);
		if (ghIndex > 0)
		{
			rawGithubUrl = downloadUrl.Substring(ghIndex);
		}

		if (rawGithubUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
		{
			string m1 = $"https://ghfast.top/{rawGithubUrl}";
			string m2 = $"https://gh-proxy.com/{rawGithubUrl}";
			string m3 = $"https://mirror.ghproxy.com/{rawGithubUrl}";
			if (!candidateUrls.Contains(m1, StringComparer.OrdinalIgnoreCase)) candidateUrls.Add(m1);
			if (!candidateUrls.Contains(m2, StringComparer.OrdinalIgnoreCase)) candidateUrls.Add(m2);
			if (!candidateUrls.Contains(m3, StringComparer.OrdinalIgnoreCase)) candidateUrls.Add(m3);
			if (!candidateUrls.Contains(rawGithubUrl, StringComparer.OrdinalIgnoreCase)) candidateUrls.Add(rawGithubUrl);
		}

		Exception? lastEx = null;
		foreach (string currentUrl in candidateUrls)
		{
			try
			{
				AppLogger.LogInfo($"Attempting to download update asset from: {currentUrl}");
				using var downloadReq = new HttpRequestMessage(HttpMethod.Get, currentUrl);
				downloadReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
				using HttpResponseMessage response = await _httpClient.SendAsync(downloadReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode)
				{
					AppLogger.LogWarn($"Download attempt failed with status {response.StatusCode} for URL: {currentUrl}");
					continue;
				}

				long? totalBytes = response.Content.Headers.ContentLength;
				long totalBytesVal = totalBytes.GetValueOrDefault(-1);

				await using Stream contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
				await using FileStream fileStream = new FileStream(destinationZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);

				byte[] buffer = new byte[65536];
				long totalRead = 0;
				int read;
				Stopwatch stopwatch = Stopwatch.StartNew();
				long lastBytes = 0;
				double lastSpeed = 0;
				DateTime lastSpeedTime = DateTime.UtcNow;

				while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
				{
					await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
					totalRead += read;

					DateTime now = DateTime.UtcNow;
					double elapsedSeconds = (now - lastSpeedTime).TotalSeconds;
					if (elapsedSeconds >= 0.3)
					{
						lastSpeed = (totalRead - lastBytes) / elapsedSeconds;
						lastBytes = totalRead;
						lastSpeedTime = now;
					}

					int percent = totalBytesVal > 0 ? (int)((totalRead * 100) / totalBytesVal) : 0;
					progress?.Report(new UpdateProgressInfo
					{
						Percent = Math.Min(100, percent),
						BytesReceived = totalRead,
						TotalBytesToReceive = totalBytesVal,
						SpeedBytesPerSecond = lastSpeed
					});
				}

				progress?.Report(new UpdateProgressInfo
				{
					Percent = 100,
					BytesReceived = totalRead,
					TotalBytesToReceive = totalRead,
					SpeedBytesPerSecond = 0
				});

				// 移除下载文件的 Zone.Identifier (Mark of the Web)，防止触发 Windows 安全中心拦截
				try
				{
					string zoneStream = destinationZipPath + ":Zone.Identifier";
					if (File.Exists(zoneStream))
					{
						File.Delete(zoneStream);
					}
				}
				catch { }

				AppLogger.LogInfo($"Update package successfully downloaded ({totalRead} bytes) to {destinationZipPath}");
				return;
			}
			catch (Exception ex)
			{
				lastEx = ex;
				AppLogger.LogWarn($"Download from {currentUrl} failed ({ex.Message}), trying next mirror...");
				if (File.Exists(destinationZipPath))
				{
					try { File.Delete(destinationZipPath); } catch { }
				}
				if (ct.IsCancellationRequested)
				{
					throw;
				}
			}
		}

		if (lastEx != null)
		{
			throw lastEx;
		}
		throw new HttpRequestException("Failed to download release asset from all mirror candidates.");
	}

	public bool RestartAndApplyUpdate(string downloadedZipPath)
	{
		try
		{
			Process currentProc = Process.GetCurrentProcess();
			int pid = currentProc.Id;
			string currentExe = currentProc.MainModule?.FileName ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StarPie.exe");
			string targetDir = Path.GetDirectoryName(currentExe)!;

			// 首先尝试直接解除下载更新包的 Mark of the Web
			try
			{
				string zoneStream = downloadedZipPath + ":Zone.Identifier";
				if (File.Exists(zoneStream))
				{
					File.Delete(zoneStream);
				}
			}
			catch { }

			string scriptPath = Path.Combine(Path.GetTempPath(), $"StarPie_Updater_{pid}.ps1");

			// 使用原生 PowerShell 脚本执行解压、覆盖与重启：
			// 1. 绝不使用批处理 (>nul / 2>nul)，根除 Windows 10/11 误报 "...\Lightweight\nul" 的缺陷
			// 2. 自动调用 Unblock-File 移除所有新释放文件的 Zone.Identifier，彻底杜绝 SmartScreen / 安全中心拦截
			// 3. 严格设置 WorkingDirectory 为 %TEMP%，隔离工作目录
			// 4. UseShellExecute = false (通过 CreateProcessW 唤起，彻底绕开 Windows Shell 附件执行服务 AES 策略)
			string scriptContent = $@"# StarPie 自动更新脚本
$ErrorActionPreference = 'SilentlyContinue'

$targetPid = {pid}
$zipPath = '{downloadedZipPath.Replace("'", "''")}'
$targetDir = '{targetDir.Replace("'", "''")}'
$exePath = '{currentExe.Replace("'", "''")}'

# 1. 等待主进程完全退出并释放所有 dll/exe 文件句柄
try {{
    $proc = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
    if ($proc) {{
        $exited = $proc.WaitForExit(12000)
        if (-not $exited) {{
            Stop-Process -Id $targetPid -Force -ErrorAction SilentlyContinue
        }}
    }}
}} catch {{}}

Start-Sleep -Milliseconds 600

# 2. 解除更新包锁定
try {{
    Unblock-File -LiteralPath $zipPath -ErrorAction SilentlyContinue
}} catch {{}}

# 3. 原生解压并覆盖安装目录
$extracted = $false
try {{
    Expand-Archive -LiteralPath $zipPath -DestinationPath $targetDir -Force -ErrorAction Stop
    $extracted = $true
}} catch {{
    try {{
        & tar.exe -xf $zipPath -C $targetDir
        $extracted = $true
    }} catch {{}}
}}

# 4. 解除安装目录下所有新释放文件的安全锁定 (消除 Internet 安全阻止)
try {{
    Get-ChildItem -LiteralPath $targetDir -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue
}} catch {{}}

# 5. 重新启动新版 StarPie (以 --silent 静默模式启动，避免更新后强制弹出全量控制台 UI)
Start-Sleep -Milliseconds 400
try {{
    Start-Process -FilePath $exePath -ArgumentList ""--silent"" -WorkingDirectory $targetDir
}} catch {{
    try {{
        [System.Diagnostics.Process]::Start($exePath, ""--silent"")
    }} catch {{}}
}}

# 6. 清理临时更新包与自身脚本
Start-Sleep -Milliseconds 800
try {{ Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue }} catch {{}}
try {{ Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue }} catch {{}}
";

			File.WriteAllText(scriptPath, scriptContent, System.Text.Encoding.UTF8);

			ProcessStartInfo psi = new ProcessStartInfo
			{
				FileName = "powershell.exe",
				Arguments = $"-NoProfile -ExecutionPolicy Bypass -NonInteractive -WindowStyle Hidden -File \"{scriptPath}\"",
				WorkingDirectory = Path.GetTempPath(),
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
				UseShellExecute = false
			};

			Process.Start(psi);

			AppLogger.LogInfo("Safe PowerShell update script launched, shutting down current instance.");
			System.Windows.Application.Current.Dispatcher.Invoke(() =>
			{
				System.Windows.Application.Current.Shutdown();
			});

			return true;
		}
		catch (Exception ex)
		{
			AppLogger.LogError("RestartAndApplyUpdate failed", ex);
			return false;
		}
	}
}
