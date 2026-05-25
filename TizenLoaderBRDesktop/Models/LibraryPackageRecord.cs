namespace TizenLoaderBRDesktop.Models;

public sealed class LibraryPackageRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;

    public TizenPackageInfo Package { get; set; } = new();

    public PackageAnalysisResult Analysis { get; set; } = new();

    public string LocalPath { get; set; } = string.Empty;
}
