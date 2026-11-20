using WhereWindsMeetItemCodeRedeemer.Common;

namespace WhereWindsMeetItemCodeRedeemer.Services;

/// <summary>
/// Detects the Esc key by polling the global key state, so it works even while the game
/// window has focus (a WPF key binding only fires when this app's own window is active).
/// </summary>
public sealed class EscapeKeyMonitor : IStopHotkeyMonitor
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(25);

    private readonly Func<bool> _isKeyDown;
    private readonly TimeSpan _pollInterval;

    public EscapeKeyMonitor()
        : this(() => (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0, DefaultPollInterval)
    {
    }

    public EscapeKeyMonitor(Func<bool> isKeyDown, TimeSpan pollInterval)
    {
        _isKeyDown = isKeyDown ?? throw new ArgumentNullException(nameof(isKeyDown));
        _pollInterval = pollInterval > TimeSpan.Zero ? pollInterval : DefaultPollInterval;
    }

    public IDisposable Start(Action onPressed)
    {
        ArgumentNullException.ThrowIfNull(onPressed);

        var cts = new CancellationTokenSource();
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            // A key that is already held when watching starts (for example Esc used to close
            // a game menu just before clicking Start) must be released before it can trigger.
            bool wasDown = _isKeyDown();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    bool isDown = _isKeyDown();
                    if (isDown && !wasDown)
                    {
                        onPressed();
                        return;
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
