using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

public interface IInputSimulationService
{
    void SendKeyDown(ushort vk);
    void SendKeyUp(ushort vk);
    void SendKeyPress(ushort vk, int holdMs = 80);
    void SendChord(ushort modifier, ushort vk);
    void TypeText(string text);
    void MoveCursorToNormalized(nint hwnd, NormalizedPoint point);
    void ClickNormalized(nint hwnd, NormalizedPoint point);

    Task RedeemOneAsync(
        nint hwnd,
        string code,
        UiConfig ui,
        double uiDelaySeconds,
        double resultWaitSeconds,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default);
}
