using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WinPieGestures;

internal sealed record UpdatePayloadFile(string RelativePath, long Length, string Sha256);

internal sealed record StagedUpdatePayload(
    string RootDirectory,
    string PayloadDirectory,
    string BackupDirectory,
    string ExeRelativePath,
    IReadOnlyList<UpdatePayloadFile> Files);

internal static class UpdatePayloadStager
{
    private const int MaxEntryCount = 4096;
    private const long MaxSingleFileBytes = 512L * 1024 * 1024;
    private const long MaxTotalPayloadBytes = 2L * 1024 * 1024 * 1024;
    private const int CopyBufferSize = 64 * 1024;
    private const string ExecutableName = "StarPie.exe";

    private static readonly HashSet<string> ForbiddenUserDataSegments =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "config.json",
            "plugin-data",
            "plugin",
            "scratch",
            "Configs",
            "plugins",
            "logs",
        };

    public static StagedUpdatePayload Stage(
        string archivePath,
        string applicationDirectory,
        string? stagingParent = null)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("Archive path is required.", nameof(archivePath));
        }

        if (string.IsNullOrWhiteSpace(applicationDirectory))
        {
            throw new ArgumentException("Application directory is required.", nameof(applicationDirectory));
        }

        string archiveFullPath = Path.GetFullPath(archivePath);
        if (!File.Exists(archiveFullPath))
        {
            throw new FileNotFoundException("Update archive was not found.", archiveFullPath);
        }

        string applicationFullPath = ValidateApplicationDirectory(applicationDirectory);
        string parentFullPath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(stagingParent)
                ? Path.Combine(Path.GetTempPath(), "StarPieUpdate")
                : stagingParent);

        EnsureDirectoryPathSafe(parentFullPath, allowMissing: true);

        string rootPath = Path.Combine(parentFullPath, Guid.NewGuid().ToString("N"));
        string payloadPath = Path.Combine(rootPath, "payload");
        string backupPath = Path.Combine(rootPath, "backup");

        if (File.Exists(rootPath) || Directory.Exists(rootPath))
        {
            throw new IOException("The generated staging root already exists.");
        }

        if (PathsOverlap(applicationFullPath, rootPath) ||
            PathsOverlap(applicationFullPath, payloadPath) ||
            PathsOverlap(applicationFullPath, backupPath))
        {
            throw new InvalidDataException(
                "Application directory must not overlap the staging root or payload.");
        }

        Directory.CreateDirectory(parentFullPath);
        EnsureDirectoryPathSafe(parentFullPath, allowMissing: false);

        bool rootOwned = true;
        try
        {
            Directory.CreateDirectory(rootPath);
            EnsureDirectoryPathSafe(rootPath, allowMissing: false);
            Directory.CreateDirectory(payloadPath);
            EnsureDirectoryPathSafe(payloadPath, allowMissing: false);
            Directory.CreateDirectory(backupPath);
            EnsureDirectoryPathSafe(backupPath, allowMissing: false);

            using FileStream archiveStream = new(
                archiveFullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using ZipArchive archive = new(archiveStream, ZipArchiveMode.Read, leaveOpen: false);

            if (archive.Entries.Count > MaxEntryCount)
            {
                throw new InvalidDataException(
                    $"Update archive contains more than {MaxEntryCount} entries.");
            }

            List<EntryInfo> entries = ReadEntryInfo(archive);
            ValidateEntries(entries);
            List<UpdatePayloadFile> files = ExtractEntries(entries, payloadPath);

            string? executablePath = files
                .Select(file => file.RelativePath)
                .FirstOrDefault(path => string.Equals(
                    path,
                    ExecutableName,
                    StringComparison.OrdinalIgnoreCase));
            if (executablePath == null)
            {
                throw new InvalidDataException(
                    $"Update archive must contain the root file {ExecutableName}.");
            }

            return new StagedUpdatePayload(
                rootPath,
                payloadPath,
                backupPath,
                executablePath,
                files.AsReadOnly());
        }
        catch
        {
            if (rootOwned)
            {
                TryDeleteOwnedRoot(rootPath, parentFullPath);
            }

            throw;
        }
    }

    internal static string ValidateApplicationDirectory(string applicationDirectory)
    {
        string fullPath = Path.GetFullPath(applicationDirectory);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"Application directory does not exist: {fullPath}");
        }

        string? pathRoot = Path.GetPathRoot(fullPath);
        if (pathRoot != null &&
            string.Equals(
                TrimEndingSeparator(pathRoot),
                TrimEndingSeparator(fullPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Application directory cannot be a filesystem root.");
        }

        EnsureDirectoryPathSafe(fullPath, allowMissing: false);
        string localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (string dataName in new[] { "StarPie", "WinPieGestures" })
        {
            if (!string.IsNullOrEmpty(localAppData) && PathsOverlap(fullPath, Path.Combine(localAppData, dataName)))
                throw new InvalidDataException("The installation directory overlaps the application data directory.");
        }
        return fullPath;
    }

    private static List<EntryInfo> ReadEntryInfo(ZipArchive archive)
    {
        var entries = new List<EntryInfo>(archive.Entries.Count);
        long totalBytes = 0;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.ExternalAttributes != 0 &&
                ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                 ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000))
            {
                throw new InvalidDataException(
                    $"Update archive contains a symbolic link or reparse entry: {entry.FullName}");
            }

            bool isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
            if (!isDirectory && entry.Name.Length == 0)
            {
                throw new InvalidDataException(
                    $"Update archive contains an entry with an empty file name: {entry.FullName}");
            }

            string relativePath = NormalizeEntryPath(entry.FullName, isDirectory);
            long length = isDirectory ? 0 : entry.Length;
            if (length < 0)
            {
                throw new InvalidDataException(
                    $"Update archive entry has an invalid length: {relativePath}");
            }

            if (!isDirectory)
            {
                if (length > MaxSingleFileBytes)
                {
                    throw new InvalidDataException(
                        $"Update archive entry exceeds the single-file limit: {relativePath}");
                }

                if (length > MaxTotalPayloadBytes - totalBytes)
                {
                    throw new InvalidDataException(
                        "Update archive exceeds the total payload size limit.");
                }

                totalBytes += length;
            }

            entries.Add(new EntryInfo(entry, relativePath, isDirectory, length));
        }

        return entries;
    }

    private static void ValidateEntries(List<EntryInfo> entries)
    {
        entries.Sort(static (left, right) =>
            string.Compare(
                left.RelativePath,
                right.RelativePath,
                StringComparison.OrdinalIgnoreCase));

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (EntryInfo entry in entries)
        {
            foreach (string parent in EnumerateParents(entry.RelativePath))
            {
                if (files.Contains(parent))
                {
                    throw new InvalidDataException(
                        $"Update archive contains a file/directory path conflict: {entry.RelativePath}");
                }
            }

            if (entry.IsDirectory)
            {
                if (files.Contains(entry.RelativePath))
                {
                    throw new InvalidDataException(
                        $"Update archive contains a file/directory path conflict: {entry.RelativePath}");
                }

                // Repeated directory entries are harmless; they cannot create a file.
                directories.Add(entry.RelativePath);
                continue;
            }

            if (files.Contains(entry.RelativePath) || directories.Contains(entry.RelativePath))
            {
                throw new InvalidDataException(
                    $"Update archive contains a duplicate or conflicting path: {entry.RelativePath}");
            }

            files.Add(entry.RelativePath);
        }
    }

    private static List<UpdatePayloadFile> ExtractEntries(
        List<EntryInfo> entries,
        string payloadPath)
    {
        var files = new List<UpdatePayloadFile>();

        foreach (EntryInfo entry in entries)
        {
            string targetPath = Path.Combine(
                payloadPath,
                entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(targetPath);
                EnsureDirectoryPathSafe(targetPath, allowMissing: false);
                continue;
            }

            string? parent = Path.GetDirectoryName(targetPath);
            if (parent != null)
            {
                Directory.CreateDirectory(parent);
                EnsureDirectoryPathSafe(parent, allowMissing: false);
            }

            using Stream input = entry.Entry.Open();
            using FileStream output = new(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.SequentialScan);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
            try
            {
                long written = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    written += read;
                    if (written > entry.Length ||
                        written > MaxSingleFileBytes)
                    {
                        throw new InvalidDataException(
                            $"Update archive entry exceeded its declared or allowed length: {entry.RelativePath}");
                    }

                    output.Write(buffer, 0, read);
                    hash.AppendData(buffer, 0, read);
                }

                if (written != entry.Length)
                {
                    throw new InvalidDataException(
                        $"Update archive entry length mismatch: {entry.RelativePath}");
                }

                files.Add(new UpdatePayloadFile(
                    entry.RelativePath,
                    written,
                    Convert.ToHexString(hash.GetHashAndReset())));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        return files;
    }

    private static string NormalizeEntryPath(string fullName, bool isDirectory)
    {
        if (string.IsNullOrEmpty(fullName))
        {
            throw new InvalidDataException("Update archive contains an empty entry path.");
        }

        if (fullName.IndexOf('\0') >= 0 ||
            fullName.IndexOf('\\') >= 0 ||
            fullName.IndexOf(':') >= 0 ||
            fullName.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Update archive contains an unsafe path: {fullName}");
        }

        string normalized = isDirectory
            ? fullName[..^1]
            : fullName;
        if (normalized.Length == 0)
        {
            throw new InvalidDataException("Update archive contains an empty entry path.");
        }

        string[] segments = normalized.Split('/');
        foreach (string segment in segments)
        {
            if (segment.Length == 0 ||
                segment == "." ||
                segment == "..")
            {
                throw new InvalidDataException(
                    $"Update archive contains an unsafe path: {fullName}");
            }

            if (segment.EndsWith(" ", StringComparison.Ordinal) ||
                segment.EndsWith(".", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Update archive contains a Windows-normalized path: {fullName}");
            }

            foreach (char character in segment)
            {
                if (character < 32 ||
                    character == 127 ||
                    "<>:\"\\|?*".IndexOf(character) >= 0)
                {
                    throw new InvalidDataException(
                        $"Update archive contains an invalid path character: {fullName}");
                }
            }

            if (ForbiddenUserDataSegments.Contains(segment))
            {
                throw new InvalidDataException(
                    $"Update archive contains a user-data path: {fullName}");
            }

            if (IsReservedDeviceName(segment))
            {
                throw new InvalidDataException(
                    $"Update archive contains a reserved device name: {fullName}");
            }
        }

        return string.Join("/", segments);
    }

    private static IEnumerable<string> EnumerateParents(string relativePath)
    {
        int index = 0;
        while ((index = relativePath.IndexOf('/', index)) >= 0)
        {
            yield return relativePath[..index];
            index++;
        }
    }

    private static bool IsReservedDeviceName(string segment)
    {
        int dot = segment.IndexOf('.');
        string stem = dot >= 0 ? segment[..dot] : segment;
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
               IsNumberedDevice(stem, "COM") ||
               IsNumberedDevice(stem, "LPT");
    }

    private static bool IsNumberedDevice(string stem, string prefix)
    {
        if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            stem.Length != prefix.Length + 1)
        {
            return false;
        }

        char number = stem[^1];
        return number >= '1' && number <= '9';
    }

    private static bool PathsOverlap(string left, string right)
    {
        string leftFull = TrimEndingSeparator(Path.GetFullPath(left));
        string rightFull = TrimEndingSeparator(Path.GetFullPath(right));
        if (string.Equals(leftFull, rightFull, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string leftPrefix = leftFull + Path.DirectorySeparatorChar;
        string rightPrefix = rightFull + Path.DirectorySeparatorChar;
        return leftFull.StartsWith(rightPrefix, StringComparison.OrdinalIgnoreCase) ||
               rightFull.StartsWith(leftPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureDirectoryPathSafe(string path, bool allowMissing)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        if (root == null)
        {
            throw new InvalidDataException($"Path has no filesystem root: {fullPath}");
        }

        string remainder = fullPath[root.Length..];
        string current = root;
        foreach (string segment in remainder.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current))
            {
                if (File.Exists(current))
                {
                    throw new InvalidDataException(
                        $"Path component is not a directory: {current}");
                }

                if (allowMissing)
                {
                    return;
                }

                throw new DirectoryNotFoundException(
                    $"Directory path component does not exist: {current}");
            }

            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"Directory path contains a reparse point: {current}");
            }
        }
    }

    private static void TryDeleteOwnedRoot(string rootPath, string parentPath)
    {
        try
        {
            string rootFull = Path.GetFullPath(rootPath);
            string parentFull = Path.GetFullPath(parentPath);
            string? actualParent = Path.GetDirectoryName(rootFull);
            string rootName = Path.GetFileName(rootFull);

            if (actualParent == null ||
                !string.Equals(
                    TrimEndingSeparator(actualParent),
                    TrimEndingSeparator(parentFull),
                    StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(rootName, "N", out _) ||
                !Directory.Exists(rootFull))
            {
                return;
            }

            FileAttributes parentAttributes = File.GetAttributes(parentFull);
            FileAttributes rootAttributes = File.GetAttributes(rootFull);
            if ((parentAttributes & FileAttributes.ReparsePoint) != 0 ||
                (rootAttributes & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            DeleteOwnedTree(rootFull);
        }
        catch
        {
            // Cleanup is best effort; never delete a path that cannot be proven owned.
        }
    }

    private static void DeleteOwnedTree(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Refusing to recurse through a reparse point.");
        }

        foreach (string child in Directory.EnumerateFileSystemEntries(path))
        {
            FileAttributes childAttributes = File.GetAttributes(child);
            if ((childAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Refusing to recurse through a reparse point.");
            }

            if ((childAttributes & FileAttributes.Directory) != 0)
            {
                DeleteOwnedTree(child);
            }
            else
            {
                File.Delete(child);
            }
        }

        Directory.Delete(path, recursive: false);
    }

    private static string TrimEndingSeparator(string path)
    {
        string trimmed = path.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? path : trimmed;
    }

    private sealed class EntryInfo
    {
        internal EntryInfo(
            ZipArchiveEntry entry,
            string relativePath,
            bool isDirectory,
            long length)
        {
            Entry = entry;
            RelativePath = relativePath;
            IsDirectory = isDirectory;
            Length = length;
        }

        internal ZipArchiveEntry Entry { get; }
        internal string RelativePath { get; }
        internal bool IsDirectory { get; }
        internal long Length { get; }
    }
}
