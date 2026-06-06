using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TizenLoaderBRDesktop.Helpers;
using TizenLoaderBRDesktop.Models;
using TizenLoaderBRDesktop.Services;

namespace TizenLoaderBRDesktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = new();
    private readonly SdbService _sdbService = new();
    private readonly PackageAnalyzerService _packageAnalyzerService = new();
    private readonly PackageImportService _packageImportService = new();
    private readonly PackageLibraryService _packageLibraryService = new();
    private readonly DownloadService _downloadService = new();
    private readonly BrowserService _browserService = new();
    private readonly LogService _logService = new();

    private AppSettings _settings = new();

    public MainViewModel()
    {
        LibraryItems = new ObservableCollection<LibraryPackageRecord>();
        Devices = new ObservableCollection<SdbDeviceInfo>();
        InstalledApps = new ObservableCollection<InstalledTizenApp>();
        ImportCandidates = new ObservableCollection<TizenPackageInfo>();
        LibraryView = CollectionViewSource.GetDefaultView(LibraryItems);
        LibraryView.Filter = FilterLibraryItem;
        LibraryFilters = new ObservableCollection<string>
        {
            "Todos",
            "Assinados",
            "Apps",
            "Watchfaces",
            "Candidatos"
        };
        SelectedLibraryFilter = "Todos";
        UsefulCommandsText = """
sdb devices
sdb shell pkgcmd -l -t wgt
sdb shell pkgcmd -s -t wgt -n <pkgid>
sdb shell pkgcmd -u -n <pkgid>
sdb dlog
""";
    }

    public ObservableCollection<SdbDeviceInfo> Devices { get; }

    public ObservableCollection<InstalledTizenApp> InstalledApps { get; }

    public ObservableCollection<LibraryPackageRecord> LibraryItems { get; }

    public ObservableCollection<TizenPackageInfo> ImportCandidates { get; }

    public ObservableCollection<string> LibraryFilters { get; }

    public ICollectionView LibraryView { get; }

    public ObservableCollection<LogEntry> Logs => _logService.Entries;

    [ObservableProperty]
    private SdbDeviceInfo? selectedDevice;

    [ObservableProperty]
    private InstalledTizenApp? selectedInstalledApp;

    [ObservableProperty]
    private LibraryPackageRecord? selectedLibraryItem;

    [ObservableProperty]
    private TizenPackageInfo? selectedImportCandidate;

    [ObservableProperty]
    private string selectedLibraryFilter = string.Empty;

    [ObservableProperty]
    private string sdbPath = string.Empty;

    [ObservableProperty]
    private string workingFolder = string.Empty;

    [ObservableProperty]
    private string downloadFolder = string.Empty;

    [ObservableProperty]
    private string connectAddress = string.Empty;

    [ObservableProperty]
    private string sourceUrl = string.Empty;

    [ObservableProperty]
    private string installLogText = "O log de instalacao aparece aqui.";

    [ObservableProperty]
    private string uninstallLogText = "O log de desinstalacao aparece aqui.";

    [ObservableProperty]
    private string deviceStatusText = "O status do relogio aparece aqui.";

    [ObservableProperty]
    private DeviceStatusInfo? deviceStatus;

    [ObservableProperty]
    private string statusText = "Pronto.";

    [ObservableProperty]
    private string busyMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private int selectedTabIndex;

    public string UsefulCommandsText { get; }

    public async Task InitializeAsync()
    {
        try
        {
            _settings = await _settingsService.LoadAsync().ConfigureAwait(true);
            SdbPath = _settings.SdbPath;
            WorkingFolder = _settings.WorkingFolder;
            DownloadFolder = _settings.DownloadFolder;
            ConnectAddress = _settings.LastConnectAddress;

            if (Directory.Exists(DownloadFolder))
            {
                await RefreshLibraryFromFolderAsync().ConfigureAwait(true);
            }
            else
            {
                await ReloadLibraryAsync().ConfigureAwait(true);
            }

            await RefreshDevicesAsync().ConfigureAwait(true);
            StatusText = "Biblioteca e dispositivos carregados.";
        }
        catch (Exception ex)
        {
            _logService.Error("Inicialização", ex.Message);
            StatusText = "Falha ao inicializar.";
        }
    }

    partial void OnSelectedDeviceChanged(SdbDeviceInfo? value)
    {
        _settings.LastDeviceSerial = value?.Serial ?? string.Empty;
    }

    partial void OnSdbPathChanged(string value)
    {
        _settings.SdbPath = value;
    }

    partial void OnWorkingFolderChanged(string value)
    {
        _settings.WorkingFolder = value;
    }

    partial void OnDownloadFolderChanged(string value)
    {
        _settings.DownloadFolder = value;
    }

    partial void OnConnectAddressChanged(string value)
    {
        _settings.LastConnectAddress = value;
    }

    partial void OnSelectedLibraryFilterChanged(string value)
    {
        LibraryView?.Refresh();
    }

    private bool FilterLibraryItem(object item)
    {
        if (item is not LibraryPackageRecord record)
        {
            return false;
        }

        return SelectedLibraryFilter switch
        {
            "Assinados" => record.Analysis.SignatureFound,
            "Apps" => !record.Analysis.IsWatchfaceCandidate,
            "Watchfaces" => record.Analysis.IsWatchfaceCandidate,
            "Candidatos" => record.Analysis.IsWatchfaceCandidate || !record.Analysis.SignatureFound,
            _ => true
        };
    }

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await ExecuteBusyAsync("Atualizando dispositivos", async () =>
        {
            if (string.IsNullOrWhiteSpace(SdbPath) || !File.Exists(SdbPath))
            {
                _logService.Warn("Dispositivos", "Caminho do sdb.exe não configurado ou inválido.");
                Devices.Clear();
                return;
            }

            await _sdbService.RestartServerAsync(SdbPath, message => _logService.Info("SDB", message)).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(ConnectAddress))
            {
                await _sdbService.ConnectAsync(SdbPath, ConnectAddress, message => _logService.Info("SDB", message)).ConfigureAwait(true);
            }

            var devices = await _sdbService.ListDevicesAsync(SdbPath, message => _logService.Info("SDB", message)).ConfigureAwait(true);
            Devices.Clear();
            foreach (var device in devices)
            {
                Devices.Add(device);
            }

            if (!string.IsNullOrWhiteSpace(_settings.LastDeviceSerial))
            {
                SelectedDevice = Devices.FirstOrDefault(device => device.Serial.Equals(_settings.LastDeviceSerial, StringComparison.OrdinalIgnoreCase));
            }

            SelectedDevice ??= Devices.FirstOrDefault();
            StatusText = $"{Devices.Count} dispositivo(s) encontrado(s).";
            _logService.Info("Dispositivos", $"{Devices.Count} dispositivo(s) listados.");
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ConnectByAddressAsync()
    {
        if (string.IsNullOrWhiteSpace(ConnectAddress))
        {
            MessageBox.Show("Informe o IP do relogio. Exemplo: 192.168.0.50", "Tizen Loader BR Desktop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(SdbPath) || !File.Exists(SdbPath))
        {
            MessageBox.Show("sdb.exe nao encontrado.", "Tizen Loader BR Desktop", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await ExecuteBusyAsync("Conectando ao relogio por IP", async () =>
        {
            var result = await _sdbService.ConnectAsync(SdbPath, ConnectAddress, message => _logService.Info("SDB", message)).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                StatusText = "Falha ao conectar por IP.";
                _logService.Warn("SDB", "Falha ao conectar por IP.");
                return;
            }

            var devices = await _sdbService.ListDevicesAsync(SdbPath, message => _logService.Info("SDB", message)).ConfigureAwait(true);
            Devices.Clear();
            foreach (var device in devices)
            {
                Devices.Add(device);
            }

            SelectedDevice = Devices.FirstOrDefault();
            StatusText = $"{Devices.Count} dispositivo(s) encontrado(s).";
            _logService.Info("Dispositivos", $"{Devices.Count} dispositivo(s) listados.");
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ScanNetworkAsync()
    {
        if (string.IsNullOrWhiteSpace(SdbPath) || !File.Exists(SdbPath))
        {
            MessageBox.Show("sdb.exe nao encontrado.", "Tizen Loader BR Desktop", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await ExecuteBusyAsync("Buscando relogio na rede", async () =>
        {
            await _sdbService.RestartServerAsync(SdbPath, message => _logService.Info("SDB", message)).ConfigureAwait(true);
            var targets = await _sdbService.ScanNetworkAsync(message => _logService.Info("Scan", message)).ConfigureAwait(true);

            if (targets.Count == 0)
            {
                StatusText = "Nenhum relogio encontrado na rede.";
                _logService.Warn("Scan", "Nenhum alvo SDB respondeu na porta 26101.");
                return;
            }

            ConnectAddress = targets[0];
            foreach (var target in targets)
            {
                await _sdbService.ConnectAsync(SdbPath, target, message => _logService.Info("SDB", message)).ConfigureAwait(true);
            }

            var devices = await _sdbService.ListDevicesAsync(SdbPath, message => _logService.Info("SDB", message)).ConfigureAwait(true);
            Devices.Clear();
            foreach (var device in devices)
            {
                Devices.Add(device);
            }

            SelectedDevice = Devices.FirstOrDefault();
            StatusText = $"{Devices.Count} dispositivo(s) encontrado(s).";
            _logService.Info("Scan", $"{targets.Count} alvo(s) SDB encontrado(s).");
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (SelectedDevice is null)
        {
            MessageBox.Show("Selecione um relógio primeiro.", "Tizen Loader BR Desktop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await ExecuteBusyAsync("Verificando conexão", async () =>
        {
            var ok = await _sdbService.TestConnectionAsync(SdbPath, SelectedDevice.Serial).ConfigureAwait(true);
            if (ok)
            {
                _logService.Info("Conexão", $"Conexão OK com {SelectedDevice.Serial}");
                StatusText = $"Conexão OK com {SelectedDevice.Serial}.";
            }
            else
            {
                _logService.Warn("Conexão", $"Falha ao verificar {SelectedDevice.Serial}");
                StatusText = $"Falha ao verificar {SelectedDevice.Serial}.";
            }
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshInstalledAppsAsync()
    {
        if (SelectedDevice is null)
        {
            MessageBox.Show("Selecione um relógio para listar os aplicativos instalados.", "Tizen Loader BR Desktop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await ReloadInstalledAppsAsync("Listando apps instalados").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshDeviceStatusAsync()
    {
        if (SelectedDevice is null)
        {
            MessageBox.Show("Selecione um relogio para ler o status.", "Tizen Loader BR Desktop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await ExecuteBusyAsync("Lendo status do relogio", async () =>
        {
            var status = await _sdbService.GetDeviceStatusAsync(SdbPath, SelectedDevice.Serial, message => _logService.Info("Status", message)).ConfigureAwait(true);
            DeviceStatus = status;
            DeviceStatusText = status.ToString();
            StatusText = "Status do relogio atualizado.";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task UninstallSelectedAppAsync()
    {
        if (SelectedDevice is null || SelectedInstalledApp is null)
        {
            MessageBox.Show("Selecione um app instalado para remover.", "Tizen Loader BR Desktop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Deseja desinstalar {SelectedInstalledApp.PackageId}?",
            "Confirmar remoção",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await ExecuteBusyAsync("Desinstalando aplicativo", async () =>
        {
            var packageId = SelectedInstalledApp.PackageId;
            UninstallLogText = string.Empty;
            AppendUninstallLog($"Iniciando desinstalacao: {packageId}");
            var result = await _sdbService.UninstallAsync(SdbPath, SelectedDevice.Serial, SelectedInstalledApp.PackageId, message => _logService.Info("Desinstalação", message)).ConfigureAwait(true);
            AppendCommandOutputToUninstallLog(result.StandardOutput);
            AppendCommandOutputToUninstallLog(result.StandardError);
            if (result.Succeeded)
            {
                AppendUninstallLog($"Desinstalacao concluida: {packageId}");
                _logService.Info("Desinstalação", $"Remoção concluída: {SelectedInstalledApp.PackageId}");
                StatusText = $"Remoção concluída: {SelectedInstalledApp.PackageId}";
                MessageBox.Show($"App desinstalado: {SelectedInstalledApp.PackageId}", "Desinstalacao concluida", MessageBoxButton.OK, MessageBoxImage.Information);
                await ReloadInstalledAppsAsync("Atualizando lista após remoção").ConfigureAwait(true);
            }
            else
            {
                AppendUninstallLog($"Falha ao desinstalar {packageId}");
                _logService.Error("Desinstalação", $"Falha ao remover {SelectedInstalledApp.PackageId}");
                StatusText = $"Falha ao remover {SelectedInstalledApp.PackageId}";
                MessageBox.Show($"Falha ao desinstalar {SelectedInstalledApp.PackageId}. Veja o diagnostico para detalhes.", "Erro ao desinstalar", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshLibraryFromFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(DownloadFolder) || !Directory.Exists(DownloadFolder))
        {
            MessageBox.Show("Selecione uma pasta de downloads valida primeiro.", "Tizen Loader BR Desktop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await ExecuteBusyAsync("Atualizando biblioteca", async () =>
        {
            LibraryItems.Clear();
            SelectedLibraryItem = null;
            await _packageLibraryService.SaveAsync(LibraryItems).ConfigureAwait(true);
            CleanupLegacyLocalLibrary();

            var knownHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var files = Directory.EnumerateFiles(DownloadFolder, "*.*", SearchOption.AllDirectories)
                .Where(IsPackageSourceFile)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var importedCount = 0;
            var skippedCount = 0;

            foreach (var file in files)
            {
                IReadOnlyList<TizenPackageInfo> imported;
                try
                {
                    imported = await _packageImportService.ImportAsync(file, DownloadFolder).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _logService.Warn("Biblioteca", $"Falha ao importar {Path.GetFileName(file)}: {ex.Message}");
                    continue;
                }

                foreach (var candidate in imported)
                {
                    var analysis = await _packageAnalyzerService.AnalyzeAsync(candidate.StagedPath).ConfigureAwait(true);
                    if (!string.IsNullOrWhiteSpace(analysis.Sha256) && !knownHashes.Add(analysis.Sha256))
                    {
                        DeleteDuplicateExtractedPackage(candidate);
                        skippedCount++;
                        continue;
                    }

                    PrepareAnalysis(candidate, analysis);
                    var record = await _packageLibraryService.AddAsync(candidate, analysis).ConfigureAwait(true);
                    LibraryItems.Add(record);
                    SelectedLibraryItem = record;
                    importedCount++;
                }
            }

            LibraryView.Refresh();
            StatusText = $"Biblioteca atualizada: {importedCount} novo(s), {skippedCount} duplicado(s).";
            _logService.Info("Biblioteca", StatusText);
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SelectDownloadFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Selecionar pasta de downloads",
            InitialDirectory = Directory.Exists(DownloadFolder) ? DownloadFolder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        DownloadFolder = dialog.FolderName;
        await _settingsService.SaveAsync(_settings).ConfigureAwait(true);
        _logService.Info("Configuração", $"Pasta de downloads selecionada: {DownloadFolder}");
        await RefreshLibraryFromFolderAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task InstallSelectedLibraryItemAsync()
    {
        if (SelectedDevice is null || SelectedLibraryItem is null)
        {
            MessageBox.Show("Selecione um relógio e um item da biblioteca.", "Tizen Loader BR Desktop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var analysis = SelectedLibraryItem.Analysis;
        if (!analysis.SignatureFound)
        {
            var confirmUnsigned = MessageBox.Show(
                "Nenhuma assinatura foi detectada. Deseja tentar instalar mesmo assim?",
                "Confirmar instalação",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmUnsigned != MessageBoxResult.Yes)
            {
                return;
            }
        }

        await ExecuteBusyAsync("Instalando pacote no relógio", async () =>
        {
            InstallLogText = string.Empty;
            var result = await _sdbService.InstallAsync(
                SdbPath,
                SelectedDevice.Serial,
                SelectedLibraryItem.Package,
                message =>
                {
                    _logService.Info("Instalação", message);
                    AppendInstallLog(message);
                }).ConfigureAwait(true);

            if (result.Succeeded)
            {
                _logService.Info("Instalação", $"Instalação concluída: {SelectedLibraryItem.Package.FileName}");
                AppendInstallLog($"Instalacao concluida: {SelectedLibraryItem.Package.FileName}");
                StatusText = $"Instalação concluída: {SelectedLibraryItem.Package.FileName}";
                await ReloadInstalledAppsAsync("Atualizando lista após instalação").ConfigureAwait(true);
            }
            else
            {
                _logService.Error("Instalação", $"Falha ao instalar {SelectedLibraryItem.Package.FileName}");
                AppendInstallLog($"Falha ao instalar {SelectedLibraryItem.Package.FileName}");
                StatusText = $"Falha ao instalar {SelectedLibraryItem.Package.FileName}";
            }
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteSelectedLibraryItemAsync()
    {
        if (SelectedLibraryItem is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"Deseja excluir {SelectedLibraryItem.Package.FileName} da biblioteca e apagar o arquivo original do disco?",
            "Confirmar exclusão",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await ExecuteBusyAsync("Excluindo da biblioteca", async () =>
        {
            var warnings = await _packageLibraryService.RemoveAsync(SelectedLibraryItem.Id, deleteOriginal: true).ConfigureAwait(true);
            LibraryItems.Remove(SelectedLibraryItem);
            LibraryView.Refresh();
            SelectedLibraryItem = null;
            foreach (var warning in warnings)
            {
                _logService.Warn("Biblioteca", warning);
            }

            _logService.Info("Biblioteca", "Item removido da biblioteca local.");
            StatusText = "Item removido da biblioteca local.";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ReloadLibraryAsync()
    {
        var records = await _packageLibraryService.LoadAsync().ConfigureAwait(true);
        LibraryItems.Clear();
        foreach (var record in records)
        {
            LibraryItems.Add(record);
        }

        LibraryView.Refresh();
        _logService.Info("Biblioteca", $"{LibraryItems.Count} item(ns) carregado(s).");
    }

    [RelayCommand]
    private async Task OpenXdaAsync()
    {
        _browserService.Open("https://xdaforums.com/t/root-required-how-to-extract-gear-s3-watch-faces-and-apps-from-the-galaxy-store.4687851/");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CopyLogsAsync()
    {
        Clipboard.SetText(_logService.GetPlainText());
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ClearLogsAsync()
    {
        _logService.Clear();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SaveLogsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Arquivo de texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*",
            FileName = $"logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _logService.SaveToFile(dialog.FileName);
        _logService.Info("Logs", $"Logs salvos em {dialog.FileName}");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DetectSdbAsync()
    {
        var detected = _settingsService.DetectSdbPath();
        if (string.IsNullOrWhiteSpace(detected))
        {
            MessageBox.Show("Não foi possível encontrar o sdb.exe nos caminhos comuns.", "Tizen Loader BR Desktop", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SdbPath = detected;
        _logService.Info("Configuração", $"sdb.exe detectado em {detected}");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await _settingsService.SaveAsync(_settings).ConfigureAwait(true);
        _logService.Info("Configuração", "Configurações salvas.");
    }

    private async Task ExecuteBusyAsync(string message, Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyMessage = message;
        StatusText = message;
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logService.Error("Erro", ex.Message);
            StatusText = ex.Message;
        }
        finally
        {
            BusyMessage = string.Empty;
            IsBusy = false;
        }
    }

    private void AppendInstallLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        InstallLogText = string.IsNullOrWhiteSpace(InstallLogText)
            || InstallLogText.Equals("O log de instalacao aparece aqui.", StringComparison.OrdinalIgnoreCase)
                ? line
                : $"{InstallLogText}{Environment.NewLine}{line}";
    }

    private void AppendUninstallLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        UninstallLogText = string.IsNullOrWhiteSpace(UninstallLogText)
            || UninstallLogText.Equals("O log de desinstalacao aparece aqui.", StringComparison.OrdinalIgnoreCase)
                ? line
                : $"{UninstallLogText}{Environment.NewLine}{line}";
    }

    private void AppendCommandOutputToUninstallLog(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            AppendUninstallLog(line);
        }
    }

    private async Task ReloadInstalledAppsAsync(string message)
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var apps = await _sdbService.ListInstalledAppsAsync(SdbPath, SelectedDevice.Serial).ConfigureAwait(true);
        InstalledApps.Clear();
        foreach (var app in apps)
        {
            InstalledApps.Add(app);
        }

        _logService.Info("Dispositivos", $"{apps.Count} app(s) instalados encontrados.");
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusText = message;
        }
    }

    private static bool IsPackageSourceFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".wgt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tpk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteDuplicateExtractedPackage(TizenPackageInfo candidate)
    {
        if (!candidate.IsFromArchive
            || string.IsNullOrWhiteSpace(candidate.StagedPath)
            || string.IsNullOrWhiteSpace(candidate.SourcePath)
            || candidate.StagedPath.Equals(candidate.SourcePath, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(candidate.StagedPath))
        {
            return;
        }

        try
        {
            File.Delete(candidate.StagedPath);
        }
        catch
        {
        }
    }

    private void CleanupLegacyLocalLibrary()
    {
        try
        {
            if (Directory.Exists(AppPaths.LibraryFolder))
            {
                Directory.Delete(AppPaths.LibraryFolder, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logService.Warn("Biblioteca", $"Nao foi possivel limpar biblioteca antiga local: {ex.Message}");
        }
    }

    private static void PrepareAnalysis(TizenPackageInfo candidate, PackageAnalysisResult analysis)
    {
        if (!analysis.SignatureFound)
        {
            analysis.Warnings.Add("Assinatura ausente");
            analysis.Warnings.Add("Pode falhar por certificado");
        }

        if (!string.IsNullOrWhiteSpace(analysis.PackageId))
        {
            candidate.DisplayName = string.IsNullOrWhiteSpace(analysis.Name) ? candidate.DisplayName : analysis.Name;
        }
    }

    private async Task HandleImportedCandidatesAsync(IReadOnlyList<TizenPackageInfo> imported)
    {
        ImportCandidates.Clear();
        foreach (var candidate in imported)
        {
            ImportCandidates.Add(candidate);
        }

        if (imported.Count == 0)
        {
            _logService.Warn("Importação", "Nenhum pacote Tizen foi encontrado.");
            StatusText = "Nenhum pacote Tizen foi encontrado.";
            return;
        }

        if (imported.Count == 1)
        {
            await AddCandidateToLibraryAsync(imported[0]).ConfigureAwait(true);
            return;
        }

        SelectedImportCandidate = ImportCandidates.FirstOrDefault();
        _logService.Info("Importação", $"Encontrados {imported.Count} pacotes. Selecione um item para adicionar à biblioteca.");
        StatusText = $"Encontrados {imported.Count} pacotes. Selecione um item para adicionar à biblioteca.";
    }

    private async Task AddCandidateToLibraryAsync(TizenPackageInfo candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.StagedPath) || !File.Exists(candidate.StagedPath))
        {
            _logService.Error("Biblioteca", "Arquivo do candidato não encontrado.");
            return;
        }

        var analysis = await _packageAnalyzerService.AnalyzeAsync(candidate.StagedPath).ConfigureAwait(true);
        if (!analysis.SignatureFound)
        {
            analysis.Warnings.Add("Assinatura ausente");
            analysis.Warnings.Add("Pode falhar por certificado");
        }

        if (!string.IsNullOrWhiteSpace(analysis.PackageId))
        {
            candidate.DisplayName = string.IsNullOrWhiteSpace(analysis.Name) ? candidate.DisplayName : analysis.Name;
        }

        var record = await _packageLibraryService.AddAsync(candidate, analysis).ConfigureAwait(true);
        LibraryItems.Add(record);
        LibraryView.Refresh();
        SelectedLibraryItem = record;
        SelectedTabIndex = 1;
        _logService.Info("Biblioteca", $"Importado: {candidate.FileName}");
        StatusText = $"Importado: {candidate.FileName}";
    }
}
