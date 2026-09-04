namespace WhereWindsMeetItemCodeRedeemer.Models;

public class GameWindowInfo
{
    public nint Hwnd { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public int ClientWidth { get; set; }
    public int ClientHeight { get; set; }

    public override string ToString() =>
        $"{ProcessName} (PID {ProcessId}) - Window: 0x{Hwnd:X8} ({ClientWidth}x{ClientHeight})";
}
