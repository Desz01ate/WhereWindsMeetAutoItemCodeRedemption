using System.Collections.ObjectModel;
using System.ComponentModel;
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

public class MainViewModel : BaseViewModel
{
    private readonly IConfigService _configService;
    private readonly ICodeScraperService _scraperService;
    private readonly IGameWindowService _gameWindowService;
    private readonly IInputSimulationService _inputService;
    private readonly ICalibrationService _calibrationService;

    private GameWindowInfo? _gameInfo;
    private bool _isGameRunning;
    private string _gameStatusText = "Checking for game...";

    private ObservableCollection<RedeemCodeItem> _codes = new();
    private ICollectionView _filteredCodes;
    private CodeFilter _currentFilter = CodeFilter.All;
    private string _searchText = string.Empty;
    private string _manualCodeInput = string.Empty;

    private bool _isBusy;
    private bool _isRedeeming;
    private string _busyStatusText = string.Empty;
    private CancellationTokenSource? _redemptionCts;

    private int _progressValue;
    private int _progressMax;
    private string _progressText = string.Empty;

    private bool _confirmEachCode;
    private bool _spaceFallback;
    private bool _stopAfterOne;

    private ObservableCollection<string> _logEntries = new();

    public event Action? RequestOpenCalibration;
    public Func<string, Task<bool?>>? ConfirmRedemptionFunc; // returns true for Yes, false for No/Retry, null for Abort

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

    public bool ConfirmEachCode
    {
        get => _confirmEachCode;
        set => SetField(ref _confirmEachCode, value);
    }

    public bool SpaceFallback
    {
        get => _spaceFallback;
        set => SetField(ref _spaceFallback, value);
    }

    public bool StopAfterOne
    {
        get => _stopAfterOne;
        set => SetField(ref _stopAfterOne, value);
    }

    public int TotalCodesCount => _codes.Count;
    public int PendingCodesCount => _codes.Count(c => c.Status == CodeStatus.Pending);
    public int RedeemedCodesCount => _codes.Count(c => c.IsRedeemed);
    public int SelectedPendingCount => _codes.Count(c => c.IsSelected && c.Status == CodeStatus.Pending);

    public ObservableCollection<string> LogEntries => _logEntries;

    public AppConfig Config => _configService.CurrentConfig;

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

    public MainViewModel(
        IConfigService configService,
        ICodeScraperService scraperService,
        IGameWindowService gameWindowService,
        IInputSimulationService inputService,
        ICalibrationService calibrationService)
    {
        _configService = configService;
        _scraperService = scraperService;
        _gameWindowService = gameWindowService;
        _inputService = inputService;
        _calibrationService = calibrationService;

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

        Log($"Application loaded. Config path: {_configService.ConfigFilePath}");
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
        Log("Starting code discovery from sources...");

        var progress = new Progress<string>(Log);
        try
        {
            var scraped = await _scraperService.ScrapeAllAsync(
                _configService.CurrentConfig.Sources,
                _configService.CurrentConfig.ApiSources,
                _configService.CurrentConfig.Timing.PageTimeoutSeconds,
                progress);

            var redeemedSet = _configService.LoadRedeemedCodes();

            // Merge with current codes
            var currentMap = _codes.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

            foreach (var item in scraped)
            {
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

                if (!currentMap.ContainsKey(item.Code))
                {
                    _codes.Add(item);
                }
            }

            UpdateCounts();
            Log($"Discovered {scraped.Count} codes total. Pending redemption: {PendingCodesCount}.");
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
            Source = "Manual",
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

        if (!_configService.CurrentConfig.Ui.IsFullyCalibrated && !SpaceFallback)
        {
            var res = MessageBox.Show(
                "UI Coordinates have not been calibrated yet.\n\nWould you like to use Space-bar fallback for the submit button?",
                "Calibration Warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                SpaceFallback = true;
            }
            else
            {
                return;
            }
        }

        IsRedeeming = true;
        _redemptionCts = new CancellationTokenSource();
        ProgressMax = toRedeem.Count;
        ProgressValue = 0;

        Log($"=== Starting Redemption Process for {toRedeem.Count} codes ===");
        var logProgress = new Progress<string>(Log);

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
                        SpaceFallback,
                        logProgress,
                        _redemptionCts.Token);

                    if (ConfirmEachCode && ConfirmRedemptionFunc != null)
                    {
                        var confirmed = await ConfirmRedemptionFunc(item.Code);
                        if (confirmed == true)
                        {
                            item.Status = CodeStatus.Success;
                            _configService.AddRedeemedCode(item.Code);
                            Log($"[Success] Confirmed redemption for {item.Code}. Saved to state.");
                        }
                        else if (confirmed == false)
                        {
                            item.Status = CodeStatus.Pending;
                            Log($"[Skipped] Code {item.Code} kept as pending.");
                        }
                        else
                        {
                            Log("[Stop] Redemption aborted by user.");
                            item.Status = CodeStatus.Pending;
                            break;
                        }
                    }
                    else
                    {
                        item.Status = CodeStatus.Success;
                        _configService.AddRedeemedCode(item.Code);
                        Log($"[Success] Redeemed code {item.Code}. Saved to state.");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    item.Status = CodeStatus.Failed;
                    item.StatusMessage = ex.Message;
                    Log($"[Failed] Error redeeming {item.Code}: {ex.Message}");
                }

                UpdateCounts();

                if (StopAfterOne)
                {
                    Log("Single-code mode enabled: stopping after one redemption.");
                    break;
                }
            }

            Log("\n=== Redemption completed ===");
        }
        catch (OperationCanceledException)
        {
            Log("\n[Cancelled] Redemption process stopped by user.");
        }
        finally
        {
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
            _logEntries.Add(timestamped);
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
