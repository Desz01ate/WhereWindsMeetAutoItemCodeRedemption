using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

public interface IGameWindowService
{
    void EnsureDpiAwareness();
    GameWindowInfo? FindGameWindow(string? processName = null, int? processId = null);
    bool FocusGameWindow(nint hwnd);
    (int Width, int Height) GetClientSize(nint hwnd);
    (int ScreenX, int ScreenY) ClientToScreen(nint hwnd, int clientX, int clientY);
    (int ClientX, int ClientY) ScreenToClient(nint hwnd, int screenX, int screenY);
    NormalizedPoint? GetCursorClientNormalized(nint hwnd);
}
