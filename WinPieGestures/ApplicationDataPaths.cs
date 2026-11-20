using System;
using System.IO;

namespace WinPieGestures;

internal static class ApplicationDataPaths
{
    internal const string PortableMarker = "StarPie.portable";
    internal const string PortableDataDirectory = "Data";
    private static string? _portableRoot;

    internal static bool IsPortable => _portableRoot != null;
    internal static string Root => _portableRoot ?? Path.Combine(LocalAppData, "StarPie");
    internal static string LocalAppData => GetUserFolder("LOCALAPPDATA", Environment.SpecialFolder.LocalApplicationData);
    internal static string ProgramCacheDirectory => _portableRoot
        ?? Path.Combine(GetUserFolder("APPDATA", Environment.SpecialFolder.ApplicationData), "StarPie");

    private static string GetUserFolder(string variable, Environment.SpecialFolder folder) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } path ? path : Environment.GetFolderPath(folder);

    // Select the data root once, before logging, configuration or the plugin host touches disk.
    internal static void Initialize(string? executableDirectory = null) =>
        _portableRoot = ResolvePortableRoot(executableDirectory ?? AppContext.BaseDirectory);

    internal static string? ResolvePortableRoot(string executableDirectory)
    {
        string marker = Path.Combine(executableDirectory, PortableMarker);
        if (ReadAttributes(marker) is not { } markerAttributes) return null;
        if ((markerAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException(marker);

        string root = Path.Combine(executableDirectory, PortableDataDirectory);
        Directory.CreateDirectory(root);
        VerifyWritableDirectory(root);
        foreach (string name in new[] { "Configs", "logs", "IconCache", "CustomIcons", "plugin-data" })
        {
            string directory = Path.Combine(root, name);
            if (ReadAttributes(directory).HasValue) VerifyWritableDirectory(directory);
        }

        foreach (string name in new[]
        {
            "config.json", "config.json.tmp", "config.json.bak", "program_cache.json",
            Path.Combine("logs", $"starpie_{DateTime.Now:yyyy-MM-dd}.log"),
            Path.Combine("plugin-data", "registry.json"), Path.Combine("plugin-data", "registry.json.tmp"),
            Path.Combine("plugin-data", "health.json"), Path.Combine("plugin-data", "health.json.tmp"),
            Path.Combine("plugin-data", "module-catalog.json"),
        })
            VerifyExistingFile(Path.Combine(root, name));

        string configs = Path.Combine(root, "Configs");
        if (Directory.Exists(configs))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(configs))
                if (entry.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                    entry.EndsWith(".json.tmp", StringComparison.OrdinalIgnoreCase))
                    VerifyExistingFile(entry);
        }

        string icons = Path.Combine(root, "CustomIcons");
        if (Directory.Exists(icons))
            foreach (string file in Directory.EnumerateFiles(icons)) VerifyExistingFile(file);
        return root;
    }

    // Unknown cache filenames are checked on demand, to keep large caches off the startup path.
    internal static void CheckPortableFile(string path)
    {
        if (_portableRoot == null) return;
        string full = Path.GetFullPath(path);
        string root = Path.TrimEndingDirectorySeparator(_portableRoot);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException(path);
        for (string? parent = Path.GetDirectoryName(full); parent != null; parent = Path.GetDirectoryName(parent))
        {
            if (ReadAttributes(parent) is { } attributes &&
                (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                throw new IOException(parent);
            if (string.Equals(parent, root, StringComparison.OrdinalIgnoreCase)) break;
        }
        VerifyExistingFile(full);
    }

    private static void VerifyExistingFile(string file)
    {
        if (ReadAttributes(file) is not { } attributes) return;
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0)
            throw new UnauthorizedAccessException(file);
        using var existing = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
    }

    private static FileAttributes? ReadAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static void VerifyWritableDirectory(string directory)
    {
        var attributes = File.GetAttributes(directory);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
            throw new IOException(directory);

        string probe = Path.Combine(directory, $".starpie-write-{Guid.NewGuid():N}.tmp");
        using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        stream.WriteByte(0);
    }
}
