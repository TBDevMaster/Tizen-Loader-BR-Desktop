using System.Text.Json;
using TizenLoaderBRDesktop.Helpers;
using TizenLoaderBRDesktop.Models;

namespace TizenLoaderBRDesktop.Services;

public sealed class PackageLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<List<LibraryPackageRecord>> LoadAsync()
    {
        AppPaths.EnsureBaseFolders();
        if (!File.Exists(AppPaths.LibraryPath))
        {
            return new List<LibraryPackageRecord>();
        }

        await using var stream = File.OpenRead(AppPaths.LibraryPath);
        var records = await JsonSerializer.DeserializeAsync<List<LibraryPackageRecord>>(stream, JsonOptions).ConfigureAwait(false)
            ?? new List<LibraryPackageRecord>();

        foreach (var record in records)
        {
            record.Analysis.IsShellCandidate = false;
            record.Analysis.Warnings.RemoveAll(warning => warning.Equals("Candidato a casca", StringComparison.OrdinalIgnoreCase));
        }

        return records;
    }

    public async Task SaveAsync(IEnumerable<LibraryPackageRecord> records)
    {
        AppPaths.EnsureBaseFolders();
        await using var stream = File.Create(AppPaths.LibraryPath);
        await JsonSerializer.SerializeAsync(stream, records, JsonOptions).ConfigureAwait(false);
    }

    public async Task<LibraryPackageRecord> AddAsync(TizenPackageInfo package, PackageAnalysisResult analysis)
    {
        if (string.IsNullOrWhiteSpace(package.StagedPath) || !File.Exists(package.StagedPath))
        {
            throw new FileNotFoundException("O pacote preparado para a biblioteca não foi encontrado.", package.StagedPath);
        }

        var records = await LoadAsync().ConfigureAwait(false);
        var record = new LibraryPackageRecord
        {
            Package = package,
            Analysis = analysis
        };

        record.LocalPath = package.StagedPath;

        records.Add(record);
        await SaveAsync(records).ConfigureAwait(false);
        return record;
    }

    public async Task<IReadOnlyList<string>> RemoveAsync(Guid recordId, bool deleteOriginal)
    {
        var warnings = new List<string>();
        var records = await LoadAsync().ConfigureAwait(false);
        var record = records.FirstOrDefault(item => item.Id == recordId);
        if (record is null)
        {
            return warnings;
        }

        if (deleteOriginal)
        {
            var packagePath = GetPackagePath(record);
            if (!string.IsNullOrWhiteSpace(packagePath) && File.Exists(packagePath))
            {
                try
                {
                    File.Delete(packagePath);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Nao foi possivel apagar o arquivo do pacote: {ex.Message}");
                }
            }
        }

        records.Remove(record);
        await SaveAsync(records).ConfigureAwait(false);
        return warnings;
    }

    private static string GetPackagePath(LibraryPackageRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.LocalPath) && File.Exists(record.LocalPath))
        {
            return record.LocalPath;
        }

        if (!string.IsNullOrWhiteSpace(record.Package.StagedPath) && File.Exists(record.Package.StagedPath))
        {
            return record.Package.StagedPath;
        }

        if (!string.IsNullOrWhiteSpace(record.Package.SourcePath) && File.Exists(record.Package.SourcePath))
        {
            return record.Package.SourcePath;
        }

        return string.Empty;
    }
}
