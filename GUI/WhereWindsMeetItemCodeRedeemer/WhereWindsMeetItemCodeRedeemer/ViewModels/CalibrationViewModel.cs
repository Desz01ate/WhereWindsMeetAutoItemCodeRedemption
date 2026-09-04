using System.Media;
using System.Windows.Input;
using WhereWindsMeetItemCodeRedeemer.Common;
using WhereWindsMeetItemCodeRedeemer.Models;
using WhereWindsMeetItemCodeRedeemer.Services;

namespace WhereWindsMeetItemCodeRedeemer.ViewModels;

public class CalibrationViewModel : BaseViewModel
{
    private readonly IGameWindowService _gameWindowService;
    private readonly IInputSimulationService _inputService;
    private readonly IConfigService _configService;

    private nint _gameHwnd;
    private int _clientWidth;
    private int _clientHeight;

    private NormalizedPoint? _exchangeButton;
    private NormalizedPoint? _codeInput;
    private NormalizedPoint? _submitButton;
    private NormalizedPoint? _cancelButton;

    private string _statusMessage = "Select a target or click Wizard to start calibrating.";
    private bool _isCapturing;
    private int _countdownSeconds;
    private CancellationTokenSource? _countdownCts;

    public NormalizedPoint? ExchangeButton
    {
        get => _exchangeButton;
        set => SetField(ref _exchangeButton, value);
    }

    public NormalizedPoint? CodeInput
    {
        get => _codeInput;
        set => SetField(ref _codeInput, value);
    }

    public NormalizedPoint? SubmitButton
    {
        get => _submitButton;
        set => SetField(ref _submitButton, value);
    }

    public NormalizedPoint? CancelButton
    {
        get => _cancelButton;
        set => SetField(ref _cancelButton, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set
        {
            if (SetField(ref _isCapturing, value))
            {
                OnPropertyChanged(nameof(CanOperate));
            }
        }
    }

    public int CountdownSeconds
    {
        get => _countdownSeconds;
        set => SetField(ref _countdownSeconds, value);
    }

    public bool CanOperate => !IsCapturing;
    public string WindowResolutionText => $"{_clientWidth} x {_clientHeight}";

    public ICommand CaptureTargetCommand { get; }
    public ICommand TestTargetCommand { get; }
    public ICommand CancelCaptureCommand { get; }
    public ICommand SaveCalibrationCommand { get; }
    public ICommand ResetToDefaultsCommand { get; }

    public event Action? RequestClose;

    public CalibrationViewModel(
        IGameWindowService gameWindowService,
        IInputSimulationService inputService,
        IConfigService configService,
        nint gameHwnd,
        int clientWidth,
        int clientHeight)
    {
        _gameWindowService = gameWindowService;
        _inputService = inputService;
        _configService = configService;
        _gameHwnd = gameHwnd;
        _clientWidth = clientWidth;
        _clientHeight = clientHeight;

        var ui = _configService.CurrentConfig.Ui;
        _exchangeButton = ui.ExchangeButton;
        _codeInput = ui.CodeInput;
        _submitButton = ui.SubmitButton;
        _cancelButton = ui.CancelButton;

        CaptureTargetCommand = new AsyncRelayCommand(targetName => StartCountdownCaptureAsync(targetName as string));
        TestTargetCommand = new RelayCommand(targetName => TestTarget(targetName as string));
        CancelCaptureCommand = new RelayCommand(CancelCapture);
        SaveCalibrationCommand = new RelayCommand(SaveCalibration);
        ResetToDefaultsCommand = new RelayCommand(ResetToDefaults);
    }

    private async Task StartCountdownCaptureAsync(string? targetName)
    {
        if (string.IsNullOrEmpty(targetName) || IsCapturing) return;

        IsCapturing = true;
        _countdownCts = new CancellationTokenSource();

        try
        {
            StatusMessage = $"Bring game to front. Hover mouse over [{targetName}]...";
            _gameWindowService.FocusGameWindow(_gameHwnd);

            for (int i = 3; i > 0; i--)
            {
                CountdownSeconds = i;
                StatusMessage = $"Capturing [{targetName}] in {i}... Hover over the target!";
                await Task.Delay(1000, _countdownCts.Token);
            }

            // Capture point
            var point = _gameWindowService.GetCursorClientNormalized(_gameHwnd);
            if (point != null)
            {
                SystemSounds.Asterisk.Play();
                SetTargetPoint(targetName, point);
                StatusMessage = $"Captured [{targetName}] at {point}.";
            }
            else
            {
                StatusMessage = $"Failed to capture cursor for [{targetName}]. Ensure cursor is inside the game window.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Capture cancelled.";
        }
        finally
        {
            IsCapturing = false;
            CountdownSeconds = 0;
            _countdownCts = null;
        }
    }

    private void SetTargetPoint(string targetName, NormalizedPoint point)
    {
        switch (targetName.ToLowerInvariant())
        {
            case "exchange":
            case "exchange_button":
                ExchangeButton = point;
                break;
            case "code_input":
            case "input":
                CodeInput = point;
                break;
            case "submit":
            case "submit_button":
                SubmitButton = point;
                break;
            case "cancel":
            case "cancel_button":
                CancelButton = point;
                break;
        }
    }

    private void TestTarget(string? targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return;

        NormalizedPoint? point = targetName.ToLowerInvariant() switch
        {
            "exchange" or "exchange_button" => ExchangeButton,
            "code_input" or "input" => CodeInput,
            "submit" or "submit_button" => SubmitButton,
            "cancel" or "cancel_button" => CancelButton,
            _ => null
        };

        if (point == null)
        {
            StatusMessage = $"[{targetName}] is not calibrated yet.";
            return;
        }

        if (_gameWindowService.FocusGameWindow(_gameHwnd))
        {
            _inputService.MoveCursorToNormalized(_gameHwnd, point);
            StatusMessage = $"Moved cursor to [{targetName}] at {point}.";
        }
        else
        {
            StatusMessage = "Could not focus game window.";
        }
    }

    private void CancelCapture()
    {
        _countdownCts?.Cancel();
    }

    private void SaveCalibration()
    {
        var ui = _configService.CurrentConfig.Ui;
        ui.ExchangeButton = ExchangeButton;
        ui.CodeInput = CodeInput;
        ui.SubmitButton = SubmitButton;
        ui.CancelButton = CancelButton;
        ui.CoordinateSpace = "client_normalized";

        if (_clientWidth > 0 && _clientHeight > 0)
        {
            ui.CalibratedClientSize = new ClientSize(_clientWidth, _clientHeight);
        }

        _configService.SaveConfig(_configService.CurrentConfig);
        StatusMessage = "Calibration saved successfully!";
        RequestClose?.Invoke();
    }

    private void ResetToDefaults()
    {
        ExchangeButton = new NormalizedPoint(0.552907, 0.246528);
        CodeInput = new NormalizedPoint(0.625291, 0.517361);
        SubmitButton = new NormalizedPoint(0.520349, 0.659028);
        CancelButton = new NormalizedPoint(0.462209, 0.65);
        StatusMessage = "Reset to default 3440x1440 coordinates.";
    }
}
