using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace WinPieGestures.Plugins;

/// <summary>进程包的完整文件清单校验；程序集包继续使用既有单程序集契约。</summary>
internal static class PluginPackageIntegrity
{
    internal static void VerifyFiles(string packageRoot, JsonElement manifest)
    {
        if (!manifest.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array ||
            files.GetArrayLength() == 0 || files.GetArrayLength() > 512)
            throw Invalid("files");

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        string prefix = root + Path.DirectorySeparatorChar;
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement file in files.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object || !file.TryGetProperty("path", out JsonElement pathElement) ||
                pathElement.ValueKind != JsonValueKind.String)
                throw Invalid("path");
            string relative = pathElement.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
                throw Invalid(relative);

            string fullPath;
            try { fullPath = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { throw Invalid(relative); }
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fullPath, Path.Combine(root, "module.manifest.json"), StringComparison.OrdinalIgnoreCase) ||
                !declared.Add(fullPath) || !File.Exists(fullPath))
                throw Invalid(relative);
            if (!file.TryGetProperty("size", out JsonElement sizeElement) || sizeElement.ValueKind != JsonValueKind.Number || !sizeElement.TryGetInt64(out long size) ||
                size < 0 || new FileInfo(fullPath).Length != size ||
                !file.TryGetProperty("sha256", out JsonElement hashElement) || hashElement.ValueKind != JsonValueKind.String)
                throw Invalid(relative);
            string expectedHash = hashElement.GetString() ?? "";
            if (expectedHash.Length != 64 || !System.Linq.Enumerable.All(expectedHash, Uri.IsHexDigit)) throw Invalid(relative);
            using FileStream stream = File.OpenRead(fullPath);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw Invalid(relative);
        }

        if (!declared.Contains(Path.Combine(root, "plugin.json"))) throw Invalid("plugin.json");
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(file, Path.Combine(root, "module.manifest.json"), StringComparison.OrdinalIgnoreCase)) continue;
            if (!declared.Contains(file)) throw Invalid(Path.GetRelativePath(root, file));
        }
    }

    private static InvalidDataException Invalid(string path) => new(I18n.TF("PluginsPackageFilesInvalid", path));
}
