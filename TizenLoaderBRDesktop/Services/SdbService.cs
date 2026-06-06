using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using TizenLoaderBRDesktop.Helpers;
using TizenLoaderBRDesktop.Models;

namespace TizenLoaderBRDesktop.Services;

public sealed class SdbService
{
    public async Task RestartServerAsync(string sdbPath, Action<string>? log, CancellationToken cancellationToken = default)
    {
        log?.Invoke("Finalizando servidor SDB antigo.");

        try
        {
            await RunSdbAsync(sdbPath, new[] { "kill-server" }, log, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Falha ao executar sdb kill-server: {ex.Message}");
        }

        KillLocalSdbProcesses(log);
        await Task.Delay(800, cancellationToken).ConfigureAwait(false);
        log?.Invoke("SDB limpo. A proxima listagem vai iniciar uma nova sessao.");
    }

    public async Task<List<SdbDeviceInfo>> ListDevicesAsync(string sdbPath, CancellationToken cancellationToken = default)
    {
        var result = await RunSdbAsync(sdbPath, new[] { "devices" }, null, cancellationToken).ConfigureAwait(false);
        return ParseDevices(result.StandardOutput);
    }

    public async Task<List<SdbDeviceInfo>> ListDevicesAsync(string sdbPath, Action<string>? log, CancellationToken cancellationToken = default)
    {
        var result = await RunSdbAsync(sdbPath, new[] { "devices" }, log, cancellationToken).ConfigureAwait(false);
        return ParseDevices(result.StandardOutput);
    }

    public async Task<CommandExecutionResult> ConnectAsync(string sdbPath, string address, Action<string>? log, CancellationToken cancellationToken = default)
    {
        var target = NormalizeConnectAddress(address);
        return await RunSdbAsync(sdbPath, new[] { "connect", target }, log, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ScanNetworkAsync(Action<string>? log, CancellationToken cancellationToken = default)
    {
        const int sdbPort = 26101;
        var prefixes = GetLocalIpv4Prefixes().Distinct().ToArray();
        var found = new List<string>();

        if (prefixes.Length == 0)
        {
            log?.Invoke("Nenhuma rede local IPv4 encontrada para scan.");
            return found;
        }

        log?.Invoke($"Buscando relogio na rede: {string.Join(", ", prefixes.Select(prefix => $"{prefix}.0/24"))}");
        using var semaphore = new SemaphoreSlim(64);
        var gate = new object();
        var tasks = new List<Task>();

        foreach (var prefix in prefixes)
        {
            for (var host = 1; host <= 254; host++)
            {
                var address = $"{prefix}.{host}";
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (await IsPortOpenAsync(address, sdbPort, cancellationToken).ConfigureAwait(false))
                        {
                            var target = $"{address}:{sdbPort}";
                            lock (gate)
                            {
                                found.Add(target);
                            }

                            log?.Invoke($"Possivel SDB encontrado: {target}");
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return found.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<bool> TestConnectionAsync(string sdbPath, string serial, CancellationToken cancellationToken = default)
    {
        var result = await RunSdbAsync(sdbPath, new[] { "-s", serial, "shell", "echo", "SDB_OK" }, null, cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.StandardOutput.Contains("SDB_OK", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<DeviceStatusInfo> GetDeviceStatusAsync(string sdbPath, string serial, Action<string>? log, CancellationToken cancellationToken = default)
    {
        var batteryCapacity = await RunShellTextAsync(sdbPath, serial, "cat /sys/class/power_supply/battery/capacity", log, cancellationToken).ConfigureAwait(false);
        var batteryStatus = await RunShellTextAsync(sdbPath, serial, "cat /sys/class/power_supply/battery/status", log, cancellationToken).ConfigureAwait(false);
        var memInfo = await RunShellTextAsync(sdbPath, serial, "cat /proc/meminfo", log, cancellationToken).ConfigureAwait(false);
        var rootDisk = await RunShellTextAsync(sdbPath, serial, "df -h /", log, cancellationToken).ConfigureAwait(false);
        var userDisk = await RunShellTextAsync(sdbPath, serial, "df -h /opt/usr", log, cancellationToken).ConfigureAwait(false);
        var uptime = await RunShellTextAsync(sdbPath, serial, "uptime", log, cancellationToken).ConfigureAwait(false);

        var battery = ParseBattery(batteryCapacity, batteryStatus);
        var memory = ParseMemory(memInfo);
        var rootDiskInfo = ParseDisk(rootDisk);
        var userDiskInfo = ParseDisk(userDisk);

        return new DeviceStatusInfo
        {
            BatteryText = battery.Text,
            BatteryPercent = battery.Percent,
            MemoryText = memory.Text,
            MemoryUsagePercent = memory.Percent,
            RootDiskText = rootDiskInfo.Text,
            RootDiskPercent = rootDiskInfo.Percent,
            UserDiskText = userDiskInfo.Text,
            UserDiskPercent = userDiskInfo.Percent,
            UptimeText = CleanSingleLine(uptime)
        };
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

    private static async Task<string> RunShellTextAsync(string sdbPath, string serial, string command, Action<string>? log, CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunSdbAsync(sdbPath, BuildSerialArgs(serial, "shell", "sh", "-c", command), log, cancellationToken).ConfigureAwait(false);
            var text = string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError : result.StandardOutput;
            return text.Trim();
        }
        catch (Exception ex)
        {
            log?.Invoke($"Falha ao ler status ({command}): {ex.Message}");
            return string.Empty;
        }
    }

    public async Task<string> CaptureDlogAsync(
        string sdbPath,
        string serial,
        string tagFilter,
        int maxLines,
        TimeSpan duration,
        Action<string>? log,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sdbPath))
        {
            throw new InvalidOperationException("Caminho do sdb.exe nao configurado.");
        }

        var lines = new List<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arguments = BuildSerialArgs(serial, "dlog").ToArray();
        log?.Invoke($"Capturando dlog por {duration.TotalSeconds:0}s. Filtro: {tagFilter}");

        var psi = new ProcessStartInfo
        {
            FileName = sdbPath,
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

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => AddDlogLine(e.Data, tagFilter, maxLines, lines, done);
        process.ErrorDataReceived += (_, e) => AddDlogLine(e.Data, tagFilter, maxLines, lines, done);

        if (!process.Start())
        {
            throw new InvalidOperationException("Nao foi possivel iniciar o dlog.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(duration, timeout.Token);
        await Task.WhenAny(done.Task, delayTask).ConfigureAwait(false);

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        return lines.Count == 0
            ? $"Nenhuma linha com {tagFilter} foi capturada nesse intervalo."
            : string.Join(Environment.NewLine, lines);
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

    private static void AddDlogLine(string? line, string tagFilter, int maxLines, List<string> lines, TaskCompletionSource done)
    {
        if (string.IsNullOrWhiteSpace(line)
            || (!string.IsNullOrWhiteSpace(tagFilter) && !line.Contains(tagFilter, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        lock (lines)
        {
            if (lines.Count >= maxLines)
            {
                done.TrySetResult();
                return;
            }

            lines.Add(line);
            if (lines.Count >= maxLines)
            {
                done.TrySetResult();
            }
        }
    }

    private static string NormalizeConnectAddress(string address)
    {
        var value = address.Trim();
        return value.Contains(':', StringComparison.Ordinal)
            ? value
            : $"{value}:26101";
    }

    private static IEnumerable<string> GetLocalIpv4Prefixes()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var parts = address.Address.ToString().Split('.');
                if (parts.Length == 4)
                {
                    yield return $"{parts[0]}.{parts[1]}.{parts[2]}";
                }
            }
        }
    }

    private static async Task<bool> IsPortOpenAsync(string address, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(180);
            using var client = new TcpClient();
            await client.ConnectAsync(address, port, timeout.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static void KillLocalSdbProcesses(Action<string>? log)
    {
        var currentProcessId = Environment.ProcessId;
        var killedCount = 0;

        foreach (var process in Process.GetProcessesByName("sdb"))
        {
            try
            {
                if (process.Id == currentProcessId || process.HasExited)
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit(1500);
                killedCount++;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Nao foi possivel finalizar sdb.exe ({process.Id}): {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        if (killedCount > 0)
        {
            log?.Invoke($"{killedCount} processo(s) sdb.exe antigo(s) finalizado(s).");
        }
    }

    private static string FormatBattery(string capacity, string status)
    {
        var cleanCapacity = CleanSingleLine(capacity);
        var cleanStatus = CleanSingleLine(status);

        if (string.IsNullOrWhiteSpace(cleanCapacity))
        {
            return "indisponivel";
        }

        return string.IsNullOrWhiteSpace(cleanStatus)
            ? $"{cleanCapacity}%"
            : $"{cleanCapacity}% ({cleanStatus})";
    }

    private static string FormatMemory(string memInfo)
    {
        var totalKb = GetMemInfoValue(memInfo, "MemTotal");
        var availableKb = GetMemInfoValue(memInfo, "MemAvailable");

        if (totalKb <= 0)
        {
            return "indisponivel";
        }

        if (availableKb <= 0)
        {
            return $"total {FormatKilobytes(totalKb)}";
        }

        var usedKb = Math.Max(0, totalKb - availableKb);
        return $"{FormatKilobytes(usedKb)} usados / {FormatKilobytes(totalKb)} total";
    }

    private static long GetMemInfoValue(string memInfo, string key)
    {
        var match = Regex.Match(memInfo, $@"^{Regex.Escape(key)}:\s+(?<value>\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return match.Success && long.TryParse(match.Groups["value"].Value, out var value) ? value : 0;
    }

    private static string FormatKilobytes(long value)
    {
        var megabytes = value / 1024d;
        return megabytes >= 1024
            ? $"{megabytes / 1024d:0.0} GB"
            : $"{megabytes:0} MB";
    }

    private static string FormatDisk(string output)
    {
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var cleanedLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!string.IsNullOrWhiteSpace(line))
            {
                cleanedLines.Add(line);
            }
        }

        if (cleanedLines.Count == 0)
        {
            return "indisponivel";
        }

        var dataLine = string.Empty;
        for (var i = cleanedLines.Count - 1; i >= 0; i--)
        {
            if (!cleanedLines[i].StartsWith("Filesystem", StringComparison.OrdinalIgnoreCase))
            {
                dataLine = cleanedLines[i];
                break;
            }
        }

        var parts = Regex.Split(dataLine, @"\s+");
        var cleanedParts = new List<string>();
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                cleanedParts.Add(part);
            }
        }

        return cleanedParts.Count >= 5
            ? $"{cleanedParts[2]} usados / {cleanedParts[1]} total ({cleanedParts[4]})"
            : CleanSingleLine(dataLine);
    }

    private static (string Text, int Percent) ParseBattery(string capacity, string status)
    {
        var cleanCapacity = CleanSingleLine(capacity).Trim().TrimEnd('%');
        var cleanStatus = CleanSingleLine(status);
        var percent = int.TryParse(cleanCapacity, out var parsedCapacity) ? Math.Clamp(parsedCapacity, 0, 100) : 0;

        var text = string.IsNullOrWhiteSpace(cleanCapacity)
            ? "indisponivel"
            : string.IsNullOrWhiteSpace(cleanStatus)
                ? $"{cleanCapacity}%"
                : $"{cleanCapacity}% ({cleanStatus})";

        return (text, percent);
    }

    private static (string Text, int Percent) ParseMemory(string memInfo)
    {
        var totalKb = GetMemInfoValue(memInfo, "MemTotal");
        var availableKb = GetMemInfoValue(memInfo, "MemAvailable");

        if (totalKb <= 0)
        {
            return ("indisponivel", 0);
        }

        var usedKb = Math.Max(0, totalKb - availableKb);
        var percent = totalKb > 0 ? (int)Math.Round(usedKb * 100d / totalKb) : 0;
        var text = availableKb <= 0
            ? $"total {FormatKilobytes(totalKb)}"
            : $"{FormatKilobytes(usedKb)} usados / {FormatKilobytes(totalKb)} total";

        return (text, Math.Clamp(percent, 0, 100));
    }

    private static (string Text, int Percent) ParseDisk(string output)
    {
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var cleanedLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!string.IsNullOrWhiteSpace(line))
            {
                cleanedLines.Add(line);
            }
        }

        var text = FormatDisk(output);
        var dataLine = string.Empty;

        for (var i = cleanedLines.Count - 1; i >= 0; i--)
        {
            if (!cleanedLines[i].StartsWith("Filesystem", StringComparison.OrdinalIgnoreCase))
            {
                dataLine = cleanedLines[i];
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(dataLine))
        {
            return (text, 0);
        }

        var parts = Regex.Split(dataLine, @"\s+");
        var cleanedParts = new List<string>();
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                cleanedParts.Add(part);
            }
        }

        if (cleanedParts.Count >= 5 && int.TryParse(cleanedParts[4].TrimEnd('%'), out var percent))
        {
            return (text, Math.Clamp(percent, 0, 100));
        }

        return (text, 0);
    }

    private static string CleanSingleLine(string value)
    {
        var clean = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;

        return string.IsNullOrWhiteSpace(clean) ? "indisponivel" : clean;
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
                || line.StartsWith("info:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("debug:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("warn:", StringComparison.OrdinalIgnoreCase)
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
            if (!IsKnownDeviceState(state))
            {
                continue;
            }

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

    private static bool IsKnownDeviceState(string state)
    {
        return state.Equals("device", StringComparison.OrdinalIgnoreCase)
            || state.Equals("offline", StringComparison.OrdinalIgnoreCase)
            || state.Equals("unauthorized", StringComparison.OrdinalIgnoreCase)
            || state.Equals("unknown", StringComparison.OrdinalIgnoreCase);
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
        var packageId = GetBracketValue(line, "pkgid");
        var name = GetBracketValue(line, "name");
        var version = GetBracketValue(line, "version");
        var packageType = GetBracketValue(line, "pkg_type");

        return new InstalledTizenApp
        {
            PackageId = packageId,
            Version = version,
            Name = string.IsNullOrWhiteSpace(name) ? packageId : name,
            PackageType = packageType,
            RawLine = line
        };
    }

    private static string GetBracketValue(string line, string key)
    {
        var match = Regex.Match(line, $@"\b{Regex.Escape(key)}\s+\[(?<value>[^\]]*)\]", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Trim() : string.Empty;
    }
}
