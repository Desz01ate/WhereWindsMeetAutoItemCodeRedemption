using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

public class CalibrationService : ICalibrationService
{
    private readonly IGameWindowService _gameWindowService;
    private readonly IInputSimulationService _inputService;

    public CalibrationService(IGameWindowService gameWindowService, IInputSimulationService inputService)
    {
        _gameWindowService = gameWindowService;
        _inputService = inputService;
    }

    public async Task ShowTargetsAsync(
        nint hwnd,
        UiConfig ui,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (!_gameWindowService.FocusGameWindow(hwnd))
        {
            throw new InvalidOperationException("Could not focus the game window; check that the game is running and visible.");
        }

        var (width, height) = _gameWindowService.GetClientSize(hwnd);
        log?.Report($"Game client area: {width}x{height}. Inspecting targets...");

        var targets = new (string Name, NormalizedPoint? Point)[]
        {
            ("Exchange Button", ui.ExchangeButton),
            ("Code Input", ui.CodeInput),
            ("Submit Button", ui.SubmitButton),
            ("Cancel / Close Button", ui.CancelButton)
        };

        foreach (var (name, point) in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (point == null)
            {
                log?.Report($"[Target] {name}: Not calibrated yet.");
                continue;
            }

            int cx = (int)Math.Round(width * point.X);
            int cy = (int)Math.Round(height * point.Y);
            var (sx, sy) = _gameWindowService.ClientToScreen(hwnd, cx, cy);

            log?.Report($"[Target] Moving cursor to {name}: ({cx}, {cy}) screen=({sx}, {sy})...");
            _inputService.MoveCursorToNormalized(hwnd, point);

            await Task.Delay(1000, cancellationToken);
        }

        log?.Report("Target inspection complete.");
    }

    public NormalizedPoint? CaptureCurrentCursor(nint hwnd)
    {
        return _gameWindowService.GetCursorClientNormalized(hwnd);
    }
}
