namespace WhereWindsMeetItemCodeRedeemer.Services;

/// <summary>
/// Watches for the emergency-stop key while a redemption run is active.
/// </summary>
public interface IStopHotkeyMonitor
{
    /// <summary>
    /// Starts watching for the stop key. <paramref name="onPressed"/> is invoked once, on a
    /// background thread, the first time the key goes down after this call.
    /// Dispose the returned object to stop watching.
    /// </summary>
    IDisposable Start(Action onPressed);
}
