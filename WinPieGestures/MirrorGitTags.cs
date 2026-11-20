using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;

namespace WinPieGestures;

internal static class MirrorGitTags
{
    // Read only the public reference advertisement. No pack, objects or checkout are requested.
    internal static async Task<IReadOnlyList<string>> ReadAsync(
        Func<string, CancellationToken, Task<HttpResponseMessage>> send, string repositoryUrl, CancellationToken token)
    {
        using var response = await send(repositoryUrl.TrimEnd('/') + ".git/info/refs?service=git-upload-pack", token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType != "application/x-git-upload-pack-advertisement")
            throw new InvalidDataException("Expected a Git reference advertisement.");
        using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > 2 * 1024 * 1024) throw new InvalidDataException("Git reference list too large.");
            buffer.Write(chunk, 0, read);
        }
        return Parse(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
    }

    private static IReadOnlyList<string> Parse(ReadOnlySpan<byte> data)
    {
        var tags = new List<string>();
        while (!data.IsEmpty)
        {
            if (data.Length < 4 || !int.TryParse(data[..4], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int length)
                || length > data.Length || length is > 0 and < 4)
                throw new InvalidDataException("Malformed Git reference advertisement.");
            if (length == 0) { data = data[4..]; continue; }
            string line = Encoding.UTF8.GetString(data[4..length]).TrimEnd('\n');
            data = data[length..];
            int capability = line.IndexOf('\0');
            if (capability >= 0) line = line[..capability];
            int delimiter = line.IndexOf(' ');
            if (delimiter < 0) continue;
            string reference = line[(delimiter + 1)..];
            if (reference.StartsWith("refs/tags/", StringComparison.Ordinal) && !reference.EndsWith("^{}", StringComparison.Ordinal))
                tags.Add(reference["refs/tags/".Length..]);
        }
        return tags;
    }
}
