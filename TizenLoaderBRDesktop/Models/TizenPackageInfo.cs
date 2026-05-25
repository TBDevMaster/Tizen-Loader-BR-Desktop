namespace TizenLoaderBRDesktop.Models;

public sealed class TizenPackageInfo
{
    public string SourcePath { get; set; } = string.Empty;

    public string StagedPath { get; set; } = string.Empty;

    public string OriginalContainerPath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public TizenPackageKind Kind { get; set; } = TizenPackageKind.Unknown;

    public bool IsFromArchive { get; set; }

    public bool IsSelectedForImport { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(DisplayName) ? FileName : DisplayName;
}
