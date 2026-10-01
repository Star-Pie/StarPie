using System.IO;
using System.Text.Json;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    static int checks;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    static SimpleVersion V(string value) { if(!SimpleVersion.TryParse(value,out var result))throw new Exception(value); return result; }
    static OfficialPluginModule Release(string version, string minimum, string api = "1.4", string framework = "net8.0-windows") => new()
    {
        Name = "Launch " + version, Version = version, ApiVersion = api, TargetFramework = framework, MinHostVersion = minimum,
        AssetName = "Launch-" + version + ".spkg", ReleaseTag = "test", PackageUrl = "https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/test/Launch-" + version + ".spkg",
        Size = 100, Sha256 = new string('a',64), Capabilities = new() { "Process" }, TypeClaims = new() { "Launch=launch" },
    };
    static OfficialPluginCatalogEntry Entry(params OfficialPluginModule[] releases) => new() { Id = "starpie.builtin.launch", Versions = releases.ToList() };
    static OfficialPluginCatalog Catalog(OfficialPluginCatalogEntry entry) => new() { SchemaVersion = 2, ReleaseTag = "test", Modules = new() { entry } };
    static void Reject(Action call) { try { call(); throw new Exception("catalog unexpectedly accepted"); } catch(InvalidDataException) { checks++; } }
    static async Task Main()
    {
        Environment.SetEnvironmentVariable("LOCALAPPDATA",Path.Combine(Path.GetTempPath(),"StarPie-CatalogTests-"+Guid.NewGuid().ToString("N")));
        var low = new OfficialPluginEnvironment("1.8.0-beta.4","1.7","net8.0-windows10.0.19041.0");
        var high = new OfficialPluginEnvironment("1.8.0-beta.5","1.8","net8.0-windows10.0.19041.0");
        var old = Release("1.0.1","1.8.0-beta.1"); var latest = Release("1.1.0","1.8.0-beta.5","1.8");
        var entry = Entry(latest,old); var catalog = Catalog(entry);
        OfficialPluginClient.ValidateCatalog(catalog);
        Check(OfficialPluginVersionSelector.Select(entry,low).Compatible == old,"first install on older host chooses historical compatible package");
        Check(OfficialPluginVersionSelector.Select(entry,high).Compatible == latest,"new host chooses latest compatible package");
        Check(OfficialPluginVersionSelector.Select(entry,low).Latest == latest,"retains unsupported latest metadata");
        Check(old.Id == entry.Id && latest.Id == entry.Id,"parent ID assigned to resolved releases");
        var incompatible = new OfficialPluginListItem(Entry(latest),null,low);
        Check(!incompatible.CanInstall && !string.IsNullOrEmpty(incompatible.StateText),"no compatible version disables install with guidance");
        var first = new OfficialPluginListItem(entry,null,low);
        Check(first.CanInstall && first.Module == old,"first install button points to chosen historical asset");
        Check(!new OfficialPluginListItem(entry,"1.0.1",low).CanInstall,"installed highest compatible is not advertised as update");
        Check(new OfficialPluginListItem(entry,"1.0.1",high).CanInstall,"new compatible update allowed");
        Check(!new OfficialPluginListItem(entry,"1.2.0",high).CanInstall,"no downgrade to older compatible package");
        Check(!new OfficialPluginListItem(entry,"broken",high).CanInstall,"invalid installed version is not silently overwritten");
        foreach(var release in new[] { Release("1.1.1","1.8.0-beta.1","2.0"),Release("1.1.2","1.8.0-beta.1","1.9"),Release("1.1.3","1.8.0-beta.1","1.4","net9.0-windows") })
            Check(!OfficialPluginVersionSelector.Evaluate(release,high).IsCompatible,"API/framework boundary blocks newer unsupported release");
        var capped = Release("1.0.0","1.8.0-beta.1"); capped.MaxHostVersion="1.8.0-beta.4";
        Check(OfficialPluginVersionSelector.Evaluate(capped,high).Kind==OfficialPluginCompatibilityKind.HostTooNew,"maximum host bound enforced");
        Check(OfficialPluginVersionSelector.Evaluate(Release("1.0.0","1.8.0"),low).IsCompatible,"existing same-release beta compatibility preserved");
        var invalid = Release("1.0.0","broken"); Check(!OfficialPluginVersionSelector.Evaluate(invalid,high).IsCompatible,"malformed requirement not treated as compatible");
        foreach(string version in new[]{"1.0.0-alpha","1.0.0-alpha.1","1.0.0-alpha.beta","1.0.0-beta","1.0.0-beta.2","1.0.0-beta.11","1.0.0-rc.1"}) Check(V(version).CompareTo(V("1.0.0"))<0,"prerelease below stable "+version);
        string[] ordered={"1.0.0-alpha","1.0.0-alpha.1","1.0.0-alpha.beta","1.0.0-beta","1.0.0-beta.2","1.0.0-beta.11","1.0.0-rc.1","1.0.0"};
        for(int i=1;i<ordered.Length;i++) Check(V(ordered[i-1]).CompareTo(V(ordered[i]))<0,"SemVer prerelease order");
        Check(V("1.8.0-beta.10").CompareTo(V("1.8.0-beta.5"))>0,"beta.10 greater than beta.5");
        Check(V("1.0.0-beta.9999999999999999999999").CompareTo(V("1.0.0-beta.9"))>0,"numeric prerelease avoids integer overflow");
        Check(!SimpleVersion.TryParse("1.0.0-beta.01",out _) && !SimpleVersion.TryParse("1.0.0-beta.",out _),"malformed prerelease rejected");
        Check(V("1.0.0+a").CompareTo(V("1.0.0+b"))==0,"build metadata has no priority");
        string json=JsonSerializer.Serialize(catalog);
        using(var doc=JsonDocument.Parse(json)) { var group=doc.RootElement.GetProperty("Modules")[0]; Check(!group.TryGetProperty("Version",out _),"no legacy top-level package fields"); Check(!group.GetProperty("Versions")[0].TryGetProperty("Id",out _),"ID not duplicated in each release"); }
        var roundtrip=JsonSerializer.Deserialize<OfficialPluginCatalog>(json)!;OfficialPluginClient.ValidateCatalog(roundtrip);
        Check(roundtrip.Modules[0].Versions.Count==2 && OfficialPluginVersionSelector.Select(roundtrip.Modules[0],high).Compatible!.Version=="1.1.0","cache retains full history and reselects after host upgrade");
        Reject(()=>OfficialPluginClient.ValidateCatalog(new OfficialPluginCatalog{SchemaVersion=1}));
        Reject(()=>OfficialPluginClient.ValidateCatalog(Catalog(Entry())));
        Reject(()=>OfficialPluginClient.ValidateCatalog(Catalog(Entry(old,old))));
        Reject(()=>OfficialPluginClient.ValidateCatalog(new OfficialPluginCatalog{SchemaVersion=2,Modules=new(){entry,entry}}));
        var unsafeUrl=Release("1.0.0","1.8.0-beta.1");unsafeUrl.PackageUrl="https://evil.test/Launch.spkg";Reject(()=>OfficialPluginClient.ValidateCatalog(Catalog(Entry(unsafeUrl))));
        var badHash=Release("1.0.0","1.8.0-beta.1");badHash.Sha256="bad";Reject(()=>OfficialPluginClient.ValidateCatalog(Catalog(Entry(badHash))));
        var badRange=Release("1.0.0","1.8.0-beta.5");badRange.MaxHostVersion="1.8.0-beta.1";Reject(()=>OfficialPluginClient.ValidateCatalog(Catalog(Entry(badRange))));
        var wrongTag=Release("1.0.0","1.8.0-beta.1");wrongTag.ReleaseTag="different";Reject(()=>OfficialPluginClient.ValidateCatalog(Catalog(Entry(wrongTag))));
        // 实际安装入口在创建临时目录/发请求之前拒绝不兼容版本。
        var future=Release("2.0.0","99.0.0");future.Id=entry.Id;
        var blocked=await OfficialPluginClient.InstallAsync(future);
        Check(!blocked.Success && blocked.PluginId==entry.Id,"incompatible install rejected before download");
        string cacheRoot=Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!,"catalog-cache");Directory.CreateDirectory(cacheRoot);
        PluginPaths.OverrideRootsForTesting(cacheRoot,Path.Combine(cacheRoot,"candidates"));
        File.WriteAllText(PluginPaths.OfficialCatalogCacheFile,JsonSerializer.Serialize(new OfficialPluginCatalogCache{Catalog=catalog}));
        Check(OfficialPluginClient.TryLoadCachedCatalog(out var cached,out _,out _) && cached.Modules[0].Versions.Count==2,"actual cache reader accepts v2 history");
        File.WriteAllText(PluginPaths.OfficialCatalogCacheFile,JsonSerializer.Serialize(new OfficialPluginCatalogCache{Catalog=new OfficialPluginCatalog{SchemaVersion=1}}));
        string before=File.ReadAllText(PluginPaths.OfficialCatalogCacheFile);
        Check(!OfficialPluginClient.TryLoadCachedCatalog(out _,out _,out _) && File.ReadAllText(PluginPaths.OfficialCatalogCacheFile)==before,"v1 cache rejected without deleting or rewriting");
        var nullApi=Release("1.0.0","1.8.0-beta.1");nullApi.ApiVersion=null!;Reject(()=>OfficialPluginClient.ValidateCatalog(Catalog(Entry(nullApi))));
        var nullAsset=Release("1.0.0","1.8.0-beta.1");nullAsset.AssetName=null!;Reject(()=>OfficialPluginClient.ValidateCatalog(Catalog(Entry(nullAsset))));
        var nullHash=Release("1.0.0","1.8.0-beta.1");nullHash.Sha256=null!;Reject(()=>OfficialPluginClient.ValidateCatalog(Catalog(Entry(nullHash))));
        Console.WriteLine($"PASS: {checks} catalog/compatibility/cache checks; no network or GUI.");
    }
}

