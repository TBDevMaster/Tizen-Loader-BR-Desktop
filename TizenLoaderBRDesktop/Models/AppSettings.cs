namespace TizenLoaderBRDesktop.Models;

public sealed class AppSettings
{
    public string SdbPath { get; set; } = string.Empty;

    public string LastDeviceSerial { get; set; } = string.Empty;

    public string WorkingFolder { get; set; } = string.Empty;

    public string DownloadFolder { get; set; } = string.Empty;
}
