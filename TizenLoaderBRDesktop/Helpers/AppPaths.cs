using System.IO;

namespace TizenLoaderBRDesktop.Helpers;

public static class AppPaths
{
    public static string BaseFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TizenLoaderBRDesktop");

    public static string LibraryFolder => Path.Combine(BaseFolder, "Library");

    public static string WorkingFolder => Path.Combine(BaseFolder, "Working");

    public static string DownloadsFolder => Path.Combine(BaseFolder, "Downloads");

    public static string SettingsPath => Path.Combine(BaseFolder, "settings.json");

    public static string LibraryPath => Path.Combine(BaseFolder, "library.json");

    public static string LogsPath => Path.Combine(BaseFolder, "logs.txt");

    public static void EnsureBaseFolders()
    {
        Directory.CreateDirectory(BaseFolder);
        Directory.CreateDirectory(LibraryFolder);
        Directory.CreateDirectory(WorkingFolder);
        Directory.CreateDirectory(DownloadsFolder);
    }
}
