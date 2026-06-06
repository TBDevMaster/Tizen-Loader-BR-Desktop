namespace TizenLoaderBRDesktop.Models;

public sealed class DeviceStatusInfo
{
    public string BatteryText { get; set; } = "indisponivel";
    public int BatteryPercent { get; set; }

    public string MemoryText { get; set; } = "indisponivel";
    public int MemoryUsagePercent { get; set; }

    public string RootDiskText { get; set; } = "indisponivel";
    public int RootDiskPercent { get; set; }

    public string UserDiskText { get; set; } = "indisponivel";
    public int UserDiskPercent { get; set; }

    public string UptimeText { get; set; } = "indisponivel";

    public override string ToString()
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"Bateria: {BatteryText}",
            $"Memoria: {MemoryText}",
            $"Disco /: {RootDiskText}",
            $"Disco usuario: {UserDiskText}",
            $"Uptime: {UptimeText}"
        });
    }
}
