using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using WhereWindsMeetItemCodeRedeemer.Common;
using WhereWindsMeetItemCodeRedeemer.Models;
using WhereWindsMeetItemCodeRedeemer.Services;

namespace WhereWindsMeetItemCodeRedeemer.ViewModels;

public enum CodeFilter
{
    All,
    PendingOnly,
    RedeemedOnly
}

public enum CodeSourceMode
{
    /// <summary>The websites and APIs listed in config.json ("sources" + "api_sources").</summary>
    ConfiguredSources,

    /// <summary>Only the codes.yar.gg API.</summary>
    CodesYarGg
}

public class MainViewModel : BaseViewModel
{
    private const string ManualSource = "Manual";

    private readonly IConfigService _configService;
    private readonly ICodeScraperService _scraperService;
    private readonly IGameWindowService _gameWindowService;
    private readonly IInputSimulationService _inputService;
    private readonly ICalibrationService _calibrationService;
    private readonly IHotkeyMonitor _hotkeyMonitor;
    private readonly IUpdateService _updateService;

    private GameWindowInfo? _gameInfo;
    private bool _isGameRunning;
    private string _gameStatusText = "Checking for game...";

    private ObservableCollection<RedeemCodeItem> _codes = new();
    private ICollectionView _filteredCodes;
    private CodeFilter _currentFilter = CodeFilter.All;
    private CodeSourceMode _codeSource = CodeSourceMode.ConfiguredSources;
    private string _searchText = string.Empty;
    private string _manualCodeInput = string.Empty;

    private bool _isBusy;
    private bool _isRedeeming;
    private string _busyStatusText = string.Empty;
    private CancellationTokenSource? _redemptionCts;

    private int _progressValue;
    private int _progressMax;
    private string _progressText = string.Empty;

    private bool _isCheckingForUpdate;
    private bool _isDownloadingUpdate;
    private bool _hasUpdateAvailable;
    private UpdateCheckResult? _availableUpdate;
    private double _updateDownloadProgress;
    private string _updateDownloadStatusText = string.Empty;
    private string? _downloadedUpdatePath;
    private CancellationTokenSource? _updateDownloadCts;

    private ObservableCollection<string> _logEntries = new();

    public event Action? RequestOpenCalibration;
    public event Action? RequestApplicationShutdown;

    public GameWindowInfo? GameInfo
    {
        get => _gameInfo;
        set
        {
            if (SetField(ref _gameInfo, value))
            {
                IsGameRunning = value != null;
                GameStatusText = value != null
                    ? $"Game Found: {value.ProcessName} (PID {value.ProcessId}) - {value.ClientWidth}x{value.ClientHeight}"
                    : $"Game window not found ({_configService.CurrentConfig.ProcessName}). Launch game to redeem.";
            }
        }
    }

    public bool IsGameRunning
    {
        get => _isGameRunning;
        set => SetField(ref _isGameRunning, value);
    }

    public string GameStatusText
    {
        get => _gameStatusText;
        set => SetField(ref _gameStatusText, value);
    }

    public ObservableCollection<RedeemCodeItem> Codes
    {
        get => _codes;
        set => SetField(ref _codes, value);
    }

    public ICollectionView FilteredCodes => _filteredCodes;

    public CodeFilter CurrentFilter
    {
        get => _currentFilter;
        set
        {
            if (SetField(ref _currentFilter, value))
            {
                _filteredCodes.Refresh();
            }
        }
    }

    /// <summary>Where "Fetch Latest Codes" looks for codes.</summary>
    public CodeSourceMode CodeSource
    {
        get => _codeSource;
        set => SetField(ref _codeSource, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                _filteredCodes.Refresh();
            }
        }
    }

    public string ManualCodeInput
    {
        get => _manualCodeInput;
        set => SetField(ref _manualCodeInput, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanOperate));
            }
        }
    }

    public bool IsRedeeming
    {
        get => _isRedeeming;
        set
        {
            if (SetField(ref _isRedeeming, value))
            {
                OnPropertyChanged(nameof(CanOperate));
            }
        }
    }

    public bool CanOperate => !IsBusy && !IsRedeeming;

    public string BusyStatusText
    {
        get => _busyStatusText;
        set => SetField(ref _busyStatusText, value);
    }

    public int ProgressValue
    {
        get => _progressValue;
        set => SetField(ref _progressValue, value);
    }

    public int ProgressMax
    {
        get => _progressMax;
        set => SetField(ref _progressMax, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetField(ref _progressText, value);
    }


    public int TotalCodesCount => _codes.Count;
    public int PendingCodesCount => _codes.Count(c => c.Status == CodeStatus.Pending);
    public int RedeemedCodesCount => _codes.Count(c => c.IsRedeemed);
    public int SelectedPendingCount => _codes.Count(c => c.IsSelected && c.Status == CodeStatus.Pending);

    public ObservableCollection<string> LogEntries => _logEntries;

    public AppConfig Config => _configService.CurrentConfig;
    public bool IsFullyCalibrated => _configService.CurrentConfig.Ui.IsFullyCalibrated;

    public string CurrentVersionText => $"v{UpdateService.FormatVersion(_updateService.CurrentVersion)}";

    public bool IsCheckingForUpdate
    {
        get => _isCheckingForUpdate;
        set
        {
            if (SetField(ref _isCheckingForUpdate, value))
            {
                OnPropertyChanged(nameof(CanCheckForUpdates));
                OnPropertyChanged(nameof(CheckForUpdatesButtonText));
            }
        }
    }

    public bool CanCheckForUpdates => !IsCheckingForUpdate && !IsDownloadingUpdate;
    public string CheckForUpdatesButtonText => IsCheckingForUpdate ? "Checking..." : "Check for Updates";

    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        set
        {
            if (SetField(ref _isDownloadingUpdate, value))
            {
                OnPropertyChanged(nameof(CanCheckForUpdates));
                OnPropertyChanged(nameof(IsUpdateBannerVisible));
                OnPropertyChanged(nameof(CanDownloadUpdate));
            }
        }
    }

    public bool HasUpdateAvailable
    {
        get => _hasUpdateAvailable;
        set
        {
            if (SetField(ref _hasUpdateAvailable, value))
            {
                OnPropertyChanged(nameof(IsUpdateBannerVisible));
                OnPropertyChanged(nameof(CanDownloadUpdate));
            }
        }
    }

    public UpdateCheckResult? AvailableUpdate
    {
        get => _availableUpdate;
        set
        {
            if (SetField(ref _availableUpdate, value))
            {
                OnPropertyChanged(nameof(UpdateBannerTitle));
                OnPropertyChanged(nameof(UpdateBannerSubtitle));
                OnPropertyChanged(nameof(CanDownloadUpdate));
            }
        }
    }

    public double UpdateDownloadProgress
    {
        get => _updateDownloadProgress;
        set => SetField(ref _updateDownloadProgress, value);
    }

    public string UpdateDownloadStatusText
    {
        get => _updateDownloadStatusText;
        set => SetField(ref _updateDownloadStatusText, value);
    }

    public string? DownloadedUpdatePath
    {
        get => _downloadedUpdatePath;
        set
        {
            if (SetField(ref _downloadedUpdatePath, value))
            {
                OnPropertyChanged(nameof(IsUpdateDownloaded));
                OnPropertyChanged(nameof(IsUpdateBannerVisible));
                OnPropertyChanged(nameof(CanDownloadUpdate));
            }
        }
    }

    public bool IsUpdateDownloaded => !string.IsNullOrEmpty(DownloadedUpdatePath);
    public bool IsUpdateBannerVisible => HasUpdateAvailable || IsDownloadingUpdate || IsUpdateDownloaded;
    public bool CanDownloadUpdate => HasUpdateAvailable && !IsDownloadingUpdate && !IsUpdateDownloaded && AvailableUpdate?.Asset != null;

    public string UpdateBannerTitle => AvailableUpdate == null
        ? string.Empty
        : $"Update Available: {AvailableUpdate.TagName} (Current: {CurrentVersionText})";

    public string UpdateBannerSubtitle => AvailableUpdate?.Asset != null
        ? $"{AvailableUpdate.Asset.Name} ({AvailableUpdate.Asset.FormattedSize})"
        : "Download available on GitHub Releases";

    public void RefreshCalibrationStatus()
    {
        OnPropertyChanged(nameof(IsFullyCalibrated));
    }


    // Commands
    public ICommand RefreshGameStatusCommand { get; }
    public ICommand FetchCodesCommand { get; }
    public ICommand AddManualCodeCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }
    public ICommand SelectPendingOnlyCommand { get; }
    public ICommand MarkSelectedAsRedeemedCommand { get; }
    public ICommand MarkSelectedAsPendingCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
    public ICommand StartRedemptionCommand { get; }
    public ICommand StopRedemptionCommand { get; }
    public ICommand InspectTargetsCommand { get; }
    public ICommand OpenCalibrationCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand DownloadUpdateCommand { get; }
    public ICommand CancelUpdateDownloadCommand { get; }
    public ICommand InstallUpdateCommand { get; }
    public ICommand OpenUpdateFolderCommand { get; }
    public ICommand OpenReleaseNotesCommand { get; }
    public ICommand DismissUpdateBannerCommand { get; }

    public MainViewModel(
        IConfigService configService,
        ICodeScraperService scraperService,
        IGameWindowService gameWindowService,
        IInputSimulationService inputService,
        ICalibrationService calibrationService,
        IHotkeyMonitor hotkeyMonitor,
        IUpdateService? updateService = null)
    {
        _configService = configService;
        _scraperService = scraperService;
        _gameWindowService = gameWindowService;
        _inputService = inputService;
        _calibrationService = calibrationService;
        _hotkeyMonitor = hotkeyMonitor;
        _updateService = updateService ?? new UpdateService();

        _filteredCodes = CollectionViewSource.GetDefaultView(_codes);
        _filteredCodes.Filter = FilterCodeItem;

        RefreshGameStatusCommand = new RelayCommand(RefreshGameStatus);
        FetchCodesCommand = new AsyncRelayCommand(FetchCodesAsync);
        AddManualCodeCommand = new RelayCommand(AddManualCode);
        SelectAllCommand = new RelayCommand(() => SetSelection(true));
        DeselectAllCommand = new RelayCommand(() => SetSelection(false));
        SelectPendingOnlyCommand = new RelayCommand(SelectPendingOnly);
        MarkSelectedAsRedeemedCommand = new RelayCommand(MarkSelectedAsRedeemed);
        MarkSelectedAsPendingCommand = new RelayCommand(MarkSelectedAsPending);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected);
        StartRedemptionCommand = new AsyncRelayCommand(StartRedemptionAsync);
        StopRedemptionCommand = new RelayCommand(StopRedemption);
        InspectTargetsCommand = new AsyncRelayCommand(InspectTargetsAsync);
        OpenCalibrationCommand = new RelayCommand(() => RequestOpenCalibration?.Invoke());
        ClearLogCommand = new RelayCommand(_logEntries.Clear);
        CheckForUpdatesCommand = new AsyncRelayCommand(() => CheckForUpdatesAsync(silent: false));
        DownloadUpdateCommand = new AsyncRelayCommand(DownloadUpdateAsync);
        CancelUpdateDownloadCommand = new RelayCommand(CancelUpdateDownload);
        InstallUpdateCommand = new RelayCommand(InstallUpdate);
        OpenUpdateFolderCommand = new RelayCommand(OpenUpdateFolder);
        OpenReleaseNotesCommand = new RelayCommand(OpenReleaseNotes);
        DismissUpdateBannerCommand = new RelayCommand(DismissUpdateBanner);

        Log($"Application loaded ({CurrentVersionText}). Config path: {_configService.ConfigFilePath}");
        Log($"State file path: {_configService.StateFilePath}");

        // Initial game detection
        RefreshGameStatus();

        // Load existing redeemed codes
        LoadExistingRedeemedState();
    }

    public void RefreshGameStatus()
    {
        GameInfo = _gameWindowService.FindGameWindow(_configService.CurrentConfig.ProcessName);
        if (GameInfo != null)
        {
            Log($"[Game Detected] {GameInfo}");
        }
    }

    private void LoadExistingRedeemedState()
    {
        var redeemed = _configService.LoadRedeemedCodes();
        Log($"Loaded {redeemed.Count} previously redeemed codes from local state.");
    }

    public async Task FetchCodesAsync()
    {
        if (IsBusy || IsRedeeming) return;

        IsBusy = true;
        BusyStatusText = "Scraping code sources...";

        var config = _configService.CurrentConfig;
        IEnumerable<string> htmlSources;
        IEnumerable<string> apiSources;
        if (CodeSource == CodeSourceMode.CodesYarGg)
        {
            htmlSources = [];
            apiSources = [CodeScraperService.CodesYarGgApiUrl];
            Log("Starting code discovery from codes.yar.gg only...");
        }
        else
        {
            htmlSources = config.Sources;
            apiSources = config.ApiSources;
            Log("Starting code discovery from the sources in config.json...");
        }

        var progress = new Progress<string>(Log);
        try
        {
            var scraped = await _scraperService.ScrapeAllAsync(
                htmlSources,
                apiSources,
                config.Timing.PageTimeoutSeconds,
                progress);

            if (scraped.Count == 0)
            {
                // Every source failed or returned nothing: don't wipe the list the user is looking at.
                Log("[Warning] No codes were found, so the current list was kept.");
                return;
            }

            var redeemedSet = _configService.LoadRedeemedCodes();

            // Replace the previously fetched codes with this result, so switching sources shows
            // only the selected source. Codes the user added by hand are kept.
            var manualCodes = _codes.Where(c => c.Source == ManualSource).ToList();
            var manualSet = new HashSet<string>(manualCodes.Select(c => c.Code), StringComparer.OrdinalIgnoreCase);

            _codes.Clear();
            foreach (var item in manualCodes)
            {
                _codes.Add(item);
            }

            foreach (var item in scraped)
            {
                if (manualSet.Contains(item.Code))
                {
                    continue;
                }

                if (redeemedSet.Contains(item.Code))
                {
                    item.Status = CodeStatus.Redeemed;
                    item.IsSelected = false;
                }
                else
                {
                    item.Status = CodeStatus.Pending;
                    item.IsSelected = true;
                }

                _codes.Add(item);
            }

            UpdateCounts();
            var keptNote = manualCodes.Count > 0 ? $" Kept {manualCodes.Count} manually added code(s)." : string.Empty;
            Log($"Discovered {scraped.Count} codes total. Pending redemption: {PendingCodesCount}.{keptNote}");
        }
        catch (Exception ex)
        {
            Log($"[Error] Fetching codes failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyStatusText = string.Empty;
        }
    }

    private void AddManualCode()
    {
        if (string.IsNullOrWhiteSpace(ManualCodeInput)) return;

        var code = ManualCodeInput.Trim().ToUpperInvariant();
        if (_codes.Any(c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
        {
            Log($"Code '{code}' already exists in the list.");
            return;
        }

        var redeemedSet = _configService.LoadRedeemedCodes();
        var isRedeemed = redeemedSet.Contains(code);

        var item = new RedeemCodeItem
        {
            Code = code,
            Source = ManualSource,
            Status = isRedeemed ? CodeStatus.Redeemed : CodeStatus.Pending,
            IsSelected = !isRedeemed
        };

        _codes.Add(item);
        ManualCodeInput = string.Empty;
        UpdateCounts();
        Log($"Manually added code: {code} (Status: {item.StatusText})");
    }

    private void SetSelection(bool selected)
    {
        foreach (var item in _filteredCodes.Cast<RedeemCodeItem>())
        {
            item.IsSelected = selected;
        }
        UpdateCounts();
    }

    private void SelectPendingOnly()
    {
        foreach (var item in _codes)
        {
            item.IsSelected = item.Status == CodeStatus.Pending;
        }
        UpdateCounts();
    }

    private void MarkSelectedAsRedeemed()
    {
        var selected = _codes.Where(c => c.IsSelected).ToList();
        if (!selected.Any()) return;

        var redeemed = _configService.LoadRedeemedCodes();
        foreach (var item in selected)
        {
            item.Status = CodeStatus.Redeemed;
            item.IsSelected = false;
            redeemed.Add(item.Code);
        }

        _configService.SaveRedeemedCodes(redeemed);
        UpdateCounts();
        Log($"Marked {selected.Count} codes as Redeemed.");
    }

    private void MarkSelectedAsPending()
    {
        var selected = _codes.Where(c => c.IsSelected).ToList();
        if (!selected.Any()) return;

        var redeemed = _configService.LoadRedeemedCodes();
        foreach (var item in selected)
        {
            item.Status = CodeStatus.Pending;
            redeemed.Remove(item.Code);
        }

        _configService.SaveRedeemedCodes(redeemed);
        UpdateCounts();
        Log($"Marked {selected.Count} codes as Pending.");
    }

    private void RemoveSelected()
    {
        var selected = _codes.Where(c => c.IsSelected).ToList();
        foreach (var item in selected)
        {
            _codes.Remove(item);
        }
        UpdateCounts();
        Log($"Removed {selected.Count} codes from list.");
    }

    public async Task StartRedemptionAsync()
    {
        if (IsRedeeming || IsBusy) return;

        RefreshGameStatus();
        if (GameInfo == null)
        {
            MessageBox.Show(
                $"Could not find the visible game window for '{_configService.CurrentConfig.ProcessName}'.\n\nPlease launch Where Winds Meet and make sure it is not minimized.",
                "Game Window Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var toRedeem = _codes.Where(c => c.IsSelected && c.Status == CodeStatus.Pending).ToList();
        if (!toRedeem.Any())
        {
            MessageBox.Show(
                "No pending codes are selected for redemption.\n\nPlease check the codes you wish to redeem.",
                "No Codes Selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!_configService.CurrentConfig.Ui.IsFullyCalibrated)
        {
            var res = MessageBox.Show(
                "Redemption coordinates are not yet configured.\n\nAll 4 targets (Exchange Button, Code Input, Submit Button, and Cancel Button) must be calibrated before redeeming codes.\n\nWould you like to open the Calibration Window now?",
                "Calibration Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                RequestOpenCalibration?.Invoke();
            }

            return;
        }

        IsRedeeming = true;
        var redemptionCts = new CancellationTokenSource();
        _redemptionCts = redemptionCts;
        ProgressMax = toRedeem.Count;
        ProgressValue = 0;

        Log($"=== Starting Redemption Process for {toRedeem.Count} codes ===");
        Log("Press Esc at any time to stop (works even while the game window is focused).");
        var logProgress = new Progress<string>(Log);

        // Esc stops the run even while the game window has focus. The callback runs on a
        // background thread: cancel first so the stop is not delayed by logging.
        var stopHotkey = _hotkeyMonitor.Watch(NativeMethods.VK_ESCAPE, () =>
        {
            if (redemptionCts.IsCancellationRequested)
                return;

            redemptionCts.Cancel();
            Log("[Esc] Stop key pressed. Stopping redemption...");
        });

        try
        {
            for (int i = 0; i < toRedeem.Count; i++)
            {
                _redemptionCts.Token.ThrowIfCancellationRequested();

                var item = toRedeem[i];
                ProgressValue = i + 1;
                ProgressText = $"Redeeming {i + 1} of {toRedeem.Count}: {item.Code}";
                item.Status = CodeStatus.Processing;
                Log($"\n>>> [{i + 1}/{toRedeem.Count}] Redeeming code: {item.Code}");

                try
                {
                    await _inputService.RedeemOneAsync(
                        GameInfo.Hwnd,
                        item.Code,
                        _configService.CurrentConfig.Ui,
                        _configService.CurrentConfig.Timing.UiDelaySeconds,
                        _configService.CurrentConfig.Timing.ResultWaitSeconds,
                        logProgress,
                        _redemptionCts.Token);

                    item.Status = CodeStatus.Success;
                    _configService.AddRedeemedCode(item.Code);
                    Log($"[Success] Redeemed code {item.Code}. Saved to state.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    item.Status = CodeStatus.Failed;
                    item.StatusMessage = ex.Message;
                    Log($"[Failed] Error redeeming {item.Code}: {ex.Message}");
                }

                UpdateCounts();

            }

            Log("\n=== Redemption completed ===");
        }
        catch (OperationCanceledException)
        {
            // The interrupted code never finished, so keep it available for the next run.
            foreach (var item in toRedeem.Where(c => c.Status == CodeStatus.Processing))
            {
                item.Status = CodeStatus.Pending;
            }

            Log("\n[Cancelled] Redemption process stopped by user.");
        }
        finally
        {
            stopHotkey.Dispose();
            IsRedeeming = false;
            ProgressText = "Finished";
            _redemptionCts = null;
            UpdateCounts();
        }
    }

    public void StopRedemption()
    {
        if (IsRedeeming)
        {
            _redemptionCts?.Cancel();
            Log("Stopping redemption requested...");
        }
    }

    public async Task InspectTargetsAsync()
    {
        if (IsBusy || IsRedeeming) return;

        RefreshGameStatus();
        if (GameInfo == null)
        {
            MessageBox.Show(
                $"Could not find game window '{_configService.CurrentConfig.ProcessName}'. Launch game first.",
                "Game Window Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        BusyStatusText = "Inspecting UI target locations...";
        Log("Inspecting configured UI targets in the game window...");

        var progress = new Progress<string>(Log);
        try
        {
            await _calibrationService.ShowTargetsAsync(
                GameInfo.Hwnd,
                _configService.CurrentConfig.Ui,
                progress);
        }
        catch (Exception ex)
        {
            Log($"[Error] Inspect targets failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyStatusText = string.Empty;
        }
    }

    public async Task CheckForUpdatesAsync(bool silent = false)
    {
        if (IsCheckingForUpdate || IsDownloadingUpdate) return;

        IsCheckingForUpdate = true;
        if (!silent)
        {
            Log($"Checking GitHub Releases for updates (current: {CurrentVersionText})...");
        }

        try
        {
            var result = await _updateService.CheckForUpdateAsync();

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                Log($"[Update] {result.ErrorMessage}");
                if (!silent && Application.Current != null)
                {
                    MessageBox.Show(
                        result.ErrorMessage,
                        "Check for Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return;
            }

            if (result.IsUpdateAvailable)
            {
                AvailableUpdate = result;
                HasUpdateAvailable = true;
                DownloadedUpdatePath = null;
                UpdateDownloadProgress = 0;
                UpdateDownloadStatusText = string.Empty;

                var assetDesc = result.Asset != null
                    ? $"{result.Asset.Name} ({result.Asset.FormattedSize})"
                    : "no downloadable asset attached";
                Log($"[Update] New version available: {result.TagName} ({assetDesc}).");
            }
            else
            {
                AvailableUpdate = result;
                HasUpdateAvailable = false;

                var latestStr = result.LatestVersion != null
                    ? $"v{UpdateService.FormatVersion(result.LatestVersion)}"
                    : CurrentVersionText;
                if (!silent)
                {
                    Log($"[Update] You are up to date (current: {CurrentVersionText}, latest: {latestStr}).");
                    if (Application.Current != null)
                    {
                        MessageBox.Show(
                            $"You are running the latest version ({CurrentVersionText}).",
                            "Up to Date",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[Update] Failed to check for updates: {ex.Message}");
            if (!silent && Application.Current != null)
            {
                MessageBox.Show(
                    $"Failed to check for updates:\n{ex.Message}",
                    "Update Check Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    public async Task DownloadUpdateAsync()
    {
        if (IsDownloadingUpdate || AvailableUpdate?.Asset == null) return;

        var asset = AvailableUpdate.Asset;
        IsDownloadingUpdate = true;
        DownloadedUpdatePath = null;
        UpdateDownloadProgress = 0;
        UpdateDownloadStatusText = $"Downloading {asset.Name}...";
        _updateDownloadCts = new CancellationTokenSource();

        Log($"[Update] Downloading {asset.Name} ({asset.FormattedSize})...");

        var progress = new Progress<DownloadProgress>(p =>
        {
            if (p.Percentage.HasValue)
            {
                UpdateDownloadProgress = p.Percentage.Value;
                UpdateDownloadStatusText =
                    $"Downloading {asset.Name}: {p.Percentage.Value:F0}% ({ReleaseAssetInfo.FormatBytes(p.BytesDownloaded)} / {ReleaseAssetInfo.FormatBytes(p.TotalBytes!.Value)})";
            }
            else
            {
                UpdateDownloadStatusText =
                    $"Downloading {asset.Name}: {ReleaseAssetInfo.FormatBytes(p.BytesDownloaded)}";
            }
        });

        try
        {
            var downloadedPath = await _updateService.DownloadUpdateAsync(
                asset,
                destinationDirectory: null,
                progress: progress,
                cancellationToken: _updateDownloadCts.Token);

            DownloadedUpdatePath = downloadedPath;
            UpdateDownloadProgress = 100;
            UpdateDownloadStatusText = $"Downloaded {asset.Name}. Ready to install.";
            Log($"[Update] Artifact downloaded to: {downloadedPath}");
        }
        catch (OperationCanceledException)
        {
            UpdateDownloadProgress = 0;
            UpdateDownloadStatusText = string.Empty;
            Log("[Update] Download cancelled by user.");
        }
        catch (Exception ex)
        {
            UpdateDownloadProgress = 0;
            UpdateDownloadStatusText = string.Empty;
            Log($"[Update] Download failed: {ex.Message}");
            if (Application.Current != null)
            {
                MessageBox.Show(
                    $"Failed to download update artifact:\n{ex.Message}",
                    "Download Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            IsDownloadingUpdate = false;
            _updateDownloadCts?.Dispose();
            _updateDownloadCts = null;
        }
    }

    public void CancelUpdateDownload()
    {
        if (IsDownloadingUpdate)
        {
            _updateDownloadCts?.Cancel();
        }
    }

    public void InstallUpdate()
    {
        if (string.IsNullOrEmpty(DownloadedUpdatePath) || !File.Exists(DownloadedUpdatePath))
        {
            Log("[Update] Downloaded artifact not found.");
            return;
        }

        if (Application.Current != null)
        {
            var confirm = MessageBox.Show(
                "The application will close, extract the new version (preserving your config.json and redeemed_codes.json), and restart automatically.\n\nProceed with installation?",
                "Install Update & Restart",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;
        }

        try
        {
            Log("[Update] Launching updater and restarting application...");
            if (_updateService.LaunchUpdaterAndApply(DownloadedUpdatePath))
            {
                RequestApplicationShutdown?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log($"[Update] Failed to apply update: {ex.Message}");
            if (Application.Current != null)
            {
                MessageBox.Show(
                    $"Failed to launch updater:\n{ex.Message}\n\nYou can still extract the downloaded archive manually.",
                    "Install Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    public void OpenUpdateFolder()
    {
        if (string.IsNullOrEmpty(DownloadedUpdatePath)) return;

        var dir = Path.GetDirectoryName(DownloadedUpdatePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{DownloadedUpdatePath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log($"[Update] Could not open folder: {ex.Message}");
        }
    }

    public void OpenReleaseNotes()
    {
        var url = !string.IsNullOrWhiteSpace(AvailableUpdate?.HtmlUrl)
            ? AvailableUpdate.HtmlUrl
            : _updateService.ReleasesPageUrl;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log($"[Update] Could not open release page: {ex.Message}");
        }
    }

    public void DismissUpdateBanner()
    {
        if (IsDownloadingUpdate) return;
        HasUpdateAvailable = false;
        DownloadedUpdatePath = null;
        UpdateDownloadProgress = 0;
        UpdateDownloadStatusText = string.Empty;
    }

    private void Log(string message)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (Application.Current != null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _logEntries.Add(timestamped);
                if (_logEntries.Count > 500)
                {
                    _logEntries.RemoveAt(0);
                }
            });
        }
        else
        {
            // No WPF dispatcher (e.g. unit tests); the Esc monitor can log from a background thread.
            lock (_logEntries)
            {
                _logEntries.Add(timestamped);
            }
        }
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(TotalCodesCount));
        OnPropertyChanged(nameof(PendingCodesCount));
        OnPropertyChanged(nameof(RedeemedCodesCount));
        OnPropertyChanged(nameof(SelectedPendingCount));
    }

    private bool FilterCodeItem(object obj)
    {
        if (obj is not RedeemCodeItem item) return false;

        // Status filter
        if (CurrentFilter == CodeFilter.PendingOnly && item.Status != CodeStatus.Pending)
            return false;

        if (CurrentFilter == CodeFilter.RedeemedOnly && !item.IsRedeemed)
            return false;

        // Search query filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            return item.Code.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                   item.Source.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}
