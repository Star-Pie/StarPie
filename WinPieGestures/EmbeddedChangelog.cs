using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace WinPieGestures;

internal sealed record ChangelogEntry(string Title, string Markdown);

internal static class EmbeddedChangelog
{
    internal const string ResourceName = "WinPieGestures.Changelog.md";
    internal const int MaximumCharacters = 1024 * 1024;

    internal static string? ReadText(Assembly assembly)
    {
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream == null) return null;
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var text = new StringBuilder();
        char[] buffer = new char[4096];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (text.Length + read > MaximumCharacters) throw new InvalidDataException("Embedded changelog exceeds its size limit.");
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }

    internal static IReadOnlyList<ChangelogEntry> Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return Array.Empty<ChangelogEntry>();
        if (markdown.Length > MaximumCharacters) throw new InvalidDataException("Changelog exceeds its size limit.");
        var entries = new List<ChangelogEntry>();
        using var reader = new StringReader(markdown);
        string? title = null;
        bool sawHeading = false;
        var body = new StringBuilder();
        char fence = '\0';
        int fenceLength = 0;
        void Finish()
        {
            string content = body.ToString().Trim('\r', '\n');
            if (title != null && !string.IsNullOrWhiteSpace(content)) entries.Add(new ChangelogEntry(title, content));
            body.Clear();
        }
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.TrimStart();
            var delimiter = Regex.Match(trimmed, @"^(`{3,}|~{3,})");
            if (delimiter.Success)
            {
                string marker = delimiter.Groups[1].Value;
                if (fence == '\0') { fence = marker[0]; fenceLength = marker.Length; }
                else if (marker[0] == fence && marker.Length >= fenceLength && string.IsNullOrWhiteSpace(trimmed[marker.Length..]))
                    fence = '\0';
            }
            if (fence == '\0')
            {
                var heading = Regex.Match(line, @"^##[ \t]+(.+?)[ \t]*$");
                if (heading.Success)
                {
                    sawHeading = true;
                    Finish();
                    title = Regex.Replace(heading.Groups[1].Value, @"^\[([^\]]+)\]", "$1");
                    continue;
                }
            }
            if (title != null) body.AppendLine(line);
        }
        Finish();
        // Preserve unsectioned documents as plain text instead of dropping their content.
        if (entries.Count == 0 && !sawHeading) entries.Add(new ChangelogEntry("CHANGELOG.md", markdown.Trim()));
        return entries;
    }

    internal static string ExportToTemporaryFile(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown) || markdown.Length > MaximumCharacters)
            throw new InvalidDataException("No valid embedded changelog to export.");
        string path = Path.Combine(Path.GetTempPath(), $"StarPie-CHANGELOG-{Guid.NewGuid():N}.md");
        // Random name plus CreateNew: never overwrite a pre-existing file or follow its link.
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(file, new UTF8Encoding(false));
        writer.Write(markdown);
        return path;
    }
}
