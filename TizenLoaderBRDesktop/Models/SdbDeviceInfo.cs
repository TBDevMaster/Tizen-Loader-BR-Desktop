namespace TizenLoaderBRDesktop.Models;

public sealed class SdbDeviceInfo
{
    public string Serial { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string RawLine { get; set; } = string.Empty;

    public string DisplayAddress => Serial.Replace(":26101", string.Empty, StringComparison.OrdinalIgnoreCase);

    public string ConnectionStatus => State.Equals("device", StringComparison.OrdinalIgnoreCase)
        ? "Conectado"
        : State.Equals("unauthorized", StringComparison.OrdinalIgnoreCase)
            ? "Nao autorizado"
            : State;

    public override string ToString() => string.IsNullOrWhiteSpace(Model)
        ? $"{Serial} ({State})"
        : $"{Serial} ({State}) - {Model}";
}
