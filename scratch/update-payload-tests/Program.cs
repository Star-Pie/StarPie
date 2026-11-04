using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WinPieGestures;

internal static class Program
{
    private static int _checks;

    private static int Main(string[] args)
    {
        string testRoot = Path.Combine(
            AppContext.BaseDirectory,
            "test-data",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);

        try
        {
            string? candidate = typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .Cast<System.Reflection.AssemblyMetadataAttribute>().SingleOrDefault(value => value.Key == "CandidateDll")?.Value;
            if (candidate != null)
            {
                using FileStream expected = File.OpenRead(candidate);
                using FileStream loaded = File.OpenRead(typeof(UpdatePayloadStager).Assembly.Location);
                Check(Convert.ToHexString(SHA256.HashData(expected)) == Convert.ToHexString(SHA256.HashData(loaded)),
                    "actual stager is loaded from the explicit candidate DLL");
                Console.WriteLine("CANDIDATE: " + candidate);
            }
            RunValidPayloadTest(testRoot);
            RunInvalidPathTests(testRoot);
            RunDuplicateAndConflictTests(testRoot);
            RunArchiveFailureTests(testRoot);
            RunApplicationDirectoryTests(testRoot);
            RunUserDataBoundaryTests(testRoot);
            foreach (string realArchive in args)
            {
                string target = NewDirectory(testRoot, "real-package-" + Guid.NewGuid().ToString("N"));
                StagedUpdatePayload staged = UpdatePayloadStager.Stage(realArchive, target,
                    Path.Combine(testRoot, "real-package-staging"));
                Check(staged.Files.Any(file => file.RelativePath == "StarPie.exe"), "real release package retains its root executable");
                foreach (UpdatePayloadFile file in staged.Files)
                {
                    using FileStream extracted = File.OpenRead(Path.Combine(staged.PayloadDirectory, file.RelativePath));
                    Check(Convert.ToHexString(SHA256.HashData(extracted)) == file.Sha256, "real package staged hash matches manifest");
                }
                TryDeleteDirectory(staged.RootDirectory);
            }

            Console.WriteLine($"RESULT passed={_checks} failed=0");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL after {_checks} checks: {ex}");
            return 1;
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    private static void RunUserDataBoundaryTests(string testRoot)
    {
        string owned = NewDirectory(testRoot, "data-boundary");
        string local = NewDirectory(owned, "local");
        string applicationData = NewDirectory(local, "StarPie");
        string configs = NewDirectory(applicationData, "Configs");
        string configuration = Path.Combine(configs, "daily.json");
        File.WriteAllText(configuration, "ORIGINAL CONFIGURATION");
        string archive = Path.Combine(owned, "payload.zip");
        CreateZip(archive, Entry("StarPie.exe", Encoding.UTF8.GetBytes("fake exe")),
            Entry("Configs/daily.json", Encoding.UTF8.GetBytes("unwanted replacement")));
        string? prior = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", local);
        try
        {
            ExpectFailure("installation is data root", () => UpdatePayloadStager.Stage(archive, applicationData, Path.Combine(owned, "staging")));
            ExpectFailure("installation is data ancestor", () => UpdatePayloadStager.Stage(archive, local, Path.Combine(owned, "staging")));
            string nested = NewDirectory(applicationData, "program");
            ExpectFailure("installation is inside data root", () => UpdatePayloadStager.Stage(archive, nested, Path.Combine(owned, "staging")));
            string independent = NewDirectory(owned, "program");
            ExpectFailure("archive cannot contain configuration schemes", () => UpdatePayloadStager.Stage(archive, independent, Path.Combine(owned, "staging")));
            Check(File.ReadAllText(configuration) == "ORIGINAL CONFIGURATION", "data overlap cases preserve all scheme bytes");
        }
        finally { Environment.SetEnvironmentVariable("LOCALAPPDATA", prior); }
    }

    private static void RunValidPayloadTest(string testRoot)
    {
        string caseRoot = NewDirectory(testRoot, "valid");
        string archive = Path.Combine(caseRoot, "payload.zip");
        string staging = Path.Combine(caseRoot, "staging");
        string target = NewDirectory(caseRoot, "target");
        string sentinel = Path.Combine(target, "keep.txt");
        File.WriteAllText(sentinel, "target-must-not-change");

        CreateZip(
            archive,
            Entry("StarPie.exe", Encoding.UTF8.GetBytes("exe-binary")),
            Entry("lib/dependency.dll", Encoding.UTF8.GetBytes("dependency")),
            Entry("icons/app.ico", Encoding.UTF8.GetBytes("icon")),
            Entry("empty/"),
            Entry("empty/readme.txt", Encoding.UTF8.GetBytes("readme")),
            Entry("duplicate-dir/"),
            Entry("duplicate-dir/"));

        StagedUpdatePayload staged = UpdatePayloadStager.Stage(archive, target, staging);

        Check(Directory.Exists(staged.RootDirectory), "root directory exists");
        Check(Directory.Exists(staged.PayloadDirectory), "payload directory exists");
        Check(Directory.Exists(staged.BackupDirectory), "backup directory exists");
        Check(staged.ExeRelativePath == "StarPie.exe", "root executable is recorded");
        Check(staged.Files.Count == 4, "only files are listed");
        Check(!Directory.EnumerateFileSystemEntries(staged.BackupDirectory).Any(), "backup remains empty");
        Check(File.ReadAllText(sentinel) == "target-must-not-change", "target is not modified");

        UpdatePayloadFile exe = staged.Files.Single(file => file.RelativePath == "StarPie.exe");
        Check(exe.Length == Encoding.UTF8.GetByteCount("exe-binary"), "executable length is actual bytes");
        Check(
            exe.Sha256 == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("exe-binary"))),
            "executable hash is SHA-256");
        Check(
            File.ReadAllText(Path.Combine(staged.PayloadDirectory, "empty", "readme.txt")) == "readme",
            "nested payload file is extracted");

        TryDeleteDirectory(staged.RootDirectory);
    }

    private static void RunInvalidPathTests(string testRoot)
    {
        string target = NewDirectory(testRoot, "invalid-target");
        string sentinel = Path.Combine(target, "keep.txt");
        File.WriteAllText(sentinel, "unchanged");

        foreach (string badPath in new[]
                 {
                     "../escape.txt",
                     "/absolute.txt",
                     "C:/drive.txt",
                     "bad\\path.txt",
                     "file.txt:stream",
                     "CON",
                     "folder/CON.txt",
                     "plugin-data/private.json",
                     "config.json",
                     "a//b.txt",
                 })
        {
            string caseRoot = NewDirectory(testRoot, "invalid-" + Guid.NewGuid().ToString("N"));
            string archive = Path.Combine(caseRoot, "payload.zip");
            string staging = Path.Combine(caseRoot, "staging");
            CreateZip(
                archive,
                Entry("StarPie.exe", Encoding.UTF8.GetBytes("exe")),
                Entry(badPath, Encoding.UTF8.GetBytes("bad")));

            ExpectFailure(badPath, () => UpdatePayloadStager.Stage(archive, target, staging));
            Check(File.ReadAllText(sentinel) == "unchanged", "invalid path leaves target unchanged: " + badPath);
            CheckNoStagingRoots(staging, "invalid path cleans staging root: " + badPath);
            TryDeleteDirectory(caseRoot);
        }
    }

    private static void RunDuplicateAndConflictTests(string testRoot)
    {
        string target = NewDirectory(testRoot, "conflict-target");
        string sentinel = Path.Combine(target, "keep.txt");
        File.WriteAllText(sentinel, "unchanged");

        RunConflictCase(
            testRoot,
            target,
            "case-duplicate",
            Entry("StarPie.exe", Encoding.UTF8.GetBytes("exe")),
            Entry("Foo.dll", Encoding.UTF8.GetBytes("one")),
            Entry("foo.DLL", Encoding.UTF8.GetBytes("two")));

        RunConflictCase(
            testRoot,
            target,
            "file-directory-conflict",
            Entry("StarPie.exe", Encoding.UTF8.GetBytes("exe")),
            Entry("a", Encoding.UTF8.GetBytes("file")),
            Entry("a/b.txt", Encoding.UTF8.GetBytes("child")));

        RunConflictCase(
            testRoot,
            target,
            "same-path-file-directory",
            Entry("StarPie.exe", Encoding.UTF8.GetBytes("exe")),
            Entry("a", Encoding.UTF8.GetBytes("file")),
            Entry("a/"));

        Check(File.ReadAllText(sentinel) == "unchanged", "conflict cases leave target unchanged");
    }

    private static void RunArchiveFailureTests(string testRoot)
    {
        string target = NewDirectory(testRoot, "archive-target");
        string sentinel = Path.Combine(target, "keep.txt");
        File.WriteAllText(sentinel, "unchanged");

        RunConflictCase(
            testRoot,
            target,
            "missing-exe",
            Entry("lib/dependency.dll", Encoding.UTF8.GetBytes("dependency")));

        RunConflictCase(
            testRoot,
            target,
            "forbidden-user-data",
            Entry("StarPie.exe", Encoding.UTF8.GetBytes("exe")),
            Entry("scratch/private.txt", Encoding.UTF8.GetBytes("private")));

        string symlinkRoot = NewDirectory(testRoot, "symlink");
        string symlinkArchive = Path.Combine(symlinkRoot, "payload.zip");
        CreateZip(
            symlinkArchive,
            Entry("StarPie.exe", Encoding.UTF8.GetBytes("exe")),
            SymlinkEntry("link"));
        ExpectFailure("symlink", () => UpdatePayloadStager.Stage(
            symlinkArchive,
            target,
            Path.Combine(symlinkRoot, "staging")));
        CheckNoStagingRoots(Path.Combine(symlinkRoot, "staging"), "symlink cleans staging root");
        TryDeleteDirectory(symlinkRoot);

        string reparseRoot = NewDirectory(testRoot, "reparse");
        string reparseArchive = Path.Combine(reparseRoot, "payload.zip");
        CreateZip(
            reparseArchive,
            Entry("StarPie.exe", Encoding.UTF8.GetBytes("exe")),
            ReparseEntry("reparse"));
        ExpectFailure("reparse", () => UpdatePayloadStager.Stage(
            reparseArchive,
            target,
            Path.Combine(reparseRoot, "staging")));
        CheckNoStagingRoots(Path.Combine(reparseRoot, "staging"), "reparse cleans staging root");
        TryDeleteDirectory(reparseRoot);

        string damagedRoot = NewDirectory(testRoot, "damaged");
        string damagedArchive = Path.Combine(damagedRoot, "payload.zip");
        File.WriteAllBytes(damagedArchive, Encoding.UTF8.GetBytes("not a zip archive"));
        ExpectFailure("damaged archive", () => UpdatePayloadStager.Stage(
            damagedArchive,
            target,
            Path.Combine(damagedRoot, "staging")));
        CheckNoStagingRoots(Path.Combine(damagedRoot, "staging"), "damaged archive cleans staging root");
        TryDeleteDirectory(damagedRoot);

        string manyRoot = NewDirectory(testRoot, "many-entries");
        string manyArchive = Path.Combine(manyRoot, "payload.zip");
        CreateZip(
            manyArchive,
            Enumerable.Range(0, 4097)
                .Select(index => Entry($"file-{index}.bin", Array.Empty<byte>()))
                .Prepend(Entry("StarPie.exe", Encoding.UTF8.GetBytes("exe")))
                .ToArray());
        ExpectFailure("entry limit", () => UpdatePayloadStager.Stage(
            manyArchive,
            target,
            Path.Combine(manyRoot, "staging")));
        CheckNoStagingRoots(Path.Combine(manyRoot, "staging"), "entry limit cleans staging root");
        TryDeleteDirectory(manyRoot);

        Check(File.ReadAllText(sentinel) == "unchanged", "archive failures leave target unchanged");
    }

    private static void RunApplicationDirectoryTests(string testRoot)
    {
        string caseRoot = NewDirectory(testRoot, "application");
        string archive = Path.Combine(caseRoot, "payload.zip");
        CreateZip(archive, Entry("StarPie.exe", Encoding.UTF8.GetBytes("exe")));

        string target = NewDirectory(caseRoot, "target");
        string nestedStaging = Path.Combine(target, "nested-staging");
        ExpectFailure("target overlap", () => UpdatePayloadStager.Stage(
            archive,
            target,
            nestedStaging));
        Check(!Directory.Exists(nestedStaging), "overlap check does not create target contents");

        string? root = Path.GetPathRoot(target);
        Check(root != null, "test target has a filesystem root");
        if (root != null)
        {
            ExpectFailure("filesystem root", () => UpdatePayloadStager.Stage(
                archive,
                root,
                Path.Combine(caseRoot, "staging")));
        }

        TryDeleteDirectory(caseRoot);
    }

    private static void RunConflictCase(
        string testRoot,
        string target,
        string name,
        params ZipEntryData[] entries)
    {
        string caseRoot = NewDirectory(testRoot, name + "-" + Guid.NewGuid().ToString("N"));
        string archive = Path.Combine(caseRoot, "payload.zip");
        CreateZip(archive, entries);
        ExpectFailure(name, () => UpdatePayloadStager.Stage(
            archive,
            target,
            Path.Combine(caseRoot, "staging")));
        CheckNoStagingRoots(Path.Combine(caseRoot, "staging"), name + " cleans staging root");
        TryDeleteDirectory(caseRoot);
    }

    private static void CheckNoStagingRoots(string staging, string message)
    {
        if (!Directory.Exists(staging))
        {
            _checks++;
            return;
        }

        Check(!Directory.EnumerateDirectories(staging).Any(), message);
    }

    private static void ExpectFailure(string name, Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected failure did not occur: " + name);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception)
        {
            _checks++;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Check failed: " + message);
        }

        _checks++;
    }

    private static string NewDirectory(string parent, string name)
    {
        string path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static ZipEntryData Entry(string name, byte[]? content = null)
        => new(name, content, false);

    private static ZipEntryData SymlinkEntry(string name)
        => new(name, Encoding.UTF8.GetBytes("target"), true);

    private static ZipEntryData ReparseEntry(string name)
        => new(name, Encoding.UTF8.GetBytes("target"), false, true);

    private static void CreateZip(string path, params ZipEntryData[] entries)
    {
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        foreach (ZipEntryData entryData in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(
                entryData.Name,
                CompressionLevel.NoCompression);
            if (entryData.Symlink)
            {
                entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            }
            else if (entryData.Reparse)
            {
                entry.ExternalAttributes = (int)FileAttributes.ReparsePoint;
            }

            if (entryData.Content != null)
            {
                using Stream entryStream = entry.Open();
                entryStream.Write(entryData.Content, 0, entryData.Content.Length);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed record ZipEntryData(
        string Name,
        byte[]? Content,
        bool Symlink,
        bool Reparse = false);
}
