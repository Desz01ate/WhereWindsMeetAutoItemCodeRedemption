using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

public interface ICalibrationService
{
    Task ShowTargetsAsync(
        nint hwnd,
        UiConfig ui,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default);

    NormalizedPoint? CaptureCurrentCursor(nint hwnd);
}
