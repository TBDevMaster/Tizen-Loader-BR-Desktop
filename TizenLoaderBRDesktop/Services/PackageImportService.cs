using System.IO.Compression;
using TizenLoaderBRDesktop.Helpers;
using TizenLoaderBRDesktop.Models;

namespace TizenLoaderBRDesktop.Services;

public sealed class PackageImportService
{
    private static readonly HashSet<string> IgnoredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "__MACOSX",
        ".DS_Store",
        "Thumbs.db",
        "desktop.ini"
    };

    public async Task<IReadOnlyList<TizenPackageInfo>> ImportAsync(string sourcePath, string workingFolder, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return Array.Empty<TizenPackageInfo>();
        }

        Directory.CreateDirectory(workingFolder);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        if (extension == ".wgt" || extension == ".tpk")
        {
            return new[] { await StageSingleFileAsync(sourcePath, workingFolder, cancellationToken).ConfigureAwait(false) };
        }

        if (extension != ".zip")
        {
            return Array.Empty<TizenPackageInfo>();
        }

        var extractRoot = Path.Combine(workingFolder, "ZipImports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);
        try
        {
            ExtractZipSafely(sourcePath, extractRoot);
            var candidates = FindPackageCandidates(extractRoot);
            var staged = new List<TizenPackageInfo>();

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                staged.Add(await StageExtractedCandidateAsync(candidate, sourcePath, workingFolder, cancellationToken).ConfigureAwait(false));
            }

            return staged;
        }
        finally
        {
            try
            {
                if (Directory.Exists(extractRoot))
                {
                    Directory.Delete(extractRoot, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static async Task<TizenPackageInfo> StageSingleFileAsync(string sourcePath, string workingFolder, CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return CreatePackageInfo(sourcePath, sourcePath, false, sourcePath);
    }

    private static async Task<TizenPackageInfo> StageExtractedCandidateAsync(string candidatePath, string containerPath, string workingFolder, CancellationToken cancellationToken)
    {
        var baseFolder = Directory.Exists(workingFolder)
            ? workingFolder
            : Path.GetDirectoryName(containerPath) ?? AppPaths.WorkingFolder;
        var stagingFolder = Path.Combine(baseFolder, "TizenExtraidos", Path.GetFileNameWithoutExtension(containerPath));
        Directory.CreateDirectory(stagingFolder);
        var destination = Path.Combine(stagingFolder, Path.GetFileName(candidatePath));
        await using (var source = File.OpenRead(candidatePath))
        await using (var target = File.Create(destination))
        {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        return CreatePackageInfo(candidatePath, destination, true, containerPath);
    }

    private static TizenPackageInfo CreatePackageInfo(string sourcePath, string stagedPath, bool fromArchive, string containerPath)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var kind = TizenPackageKind.Unknown;
        if (extension == ".wgt")
        {
            kind = TizenPackageKind.Wgt;
        }
        else if (extension == ".tpk")
        {
            kind = TizenPackageKind.Tpk;
        }
        else if (extension == ".zip")
        {
            kind = TizenPackageKind.Zip;
        }

        return new TizenPackageInfo
        {
            SourcePath = sourcePath,
            StagedPath = stagedPath,
            OriginalContainerPath = fromArchive ? containerPath : string.Empty,
            FileName = Path.GetFileName(stagedPath),
            DisplayName = Path.GetFileNameWithoutExtension(stagedPath),
            Kind = kind,
            IsFromArchive = fromArchive
        };
    }

    private static void ExtractZipSafely(string zipPath, string destinationFolder)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
            {
                continue;
            }

            if (ShouldIgnore(entry.FullName))
            {
                continue;
            }

            var destinationPath = Path.Combine(destinationFolder, entry.FullName);
            var fullDestinationPath = Path.GetFullPath(destinationPath);
            if (!fullDestinationPath.StartsWith(Path.GetFullPath(destinationFolder), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(fullDestinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            using var source = entry.Open();
            using var target = File.Create(fullDestinationPath);
            source.CopyTo(target);
        }
    }

    private static List<string> FindPackageCandidates(string root)
    {
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => IsCandidate(path) && !ShouldIgnore(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsCandidate(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".wgt" or ".tpk";
    }

    private static bool ShouldIgnore(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => IgnoredNames.Contains(part))
            || path.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase)
            || path.Contains("._", StringComparison.OrdinalIgnoreCase);
    }
}
