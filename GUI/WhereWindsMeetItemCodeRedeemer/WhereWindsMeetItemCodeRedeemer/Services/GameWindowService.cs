using System.IO;
using WhereWindsMeetItemCodeRedeemer.Common;
using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

public class GameWindowService : IGameWindowService
{
    private bool _dpiConfigured;

    public void EnsureDpiAwareness()
    {
        if (_dpiConfigured) return;

        try
        {
            // Try Per-Monitor V2 awareness context (-4)
            var res = NativeMethods.SetProcessDpiAwarenessContext((nint)(-4));
            if (res != 0)
            {
                _dpiConfigured = true;
                return;
            }
        }
        catch
        {
            // Fallback
        }

        try
        {
            NativeMethods.SetProcessDPIAware();
            _dpiConfigured = true;
        }
        catch
        {
            // Ignore if already set or unsupported
        }
    }

    public GameWindowInfo? FindGameWindow(string? processName = null, int? processId = null)
    {
        EnsureDpiAwareness();

        var targetName = (processName ?? "wwm.exe").Trim().ToLowerInvariant();
        GameWindowInfo? match = null;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
                return true;

            var candName = GetProcessName((int)pid);
            if (candName == null)
                return true;

            bool matches = false;
            if (processId.HasValue && (int)pid == processId.Value)
            {
                matches = true;
            }
            else if (!string.IsNullOrEmpty(targetName) && candName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
            {
                matches = true;
            }

            if (matches)
            {
                var (w, h) = GetClientSize(hwnd);
                // Game window must have client area > 0
                if (w > 0 && h > 0)
                {
                    match = new GameWindowInfo
                    {
                        Hwnd = hwnd,
                        ProcessId = (int)pid,
                        ProcessName = candName,
                        ClientWidth = w,
                        ClientHeight = h
                    };
                    return false; // Stop enumeration
                }
            }

            return true;
        }, 0);

        return match;
    }

    public bool FocusGameWindow(nint hwnd)
    {
        if (hwnd == 0) return false;

        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(hwnd);
        Thread.Sleep(750);

        var fg = NativeMethods.GetForegroundWindow();
        return fg == hwnd;
    }

    public (int Width, int Height) GetClientSize(nint hwnd)
    {
        if (hwnd == 0) return (0, 0);

        if (NativeMethods.GetClientRect(hwnd, out var rect))
        {
            return (rect.Width, rect.Height);
        }

        return (0, 0);
    }

    public (int ScreenX, int ScreenY) ClientToScreen(nint hwnd, int clientX, int clientY)
    {
        var pt = new NativeMethods.POINT(clientX, clientY);
        NativeMethods.ClientToScreen(hwnd, ref pt);
        return (pt.X, pt.Y);
    }

    public (int ClientX, int ClientY) ScreenToClient(nint hwnd, int screenX, int screenY)
    {
        var pt = new NativeMethods.POINT(screenX, screenY);
        NativeMethods.ScreenToClient(hwnd, ref pt);
        return (pt.X, pt.Y);
    }

    public NormalizedPoint? GetCursorClientNormalized(nint hwnd)
    {
        if (hwnd == 0) return null;

        if (!NativeMethods.GetCursorPos(out var cursor))
            return null;

        if (!NativeMethods.ScreenToClient(hwnd, ref cursor))
            return null;

        var (width, height) = GetClientSize(hwnd);
        if (width <= 0 || height <= 0)
            return null;

        double normX = Math.Clamp(Math.Round((double)cursor.X / width, 6), 0.0, 1.0);
        double normY = Math.Clamp(Math.Round((double)cursor.Y / height, 6), 0.0, 1.0);

        return new NormalizedPoint(normX, normY);
    }

    private static string? GetProcessName(int pid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == 0)
            return null;

        try
        {
            var buffer = new char[1024];
            uint size = (uint)buffer.Length;
            if (NativeMethods.QueryFullProcessImageNameW(handle, 0, buffer, ref size))
            {
                var fullPath = new string(buffer, 0, (int)size);
                return Path.GetFileName(fullPath).ToLowerInvariant();
            }
            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
