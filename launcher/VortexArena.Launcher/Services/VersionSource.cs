using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace VortexArena.Launcher.Services;

/// <summary>One APK on the update server.</summary>
/// <param name="Version">Version number, also the package suffix.</param>
/// <param name="File">File name as published (<c>game_v&lt;N&gt;.apk</c>).</param>
/// <param name="Size">Byte length; 0 when the server did not report it.</param>
/// <param name="Modified">Publish time as the server formatted it; shown verbatim.</param>
public sealed record RemoteVersion(int Version, string File, long Size, string Modified);

public sealed record DownloadProgress(long Received, long Total);

/// <summary>
/// Where the APK list comes from.
/// <para>⚠️ An interface on purpose: the licence backend is expected to replace these open HTTP
/// endpoints, and nothing above this layer should have to change.</para>
/// </summary>
public interface IVersionSource
{
    Task<IReadOnlyList<RemoteVersion>> ListAsync(CancellationToken ct);

    Task DownloadAsync(RemoteVersion version, string targetPath,
        IProgress<DownloadProgress>? progress, CancellationToken ct);
}

/// <summary>Plain-HTTP update server: a JSON listing plus static file downloads.</summary>
public sealed class HttpVersionSource : IVersionSource, IDisposable
{
    /// <summary>Suffix of the in-progress file; never shown as an installed version.</summary>
    public const string PartialExtension = ".part";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly Func<string> _listUrl;
    private readonly Func<string> _downloadBaseUrl;

    public HttpVersionSource(Func<string> listUrl, Func<string> downloadBaseUrl)
    {
        _listUrl = listUrl;
        _downloadBaseUrl = downloadBaseUrl;
    }

    public async Task<IReadOnlyList<RemoteVersion>> ListAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));

        var json = await _http.GetStringAsync(_listUrl(), timeout.Token).ConfigureAwait(false);
        return ParseList(json);
    }

    /// <summary>
    /// Downloads to <c>&lt;target&gt;.part</c> and renames only after the length matches.
    /// <para>⚠️ A half-written <c>game_v&lt;N&gt;.apk</c> would look installable and adb would fail
    /// with a parse error the operator cannot read — hence the two-step write.</para>
    /// </summary>
    public async Task DownloadAsync(RemoteVersion version, string targetPath,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var partial = targetPath + PartialExtension;
        var url = _downloadBaseUrl().TrimEnd('/') + "/" + version.File;

        try
        {
            using var response = await _http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? version.Size;
            progress?.Report(new DownloadProgress(0, total));

            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write,
                             FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    received += read;
                    progress?.Report(new DownloadProgress(received, total));
                }

                if (total > 0 && received != total)
                    throw new IOException($"İndirme yarım kaldı: {received}/{total} bayt.");
            }

            File.Move(partial, targetPath, overwrite: true);
        }
        catch (Exception)
        {
            TryDelete(partial);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // Leftover .part is harmless; it is never listed.
        }
    }

    /// <summary>
    /// Parses the listing: <c>{ "count": N, "versions": [ { version, file, size, modified } ] }</c>.
    /// Unparsable entries are dropped rather than failing the whole list.
    /// </summary>
    public static IReadOnlyList<RemoteVersion> ParseList(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("versions", out var versions) ||
            versions.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Sürüm listesinde 'versions' dizisi yok.");
        }

        var parsed = new List<RemoteVersion>();
        foreach (var entry in versions.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;

            if (!entry.TryGetProperty("version", out var versionField) ||
                versionField.ValueKind != JsonValueKind.Number ||
                !versionField.TryGetInt32(out var number))
            {
                continue;
            }

            var file = entry.TryGetProperty("file", out var fileField) &&
                       fileField.ValueKind == JsonValueKind.String
                ? fileField.GetString() ?? ""
                : "";
            if (file.Length == 0) file = GamePackage.FileNameFor(number);

            long size = entry.TryGetProperty("size", out var sizeField) &&
                        sizeField.ValueKind == JsonValueKind.Number &&
                        sizeField.TryGetInt64(out var sizeValue)
                ? sizeValue
                : 0;

            var modified = entry.TryGetProperty("modified", out var modifiedField) &&
                           modifiedField.ValueKind == JsonValueKind.String
                ? modifiedField.GetString() ?? ""
                : "";

            parsed.Add(new RemoteVersion(number, file, size, modified));
        }

        return parsed.OrderByDescending(v => v.Version).ToArray();
    }

    public void Dispose() => _http.Dispose();
}
