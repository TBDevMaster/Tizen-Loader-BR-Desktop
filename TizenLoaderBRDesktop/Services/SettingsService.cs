using System.Text.Json;
using TizenLoaderBRDesktop.Helpers;
using TizenLoaderBRDesktop.Models;

namespace TizenLoaderBRDesktop.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings CreateDefaultSettings()
    {
        AppPaths.EnsureBaseFolders();
        return new AppSettings
        {
            SdbPath = DetectSdbPath(),
            WorkingFolder = AppPaths.WorkingFolder,
            DownloadFolder = AppPaths.DownloadsFolder
        };
    }

    public async Task<AppSettings> LoadAsync()
    {
        AppPaths.EnsureBaseFolders();
        if (!File.Exists(AppPaths.SettingsPath))
        {
            var defaults = CreateDefaultSettings();
            await SaveAsync(defaults).ConfigureAwait(false);
            return defaults;
        }

        await using var stream = File.OpenRead(AppPaths.SettingsPath);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions).ConfigureAwait(false);
        settings ??= CreateDefaultSettings();

        if (string.IsNullOrWhiteSpace(settings.SdbPath) || !File.Exists(settings.SdbPath))
        {
            settings.SdbPath = DetectSdbPath();
        }

        if (string.IsNullOrWhiteSpace(settings.WorkingFolder))
        {
            settings.WorkingFolder = AppPaths.WorkingFolder;
        }

        if (string.IsNullOrWhiteSpace(settings.DownloadFolder))
        {
            settings.DownloadFolder = AppPaths.DownloadsFolder;
        }

        return settings;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        AppPaths.EnsureBaseFolders();
        await using var stream = File.Create(AppPaths.SettingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions).ConfigureAwait(false);
    }

    public string DetectSdbPath()
    {
        var appFolder = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(appFolder, "Tools", "sdb", "sdb.exe"),
            Path.Combine(AppContext.BaseDirectory, "sdb.exe"),
            @"C:\tizen-studio\tools\sdb.exe",
            @"C:\tizen-studio\platforms\wearable\tools\sdb.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "tizen-studio", "tools", "sdb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "tizen-studio", "tools", "sdb.exe")
        };

        candidates.AddRange(GetSingleFileExtractedSdbCandidates());
        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private static IEnumerable<string> GetSingleFileExtractedSdbCandidates()
    {
        var bundleRoot = Path.Combine(Path.GetTempPath(), ".net", "TizenLoaderBRDesktop");
        if (!Directory.Exists(bundleRoot))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.EnumerateDirectories(bundleRoot)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .Select(folder => Path.Combine(folder, "Tools", "sdb", "sdb.exe"))
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
