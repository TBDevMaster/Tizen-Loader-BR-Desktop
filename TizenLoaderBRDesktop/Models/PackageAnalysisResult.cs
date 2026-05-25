namespace TizenLoaderBRDesktop.Models;

public sealed class PackageAnalysisResult
{
    public string Sha256 { get; set; } = string.Empty;

    public TizenPackageKind DetectedType { get; set; } = TizenPackageKind.Unknown;

    public string PackageId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = new();

    public List<string> KeywordsFound { get; set; } = new();

    public List<string> SignatureFiles { get; set; } = new();

    public List<string> Warnings { get; set; } = new();

    public List<string> Notes { get; set; } = new();

    public bool HasConfigXml { get; set; }

    public bool HasManifestXml { get; set; }

    public bool SignatureFound { get; set; }

    public bool UsesInternet { get; set; }

    public bool IsWatchfaceCandidate { get; set; }

    public bool IsShellCandidate { get; set; }

    public bool ProbablyInstallable { get; set; }
}
