using System.Diagnostics;
using System.Text;

namespace TizenLoaderBRDesktop.Helpers;

public static class ProcessRunner
{
    public static async Task<CommandExecutionResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        Action<string>? output = null,
        Action<string>? error = null,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        var result = new CommandExecutionResult();
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var exitTask = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            stdout.AppendLine(e.Data);
            output?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            stderr.AppendLine(e.Data);
            error?.Invoke(e.Data);
        };

        process.Exited += (_, _) => exitTask.TrySetResult(process.ExitCode);

        if (!process.Start())
        {
            throw new InvalidOperationException($"Não foi possível iniciar o processo: {fileName}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        result.ExitCode = await exitTask.Task.ConfigureAwait(false);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        result.StandardOutput = stdout.ToString();
        result.StandardError = stderr.ToString();
        return result;
    }
}
