namespace TizenLoaderBRDesktop.Models;

public sealed class InstalledTizenApp
{
    public string PackageId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string PackageType { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string RawLine { get; set; } = string.Empty;
}
