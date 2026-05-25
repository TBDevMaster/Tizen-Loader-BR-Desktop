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
        return await JsonSerializer.DeserializeAsync<List<LibraryPackageRecord>>(stream, JsonOptions).ConfigureAwait(false)
            ?? new List<LibraryPackageRecord>();
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

        var safeName = $"{record.Id:N}_{Path.GetFileName(package.StagedPath)}";
        record.LocalPath = Path.Combine(AppPaths.LibraryFolder, safeName);
        Directory.CreateDirectory(AppPaths.LibraryFolder);
        File.Copy(package.StagedPath, record.LocalPath, overwrite: false);

        records.Add(record);
        await SaveAsync(records).ConfigureAwait(false);
        return record;
    }

    public async Task RemoveAsync(Guid recordId)
    {
        var records = await LoadAsync().ConfigureAwait(false);
        var record = records.FirstOrDefault(item => item.Id == recordId);
        if (record is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(record.LocalPath) && File.Exists(record.LocalPath))
        {
            File.Delete(record.LocalPath);
        }

        records.Remove(record);
        await SaveAsync(records).ConfigureAwait(false);
    }
}
