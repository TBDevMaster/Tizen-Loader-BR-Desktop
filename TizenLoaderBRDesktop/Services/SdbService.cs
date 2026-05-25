using System.Text.RegularExpressions;
using TizenLoaderBRDesktop.Helpers;
using TizenLoaderBRDesktop.Models;

namespace TizenLoaderBRDesktop.Services;

public sealed class SdbService
{
    public async Task<List<SdbDeviceInfo>> ListDevicesAsync(string sdbPath, CancellationToken cancellationToken = default)
    {
        var result = await RunSdbAsync(sdbPath, new[] { "devices", "-l" }, null, cancellationToken).ConfigureAwait(false);
        return ParseDevices(result.StandardOutput);
    }

    public async Task<bool> TestConnectionAsync(string sdbPath, string serial, CancellationToken cancellationToken = default)
    {
        var result = await RunSdbAsync(sdbPath, new[] { "-s", serial, "shell", "echo", "SDB_OK" }, null, cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.StandardOutput.Contains("SDB_OK", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<InstalledTizenApp>> ListInstalledAppsAsync(string sdbPath, string serial, CancellationToken cancellationToken = default)
    {
        var result = await RunSdbAsync(sdbPath, new[] { "-s", serial, "shell", "pkgcmd", "-l" }, null, cancellationToken).ConfigureAwait(false);
        return ParseInstalledApps(result.StandardOutput);
    }

    public async Task<CommandExecutionResult> InstallAsync(string sdbPath, string serial, TizenPackageInfo packageInfo, Action<string>? log, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageInfo.StagedPath) || !File.Exists(packageInfo.StagedPath))
        {
            throw new FileNotFoundException("O arquivo do pacote não foi encontrado para instalação.", packageInfo.StagedPath);
        }

        var remotePath = $"/tmp/{Path.GetFileName(packageInfo.StagedPath)}";
        log?.Invoke($"Início da instalação de {packageInfo.FileName}");

        var pushResult = await RunSdbAsync(
            sdbPath,
            BuildSerialArgs(serial, "push", packageInfo.StagedPath, remotePath),
            log,
            cancellationToken).ConfigureAwait(false);

        if (!pushResult.Succeeded)
        {
            return pushResult;
        }

        log?.Invoke($"Arquivo enviado para {remotePath}");

        var installArgs = packageInfo.Kind switch
        {
            TizenPackageKind.Wgt => BuildSerialArgs(serial, "shell", "pkgcmd", "-w", "-t", "wgt", "-p", remotePath),
            TizenPackageKind.Tpk => BuildSerialArgs(serial, "shell", "pkgcmd", "-i", "-t", "tpk", "-p", remotePath),
            _ => BuildSerialArgs(serial, "shell", "pkgcmd", "-w", "-t", "wgt", "-p", remotePath)
        };

        log?.Invoke($"Comando executado: {string.Join(' ', installArgs)}");
        var installResult = await RunSdbAsync(sdbPath, installArgs, log, cancellationToken).ConfigureAwait(false);
        return installResult;
    }

    public async Task<CommandExecutionResult> UninstallAsync(string sdbPath, string serial, string packageId, Action<string>? log, CancellationToken cancellationToken = default)
    {
        log?.Invoke($"Início da remoção de {packageId}");
        var args = BuildSerialArgs(serial, "shell", "pkgcmd", "-u", "-n", packageId);
        log?.Invoke($"Comando executado: {string.Join(' ', args)}");
        return await RunSdbAsync(sdbPath, args, log, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CommandExecutionResult> RunRawAsync(string sdbPath, IEnumerable<string> args, Action<string>? log, CancellationToken cancellationToken = default)
    {
        return await RunSdbAsync(sdbPath, args, log, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CommandExecutionResult> RunSdbAsync(string sdbPath, IEnumerable<string> args, Action<string>? log, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sdbPath))
        {
            throw new InvalidOperationException("Caminho do sdb.exe não configurado.");
        }

        log?.Invoke($"Executando: {sdbPath} {string.Join(' ', args)}");
        var result = await ProcessRunner.RunAsync(
            sdbPath,
            args,
            output: line => log?.Invoke(line),
            error: line => log?.Invoke($"[stderr] {line}"),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        log?.Invoke($"Saída final: código {result.ExitCode}");
        return result;
    }

    private static IEnumerable<string> BuildSerialArgs(string serial, params string[] args)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(serial))
        {
            list.Add("-s");
            list.Add(serial);
        }

        list.AddRange(args);
        return list;
    }

    private static List<SdbDeviceInfo> ParseDevices(string output)
    {
        var devices = new List<SdbDeviceInfo>();
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Available", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("*", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = Regex.Split(line, @"\s+").Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
            if (parts.Length == 0)
            {
                continue;
            }

            var serial = parts[0];
            var state = parts.Length > 1 ? parts[1] : string.Empty;
            var model = parts.Length > 2 ? string.Join(' ', parts.Skip(2)) : string.Empty;

            devices.Add(new SdbDeviceInfo
            {
                Serial = serial,
                State = state,
                Model = model,
                RawLine = line
            });
        }

        return devices;
    }

    private static List<InstalledTizenApp> ParseInstalledApps(string output)
    {
        var apps = new List<InstalledTizenApp>();
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Contains("pkgcmd", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("package", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("installed", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var app = ParseInstalledAppLine(line);
            if (!string.IsNullOrWhiteSpace(app.PackageId))
            {
                apps.Add(app);
            }
        }

        return apps;
    }

    private static InstalledTizenApp ParseInstalledAppLine(string line)
    {
        var normalized = line.Replace('|', ' ');
        var parts = Regex.Split(normalized, @"\s+")
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (parts.Length == 0)
        {
            return new InstalledTizenApp { RawLine = line };
        }

        var version = parts.FirstOrDefault(part => Regex.IsMatch(part, @"^\d+(\.\d+){1,3}([\-+].+)?$")) ?? string.Empty;
        var packageId = parts[0];
        var nameParts = parts.Skip(1)
            .Where(part => !string.Equals(part, version, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new InstalledTizenApp
        {
            PackageId = packageId,
            Version = version,
            Name = nameParts.Length > 0 ? string.Join(' ', nameParts) : packageId,
            RawLine = line
        };
    }
}
