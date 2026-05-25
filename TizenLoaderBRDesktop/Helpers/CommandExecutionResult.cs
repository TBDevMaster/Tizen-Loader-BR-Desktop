namespace TizenLoaderBRDesktop.Helpers;

public sealed class CommandExecutionResult
{
    public int ExitCode { get; set; }

    public string StandardOutput { get; set; } = string.Empty;

    public string StandardError { get; set; } = string.Empty;

    public bool Succeeded => ExitCode == 0;
}
