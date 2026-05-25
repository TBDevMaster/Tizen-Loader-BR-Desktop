using System.Net.Http;
using System.Net.Http.Headers;
using TizenLoaderBRDesktop.Helpers;

namespace TizenLoaderBRDesktop.Services;

public sealed class DownloadService
{
    private readonly HttpClient _httpClient = new();

    public async Task<string> DownloadAsync(string url, string downloadFolder, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(downloadFolder);

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var fileName = GetFileNameFromUrl(url, response.Content.Headers);
        var destinationPath = GetUniquePath(Path.Combine(downloadFolder, fileName));
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(destinationPath);

        var buffer = new byte[81920];
        var totalRead = 0L;
        var total = response.Content.Headers.ContentLength;

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            totalRead += read;

            if (total.HasValue && total.Value > 0)
            {
                var percent = totalRead * 100d / total.Value;
                progress?.Report($"{percent:0}% - {totalRead:n0}/{total.Value:n0} bytes");
            }
            else
            {
                progress?.Report($"{totalRead:n0} bytes baixados");
            }
        }

        progress?.Report($"Download concluído: {destinationPath}");
        return destinationPath;
    }

    private static string GetFileNameFromUrl(string url, HttpContentHeaders headers)
    {
        var uri = new Uri(url);
        var name = Path.GetFileName(uri.LocalPath);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var contentDisposition = headers.ContentDisposition;
        if (!string.IsNullOrWhiteSpace(contentDisposition?.FileNameStar))
        {
            return contentDisposition.FileNameStar.Trim('"');
        }

        if (!string.IsNullOrWhiteSpace(contentDisposition?.FileName))
        {
            return contentDisposition.FileName.Trim('"');
        }

        return "download.bin";
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var counter = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{fileName}_{counter}{extension}");
            counter++;
        }
        while (File.Exists(candidate));

        return candidate;
    }
}
