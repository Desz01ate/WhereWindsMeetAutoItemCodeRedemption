using WhereWindsMeetItemCodeRedeemer.Common;

namespace WhereWindsMeetItemCodeRedeemer.Services;

/// <summary>
/// Detects key presses by polling the global key state, so a hotkey works even while another
/// window (such as the game) has focus. A WPF key binding only fires while this app's own
/// window is active.
/// </summary>
public sealed class HotkeyMonitor : IHotkeyMonitor
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(25);

    private readonly Func<int, bool> _isKeyDown;
    private readonly TimeSpan _pollInterval;

    public HotkeyMonitor()
        : this(virtualKey => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0, DefaultPollInterval)
    {
    }

    public HotkeyMonitor(Func<int, bool> isKeyDown, TimeSpan pollInterval)
    {
        _isKeyDown = isKeyDown ?? throw new ArgumentNullException(nameof(isKeyDown));
        _pollInterval = pollInterval > TimeSpan.Zero ? pollInterval : DefaultPollInterval;
    }

    public IDisposable Watch(int virtualKey, Action onPressed)
    {
        if (virtualKey is < 0x01 or > 0xFE)
            throw new ArgumentOutOfRangeException(nameof(virtualKey), virtualKey, "Expected a Win32 virtual-key code (0x01-0xFE).");
        ArgumentNullException.ThrowIfNull(onPressed);

        var cts = new CancellationTokenSource();
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            // A key that is already held when watching starts must be released first, so a
            // press that began before the watch (for example Esc used to close a game menu
            // just before clicking Start) is not reported.
            bool wasDown = _isKeyDown(virtualKey);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    bool isDown = _isKeyDown(virtualKey);
                    if (isDown && !wasDown && !token.IsCancellationRequested)
                    {
                        onPressed();
                    }

                    wasDown = isDown;
                    await Task.Delay(_pollInterval, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Watching was stopped by Dispose.
            }
        });

        return new Subscription(cts);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly CancellationTokenSource _cts;

        public Subscription(CancellationTokenSource cts) => _cts = cts;

        // Only signals the polling loop; it never waits for it, because the callback may be
        // marshalling to the UI thread that is calling Dispose.
        public void Dispose() => _cts.Cancel();
    }
}
