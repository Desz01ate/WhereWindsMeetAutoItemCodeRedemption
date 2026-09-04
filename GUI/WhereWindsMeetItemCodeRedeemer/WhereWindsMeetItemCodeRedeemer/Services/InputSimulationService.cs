using System.Diagnostics;
using System.Runtime.InteropServices;
using WhereWindsMeetItemCodeRedeemer.Common;
using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

public class InputSimulationService : IInputSimulationService
{
    private readonly IGameWindowService _gameWindowService;

    private static readonly Dictionary<ushort, ushort> ScancodeMap = new()
    {
        { NativeMethods.VK_CONTROL, NativeMethods.SCAN_CONTROL },
        { NativeMethods.VK_A, NativeMethods.SCAN_A },
        { NativeMethods.VK_ESCAPE, NativeMethods.SCAN_ESCAPE },
        { NativeMethods.VK_SPACE, NativeMethods.SCAN_SPACE },
        { NativeMethods.VK_V, NativeMethods.SCAN_V }
    };

    public InputSimulationService(IGameWindowService gameWindowService)
    {
        _gameWindowService = gameWindowService;
    }

    public void SendKeyDown(ushort vk)
    {
        if (ScancodeMap.TryGetValue(vk, out ushort scan))
        {
            SendKeyboard(0, scan, NativeMethods.KEYEVENTF_SCANCODE);
        }
        else
        {
            SendKeyboard(vk, 0, 0);
        }
    }

    public void SendKeyUp(ushort vk)
    {
        if (ScancodeMap.TryGetValue(vk, out ushort scan))
        {
            SendKeyboard(0, scan, NativeMethods.KEYEVENTF_SCANCODE | NativeMethods.KEYEVENTF_KEYUP);
        }
        else
        {
            SendKeyboard(vk, 0, NativeMethods.KEYEVENTF_KEYUP);
        }
    }

    public void SendKeyPress(ushort vk, int holdMs = 80)
    {
        SendKeyDown(vk);
        Thread.Sleep(holdMs);
        SendKeyUp(vk);
    }

    public void SendChord(ushort modifier, ushort vk)
    {
        SendKeyDown(modifier);
        try
        {
            SendKeyPress(vk);
        }
        finally
        {
            SendKeyUp(modifier);
        }
    }

    public void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        bool clipboardSuccess = false;
        try
        {
            SetClipboardText(text);
            clipboardSuccess = true;
        }
        catch
        {
            clipboardSuccess = false;
        }

        if (clipboardSuccess)
        {
            // Ctrl+V
            SendChord(NativeMethods.VK_CONTROL, NativeMethods.VK_V);
        }
        else
        {
            // Unicode fallback
            foreach (char c in text)
            {
                SendUnicode(c);
            }
        }
    }

    public void SendUnicode(char c)
    {
        ushort scan = (ushort)c;
        SendKeyboard(0, scan, NativeMethods.KEYEVENTF_UNICODE);
        SendKeyboard(0, scan, NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP);
    }

    public void MoveCursorToNormalized(nint hwnd, NormalizedPoint point)
    {
        var (width, height) = _gameWindowService.GetClientSize(hwnd);
        if (width <= 0 || height <= 0) return;

        int clientX = (int)Math.Round(width * point.X);
        int clientY = (int)Math.Round(height * point.Y);

        var (screenX, screenY) = _gameWindowService.ClientToScreen(hwnd, clientX, clientY);
        NativeMethods.SetCursorPos(screenX, screenY);
    }

    public void ClickNormalized(nint hwnd, NormalizedPoint point)
    {
        MoveCursorToNormalized(hwnd, point);
        Thread.Sleep(30);
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        Thread.Sleep(30);
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }

    public async Task RedeemOneAsync(
        nint hwnd,
        string code,
        UiConfig ui,
        double uiDelaySeconds,
        double resultWaitSeconds,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (!ui.IsFullyCalibrated)
        {
            throw new InvalidOperationException(
                "Target coordinates are not fully configured. All targets (Exchange Button, Code Input, Submit Button, Cancel Button) must be calibrated before redeeming.");
        }
        int uiDelayMs = (int)Math.Max(50, uiDelaySeconds * 1000);
        int resultWaitMs = (int)Math.Max(100, resultWaitSeconds * 1000);

        log?.Report($"[1/6] Focusing game window (0x{hwnd:X8})...");
        if (!_gameWindowService.FocusGameWindow(hwnd))
        {
            throw new InvalidOperationException("Could not focus the game window; refusing to send input elsewhere.");
        }
        await Task.Delay(uiDelayMs, cancellationToken);

        log?.Report("[2/6] Clicking Exchange button...");
        ClickNormalized(hwnd, ui.ExchangeButton!);
        await Task.Delay(uiDelayMs, cancellationToken);

        log?.Report("[3/6] Clicking Code Input and clearing text...");
        ClickNormalized(hwnd, ui.CodeInput!);
        await Task.Delay(50, cancellationToken);
        SendChord(NativeMethods.VK_CONTROL, NativeMethods.VK_A);
        await Task.Delay(50, cancellationToken);

        log?.Report($"[4/6] Typing code: {code}...");
        TypeText(code);
        await Task.Delay(uiDelayMs, cancellationToken);

        log?.Report("[5/6] Clicking Submit button...");
        ClickNormalized(hwnd, ui.SubmitButton!);
        await Task.Delay(uiDelayMs, cancellationToken);

        log?.Report("[6/6] Clicking Cancel/Close button...");
        ClickNormalized(hwnd, ui.CancelButton!);

        log?.Report($"Waiting for result ({resultWaitSeconds:F1}s)...");
        await Task.Delay(resultWaitMs, cancellationToken);
    }

    private static void SendKeyboard(ushort vk, ushort scan, uint flags)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = 0
                }
            }
        };

        var inputs = new[] { input };
        uint sent = NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != 1)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SendInput failed with Win32 error code {err}.");
        }
    }

    private static void SetClipboardText(string text)
    {
        var thread = new Thread(() =>
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
            }
            catch
            {
                SetClipboardViaClipExe(text);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(1000))
        {
            SetClipboardViaClipExe(text);
        }
    }

    private static void SetClipboardViaClipExe(string text)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "clip.exe",
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc != null)
        {
            using (var sw = proc.StandardInput)
            {
                sw.Write(text);
            }
            proc.WaitForExit(1000);
        }
    }
}
