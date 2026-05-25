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

        if (string.IsNullOrWhiteSpace(settings.SdbPath))
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
        var candidates = new[]
        {
            @"C:\tizen-studio\tools\sdb.exe",
            @"C:\tizen-studio\platforms\wearable\tools\sdb.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "tizen-studio", "tools", "sdb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "tizen-studio", "tools", "sdb.exe")
        };

        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }
}
