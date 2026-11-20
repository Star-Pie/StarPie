using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Xml;

namespace WinPieGestures;

internal sealed record SourceMeasurement(string Source, long? Milliseconds, string? Failure = null, int? Status = null);
internal sealed record SourceResponse<T>(SourceMeasurement Measurement, T? Value) where T : class;

internal static class MirrorSourceProbe
{
    internal static Task<SourceResponse<T>[]> CompareAsync<T>(
        string first, string second, Func<string, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken, TimeSpan? timeout = null) where T : class
        => CompareAsync(new[] { first, second }, read, cancellationToken, timeout);

    internal static async Task<SourceResponse<T>[]> CompareAsync<T>(
        IReadOnlyList<string> sources, Func<string, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken, TimeSpan? timeout = null) where T : class
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
        var responses = await Task.WhenAll(sources.Select(MeasureAsync)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return responses;

        async Task<SourceResponse<T>> MeasureAsync(string source)
        {
            var clock = Stopwatch.StartNew();
            try
            {
                var value = await read(source, budget.Token).ConfigureAwait(false);
                budget.Token.ThrowIfCancellationRequested();
                return new(new(source, clock.ElapsedMilliseconds), value);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
                or JsonException or XmlException or TimeoutException or OperationCanceledException or FormatException
                or System.Text.DecoderFallbackException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var failure = ex switch
                {
                    TimeoutException or OperationCanceledException => "SourceProbeTimeout",
                    HttpRequestException { StatusCode: not null } => "SourceProbeHttp",
                    HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => "SourceProbeTls",
                    HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => "SourceProbeDns",
                    HttpRequestException or IOException => "SourceProbeNetwork",
                    _ => "SourceProbeInvalid"
                };
                var status = (int?)(ex as HttpRequestException)?.StatusCode;
                AppLogger.LogWarn($"[source probe] {source}: {failure} {status}");
                return new(new(source, null, failure, status), null);
            }
        }
    }
}
