using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using TizenLoaderBRDesktop.Models;

namespace TizenLoaderBRDesktop.Services;

public sealed class PackageAnalyzerService
{
    private static readonly string[] KeywordSet =
    {
        "webview",
        "url",
        "http",
        "https",
        "json",
        "rss",
        "feed",
        "sync",
        "server",
        "browser"
    };

    public async Task<PackageAnalysisResult> AnalyzeAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        var result = new PackageAnalysisResult
        {
            DetectedType = DetectKind(packagePath)
        };

        if (!File.Exists(packagePath))
        {
            result.Warnings.Add("Arquivo não encontrado.");
            return result;
        }

        result.Sha256 = await ComputeSha256Async(packagePath, cancellationToken).ConfigureAwait(false);

        if (result.DetectedType != TizenPackageKind.Unknown)
        {
            await AnalyzeArchiveAsync(packagePath, result, cancellationToken).ConfigureAwait(false);
        }

        ApplyHeuristics(result);
        return result;
    }

    private static TizenPackageKind DetectKind(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".wgt")
        {
            return TizenPackageKind.Wgt;
        }

        if (extension == ".tpk")
        {
            return TizenPackageKind.Tpk;
        }

        if (extension == ".zip")
        {
            return TizenPackageKind.Zip;
        }

        return TizenPackageKind.Unknown;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task AnalyzeArchiveAsync(string packagePath, PackageAnalysisResult result, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var textContent = new StringBuilder();

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entryName = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (entryName.EndsWith("author-signature.xml", StringComparison.OrdinalIgnoreCase) ||
                entryName.EndsWith("signature1.xml", StringComparison.OrdinalIgnoreCase))
            {
                result.SignatureFound = true;
                result.SignatureFiles.Add(entryName);
            }

            if (IsTextCandidate(entry.Name))
            {
                var content = await ReadEntryTextAsync(entry, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    textContent.AppendLine(content);
                    AnalyzeXmlContent(entryName, content, result);
                }
            }
        }

        var allText = textContent.ToString();
        CollectKeywords(allText, result);
    }

    private static async Task<string> ReadEntryTextAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var buffer = new char[8192];
        var builder = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
            if (builder.Length > 1024 * 1024)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static bool IsTextCandidate(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".xml"
            || extension == ".html"
            || extension == ".htm"
            || extension == ".js"
            || extension == ".json"
            || extension == ".css"
            || extension == ".txt"
            || extension == ".svg"
            || fileName.Equals("config.xml", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("tizen-manifest.xml", StringComparison.OrdinalIgnoreCase);
    }

    private static void AnalyzeXmlContent(string entryName, string xmlContent, PackageAnalysisResult result)
    {
        try
        {
            var document = XDocument.Parse(xmlContent, LoadOptions.PreserveWhitespace);
            var root = document.Root;
            if (root is null)
            {
                return;
            }

            if (entryName.EndsWith("config.xml", StringComparison.OrdinalIgnoreCase) || root.Name.LocalName.Equals("widget", StringComparison.OrdinalIgnoreCase))
            {
                result.HasConfigXml = true;
                result.PackageId = GetAttribute(root, "id", "package", "package-id") ?? result.PackageId;
                result.Version = GetAttribute(root, "version") ?? result.Version;
                result.Name = GetFirstValue(document, "name", "label") ?? result.Name;
            }

            if (entryName.EndsWith("tizen-manifest.xml", StringComparison.OrdinalIgnoreCase) || root.Name.LocalName.Contains("manifest", StringComparison.OrdinalIgnoreCase))
            {
                result.HasManifestXml = true;
                result.PackageId = GetAttribute(root, "package", "id") ?? result.PackageId;
                result.Version = GetAttribute(root, "version") ?? result.Version;
                result.AppId = GetAttribute(root, "appid", "id") ?? result.AppId;
                result.Name = GetFirstValue(document, "label", "name") ?? result.Name;
                result.Permissions.AddRange(GetAttributes(document, "privilege", "name"));
                result.Permissions.AddRange(GetAttributes(document, "permission", "name"));
            }

            result.Permissions = result.Permissions
                .Where(permission => !string.IsNullOrWhiteSpace(permission))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            result.Notes.Add($"Falha ao ler XML: {entryName}");
        }
    }

    private static string? GetAttribute(XElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var attribute = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(attribute?.Value))
            {
                return attribute.Value;
            }
        }

        return null;
    }

    private static string? GetFirstValue(XDocument document, params string[] elementNames)
    {
        foreach (var elementName in elementNames)
        {
            var value = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static IEnumerable<string> GetAttributes(XDocument document, string elementName, string attributeName)
    {
        return document.Descendants()
            .Where(element => element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase))?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))!
            .Select(value => value!.Trim());
    }

    private static void CollectKeywords(string text, PackageAnalysisResult result)
    {
        foreach (var keyword in KeywordSet)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                result.KeywordsFound.Add(keyword);
            }
        }

        result.KeywordsFound = result.KeywordsFound
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ApplyHeuristics(PackageAnalysisResult result)
    {
        var signatureLabel = result.SignatureFound ? "Assinatura encontrada" : "Assinatura ausente";
        result.Warnings.Add(signatureLabel);

        if (!result.SignatureFound)
        {
            result.Warnings.Add("Pode falhar por certificado");
        }

        if (result.DetectedType is TizenPackageKind.Wgt or TizenPackageKind.Tpk)
        {
            result.ProbablyInstallable = result.HasConfigXml || result.HasManifestXml || !string.IsNullOrWhiteSpace(result.PackageId);
        }

        var keywordText = string.Join(' ', result.KeywordsFound);
        result.UsesInternet = keywordText.Contains("http", StringComparison.OrdinalIgnoreCase)
            || keywordText.Contains("https", StringComparison.OrdinalIgnoreCase)
            || keywordText.Contains("url", StringComparison.OrdinalIgnoreCase)
            || keywordText.Contains("browser", StringComparison.OrdinalIgnoreCase)
            || result.Permissions.Any(permission => permission.Contains("internet", StringComparison.OrdinalIgnoreCase) || permission.Contains("network", StringComparison.OrdinalIgnoreCase));

        var heuristicsText = $"{result.PackageId} {result.AppId} {result.Name} {keywordText}".ToLowerInvariant();
        result.IsWatchfaceCandidate = heuristicsText.Contains("watchface")
            || heuristicsText.Contains("watch face")
            || heuristicsText.Contains("clock")
            || heuristicsText.Contains("circular")
            || heuristicsText.Contains("face");

        result.IsShellCandidate = heuristicsText.Contains("webview")
            || heuristicsText.Contains("browser")
            || heuristicsText.Contains("http")
            || heuristicsText.Contains("https")
            || heuristicsText.Contains("url")
            || heuristicsText.Contains("rss")
            || heuristicsText.Contains("feed")
            || heuristicsText.Contains("sync")
            || heuristicsText.Contains("server");

        if (result.ProbablyInstallable)
        {
            result.Warnings.Add("Provavelmente instalável");
        }

        if (result.IsWatchfaceCandidate)
        {
            result.Warnings.Add("Candidato a watchface");
        }

        if (result.IsShellCandidate)
        {
            result.Warnings.Add("Candidato a casca");
        }

        if (result.UsesInternet)
        {
            result.Notes.Add("Usa internet ou recursos de rede.");
        }
    }
}
