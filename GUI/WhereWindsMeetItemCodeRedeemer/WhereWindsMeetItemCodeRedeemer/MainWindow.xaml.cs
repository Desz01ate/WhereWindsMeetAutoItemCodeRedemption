using System.Collections.Specialized;
using System.Windows;
using WhereWindsMeetItemCodeRedeemer.Services;
using WhereWindsMeetItemCodeRedeemer.ViewModels;

namespace WhereWindsMeetItemCodeRedeemer;

public partial class MainWindow : Window
{
    private readonly IConfigService _configService;
    private readonly ICodeScraperService _scraperService;
    private readonly IGameWindowService _gameWindowService;
    private readonly IInputSimulationService _inputService;
    private readonly ICalibrationService _calibrationService;
    private readonly IUpdateService _updateService;
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        if (App.IsRunningAsAdministrator())
        {
            Title += " [Administrator]";
        }

        _configService = new ConfigService();
        _scraperService = new CodeScraperService();
        _gameWindowService = new GameWindowService();
        _inputService = new InputSimulationService(_gameWindowService);
        _calibrationService = new CalibrationService(_gameWindowService, _inputService);
        _updateService = new UpdateService();

        _viewModel = new MainViewModel(
            _configService,
            _scraperService,
            _gameWindowService,
            _inputService,
            _calibrationService,
            _updateService,
            new EscapeKeyMonitor());

        DataContext = _viewModel;

        _viewModel.RequestOpenCalibration += OnRequestOpenCalibration;
        _viewModel.RequestApplicationShutdown += () => Application.Current?.Shutdown();

        Loaded += async (_, _) => await _viewModel.CheckForUpdatesAsync(silent: true);

        // Auto-scroll activity log
        if (LogListBox.ItemsSource is INotifyCollectionChanged notify)
        {
            notify.CollectionChanged += (_, _) =>
            {
                if (LogListBox.Items.Count > 0)
                {
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                }
            };
        }
    }

    private void OnRequestOpenCalibration()
    {
        _viewModel.RefreshGameStatus();
        var gameInfo = _viewModel.GameInfo;

        int clientWidth = gameInfo?.ClientWidth ?? _configService.CurrentConfig.Ui.CalibratedClientSize?.Width ?? 3440;
        int clientHeight = gameInfo?.ClientHeight ??
                           _configService.CurrentConfig.Ui.CalibratedClientSize?.Height ?? 1440;

        var calVm = new CalibrationViewModel(
            _gameWindowService,
            _inputService,
            _configService,
            gameInfo?.Hwnd ?? 0,
            clientWidth,
            clientHeight);

        var calWindow = new CalibrationWindow(calVm)
        {
            Owner = this
        };

        calWindow.ShowDialog();
        _viewModel.RefreshCalibrationStatus();
    }
}