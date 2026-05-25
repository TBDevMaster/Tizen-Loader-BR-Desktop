namespace TizenLoaderBRDesktop.Models;

public sealed class SdbDeviceInfo
{
    public string Serial { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string RawLine { get; set; } = string.Empty;

    public override string ToString() => string.IsNullOrWhiteSpace(Model)
        ? $"{Serial} ({State})"
        : $"{Serial} ({State}) - {Model}";
}
