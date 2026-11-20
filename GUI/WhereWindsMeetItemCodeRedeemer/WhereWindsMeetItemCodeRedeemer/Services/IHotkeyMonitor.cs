namespace WhereWindsMeetItemCodeRedeemer.Services;

/// <summary>
/// Watches a keyboard key globally and reports each new press, whichever window has focus.
/// It knows nothing about what a press means; the caller decides what to do with it.
/// </summary>
public interface IHotkeyMonitor
{
    /// <summary>
    /// Starts watching <paramref name="virtualKey"/> (a Win32 virtual-key code).
    /// <paramref name="onPressed"/> is invoked on a background thread each time the key goes
    /// down, until the returned object is disposed. A key that is already held when watching
    /// starts is reported only after it has been released and pressed again.
    /// </summary>
    IDisposable Watch(int virtualKey, Action onPressed);
}
