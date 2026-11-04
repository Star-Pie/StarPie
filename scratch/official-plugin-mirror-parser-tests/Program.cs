using System.Globalization;
using System.Text;
using System.Text.Json;
using WinPieGestures.Plugins;

internal static class Program
{
    private const string CatalogName = "module-catalog.json";
    private const string SafeBase = "https://assets.example/";

    private static int s_passed;
    private static int s_failed;

    private static int Main()
    {
        Run("good release and read-only result", GoodReleaseAndReadOnly);
        Run("published date preference and ordering", PublishedDatePreference);
        Run("source publication survives historical backfill", SourcePublication);
        Run("draft and catalog filtering", DraftAndCatalogFiltering);
        Run("asset name and URL safety", AssetSafety);
        Run("tag validation and length boundary", TagValidation);
        Run("duplicate tag and asset rejection", DuplicateData);
        Run("limits and malformed input", LimitsAndMalformedInput);

        Console.WriteLine($"RESULT: {(s_failed == 0 ? "PASS" : "FAIL")} ({s_passed} passed, {s_failed} failed)");
        return s_failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            s_passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            s_failed++;
            Console.WriteLine($"FAIL {name}: {exception}");
        }
    }

    private static void GoodReleaseAndReadOnly()
    {
        string json = Root(
            Release(
                "modules-1.2.3+build_1",
                "2026-01-02T03:04:05Z",
                null,
                assets: new object[]
                {
                    Asset(CatalogName, SafeBase + "module-catalog.json"),
                    Asset("plugin.spkg", SafeBase + "plugin.spkg"),
                }));

        IReadOnlyList<OfficialPluginMirrorRelease> releases = Parse(json);
        Equal(1, releases.Count, "A valid release must be returned.");
        OfficialPluginMirrorRelease release = releases[0];
        Equal("modules-1.2.3+build_1", release.Tag, "The modules tag must be preserved.");
        Equal(Date("2026-01-02T03:04:05Z"), release.PublishedAt,
            "The published date must be parsed.");
        Equal(2, release.Assets.Count, "Catalog and spkg assets must be retained.");
        Equal(
            SafeBase + "module-catalog.json",
            release.Assets[CatalogName],
            "The catalog URL must remain exact.");
        Equal(
            SafeBase + "plugin.spkg",
            release.Assets["plugin.spkg"],
            "The spkg URL must remain exact.");

        Throws<NotSupportedException>(
            () => ((IDictionary<string, string>)release.Assets).Add(
                "new.spkg",
                SafeBase + "new.spkg"),
            "The assets dictionary must be read-only.");
        Throws<NotSupportedException>(
            () => ((IList<OfficialPluginMirrorRelease>)releases).Add(release),
            "The release list must be read-only.");
    }

    private static void PublishedDatePreference()
    {
        string json = Root(
            Release(
                "a",
                "2026-01-01T00:00:00Z",
                "2020-01-01T00:00:00Z",
                updatedAt: "2035-01-01T00:00:00Z",
                assets: new object[] { Catalog() }),
            Release(
                "b",
                "not-a-date",
                "2025-12-31T00:00:00Z",
                updatedAt: "2036-01-01T00:00:00Z",
                assets: new object[] { Catalog() }),
            Release(
                "c",
                "2026-02-01T00:00:00Z",
                "2020-01-01T00:00:00Z",
                updatedAt: "2010-01-01T00:00:00Z",
                assets: new object[] { Catalog() }));

        IReadOnlyList<OfficialPluginMirrorRelease> releases = Parse(json);
        Equal(3, releases.Count, "All dated releases must be returned.");
        Equal("c", releases[0].Tag, "The newest publication must sort first.");
        Equal("a", releases[1].Tag, "The next publication must follow.");
        Equal("b", releases[2].Tag, "The created_at fallback must be used.");
        Equal(Date("2026-01-01T00:00:00Z"), releases[1].PublishedAt,
            "published_at must win over created_at.");
        Equal(Date("2025-12-31T00:00:00Z"), releases[2].PublishedAt,
            "created_at must be used when published_at is invalid.");
    }

    private static void SourcePublication()
    {
        var older = Release("old", "2026-12-01T00:00:00Z", null, assets: new object[] { Catalog() });
        older["body"] = "Old notes\n\n<!-- starpie-source-published-at: 2026-09-01T00:00:00.000Z -->";
        var newer = Release("new", "2026-11-01T00:00:00Z", null, assets: new object[] { Catalog() });
        newer["body"] = "New notes\n\n<!-- starpie-source-published-at: 2026-10-01T00:00:00.000Z -->";
        Equal("new", Parse(Root(older, newer))[0].Tag, "Source publication must precede mirror creation order.");
        Assert(Parse(Root(older))[0].HasSourcePublicationTime, "Valid synchronization provenance must be identifiable.");
        older["body"] = "<!-- starpie-source-published-at: malformed -->";
        Equal(Date("2026-12-01T00:00:00Z"), Parse(Root(older))[0].PublishedAt, "Invalid marker must fall back to API date.");
        Assert(!Parse(Root(older))[0].HasSourcePublicationTime, "Malformed provenance cannot qualify catalog discovery.");
    }

    private static void DraftAndCatalogFiltering()
    {
        string json = Root(
            Release(
                "draft-bool",
                "2026-01-01T00:00:00Z",
                null,
                draft: true,
                assets: new object[] { Catalog() }),
            Release(
                "draft-status",
                "2026-01-02T00:00:00Z",
                null,
                releaseStatus: "DRAFT",
                assets: new object[] { Catalog() }),
            Release(
                "no-catalog",
                "2026-01-03T00:00:00Z",
                null,
                assets: new object[]
                {
                    Asset("plugin.spkg", SafeBase + "plugin.spkg"),
                }),
            Release(
                "unsafe-catalog",
                "2026-01-04T00:00:00Z",
                null,
                assets: new object[]
                {
                    Asset(CatalogName, "http://evil.example/module-catalog.json"),
                }),
            Release(
                "safe",
                "2026-01-05T00:00:00Z",
                null,
                releaseStatus: "published",
                assets: new object[]
                {
                    Catalog(),
                    Asset("unsafe.spkg", "http://evil.example/unsafe.spkg"),
                }));

        IReadOnlyList<OfficialPluginMirrorRelease> releases = Parse(json);
        Equal(1, releases.Count, "Drafts and releases without a safe catalog must be ignored.");
        Equal("safe", releases[0].Tag, "The safe release must remain.");
        Equal(1, releases[0].Assets.Count,
            "Unsafe non-catalog assets must not be retained.");
        Assert(releases[0].Assets.ContainsKey(CatalogName),
            "The safe catalog asset must be retained.");
    }

    private static void AssetSafety()
    {
        string maxLengthName = new string('x', 200);
        string tooLongName = new string('y', 201);
        string json = Root(
            Release(
                "asset-safety",
                "2026-01-01T00:00:00Z",
                null,
                assets: new object[]
                {
                    Catalog(),
                    Asset("plugin.spkg", SafeBase + "plugin.spkg"),
                    Asset(maxLengthName, SafeBase + maxLengthName),
                    Asset("../escape.spkg", SafeBase + "escape.spkg"),
                    Asset("bad/name.spkg", SafeBase + "bad-slash.spkg"),
                    Asset("bad\\name.spkg", SafeBase + "bad-backslash.spkg"),
                    Asset(".", SafeBase + "dot.spkg"),
                    Asset("..", SafeBase + "dotdot.spkg"),
                    Asset("bad\u0001name.spkg", SafeBase + "control.spkg"),
                    Asset(tooLongName, SafeBase + "too-long.spkg"),
                    Asset("unsafe.spkg", "http://evil.example/unsafe.spkg"),
                    Asset("empty.spkg", ""),
                }));

        OfficialPluginMirrorRelease release = Parse(json)[0];
        Equal(3, release.Assets.Count,
            "Only the catalog, spkg, and maximum-length safe name may remain.");
        Assert(release.Assets.ContainsKey("plugin.spkg"),
            "A safe spkg asset must remain available.");
        Assert(release.Assets.ContainsKey(maxLengthName),
            "A 200-character safe filename must be accepted.");
        Assert(!release.Assets.ContainsKey("../escape.spkg"),
            "Path traversal names must be rejected.");
        Assert(!release.Assets.ContainsKey("bad/name.spkg"),
            "Slash names must be rejected.");
        Assert(!release.Assets.ContainsKey("bad\\name.spkg"),
            "Backslash names must be rejected.");
        Assert(!release.Assets.ContainsKey("."),
            "A dot name must be rejected.");
        Assert(!release.Assets.ContainsKey(".."),
            "A dot-dot name must be rejected.");
        Assert(!release.Assets.ContainsKey(tooLongName),
            "A 201-character filename must be rejected.");
        Assert(!release.Assets.ContainsKey("unsafe.spkg"),
            "An unsafe URL must be rejected.");
        Assert(!release.Assets.ContainsKey("empty.spkg"),
            "An empty URL must be rejected.");
    }

    private static void TagValidation()
    {
        string maxTag = new string('a', 128);
        Equal(maxTag, Parse(Root(
                Release(maxTag, "2026-01-01T00:00:00Z", null,
                    assets: new object[] { Catalog() })))[0].Tag,
            "A 128-character tag must be accepted.");

        string[] invalidTags =
        {
            "-leading",
            "bad/tag",
            "bad tag",
            "bad%20tag",
            "bad\ttag",
            "é",
            new string('a', 129),
        };
        foreach (string tag in invalidTags)
        {
            Throws<InvalidDataException>(
                () => Parse(Root(
                    Release(tag, "2026-01-01T00:00:00Z", null,
                        assets: new object[] { Catalog() }))),
                $"The tag '{tag}' must be rejected.");
        }
    }

    private static void DuplicateData()
    {
        Throws<InvalidDataException>(
            () => Parse(Root(
                Release("same-tag", "2026-01-01T00:00:00Z", null,
                    assets: new object[] { Catalog() }),
                Release("same-tag", "2026-01-02T00:00:00Z", null,
                    assets: new object[] { Catalog() }))),
            "Duplicate tags must not be merged.");

        Throws<InvalidDataException>(
            () => Parse(Root(
                Release(
                    "duplicate-assets",
                    "2026-01-01T00:00:00Z",
                    null,
                    assets: new object[]
                    {
                        Catalog(),
                        Asset("plugin.spkg", SafeBase + "first.spkg"),
                        Asset("plugin.spkg", SafeBase + "second.spkg"),
                    }))),
            "Duplicate valid asset names must not be merged.");
    }

    private static void LimitsAndMalformedInput()
    {
        IReadOnlyList<OfficialPluginMirrorRelease> empty = Parse("[]");
        Equal(0, empty.Count, "An empty root must return an empty list.");

        object[] hundredEmptyReleases = Enumerable
            .Range(0, 100)
            .Select(_ => (object)new Dictionary<string, object?>())
            .ToArray();
        Equal(0, Parse(Root(hundredEmptyReleases)).Count,
            "One hundred ignored releases must be accepted.");

        object[] tooManyReleases = Enumerable
            .Range(0, 101)
            .Select(_ => (object)new Dictionary<string, object?>())
            .ToArray();
        Throws<InvalidDataException>(
            () => Parse(Root(tooManyReleases)),
            "More than one hundred releases must be rejected.");

        object[] atAssetLimit = new object[512];
        atAssetLimit[0] = Catalog();
        for (int index = 1; index < atAssetLimit.Length; index++)
        {
            atAssetLimit[index] = Asset(
                $"plugin-{index}.spkg",
                SafeBase + $"plugin-{index}.spkg");
        }
        OfficialPluginMirrorRelease atLimitRelease = Parse(Root(
                Release("asset-limit", "2026-01-01T00:00:00Z", null,
                    assets: atAssetLimit)))[0];
        Equal(512, atLimitRelease.Assets.Count,
            "Five hundred twelve assets must be accepted.");

        object[] overAssetLimit = new object[513];
        Array.Copy(atAssetLimit, overAssetLimit, atAssetLimit.Length);
        overAssetLimit[512] = Asset("extra.spkg", SafeBase + "extra.spkg");
        Throws<InvalidDataException>(
            () => Parse(Root(
                Release("too-many-assets", "2026-01-01T00:00:00Z", null,
                    assets: overAssetLimit))),
            "More than five hundred twelve assets must be rejected.");

        string tooLarge = "[" + new string('x', 2 * 1024 * 1024) + "]";
        Throws<InvalidDataException>(
            () => Parse(tooLarge),
            "Input over two MiB must be rejected.");

        string tooDeep = new string('[', 33) + "0" + new string(']', 33);
        Throws<InvalidDataException>(
            () => Parse(tooDeep),
            "JSON depth over thirty-two must be rejected.");

        Throws<InvalidDataException>(() => Parse("{"), "Malformed JSON must be rejected.");
        Throws<InvalidDataException>(() => Parse("{}"), "A non-array root must be rejected.");
        Throws<InvalidDataException>(
            () => Parse(Root(new Dictionary<string, object?>
            {
                ["tag_name"] = "bad-assets",
                ["published_at"] = "2026-01-01T00:00:00Z",
                ["assets"] = new Dictionary<string, object?>(),
            })),
            "A non-array assets field must be rejected.");
        Throws<ArgumentNullException>(
            () => OfficialPluginMirrorReleaseParser.Parse("[]", null!),
            "A null URL validator must be rejected.");
    }

    private static IReadOnlyList<OfficialPluginMirrorRelease> Parse(string json)
    {
        return OfficialPluginMirrorReleaseParser.Parse(json, IsAllowedAssetUrl);
    }

    private static bool IsAllowedAssetUrl(string url)
    {
        return url.StartsWith(SafeBase, StringComparison.Ordinal);
    }

    private static string Root(params object[] releases)
    {
        return JsonSerializer.Serialize(releases);
    }

    private static Dictionary<string, object?> Release(
        string tag,
        string? publishedAt,
        string? createdAt,
        bool draft = false,
        string? releaseStatus = null,
        string? updatedAt = null,
        params object[] assets)
    {
        var release = new Dictionary<string, object?>
        {
            ["tag_name"] = tag,
            ["draft"] = draft,
            ["assets"] = assets,
        };
        if (publishedAt != null)
        {
            release["published_at"] = publishedAt;
        }

        if (createdAt != null)
        {
            release["created_at"] = createdAt;
        }

        if (releaseStatus != null)
        {
            release["release_status"] = releaseStatus;
        }

        if (updatedAt != null)
        {
            release["updated_at"] = updatedAt;
        }

        return release;
    }

    private static Dictionary<string, string> Catalog()
    {
        return Asset(CatalogName, SafeBase + CatalogName);
    }

    private static Dictionary<string, string> Asset(string name, string url)
    {
        return new Dictionary<string, string>
        {
            ["name"] = name,
            ["browser_download_url"] = url,
        };
    }

    private static DateTimeOffset Date(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{message} Expected: {expected}; actual: {actual}.");
        }
    }

    private static void Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{message} Expected {typeof(TException).Name}.");
    }
}
