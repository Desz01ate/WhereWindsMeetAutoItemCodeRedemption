using System.Collections.Concurrent;
using System.IO;
using WhereWindsMeetItemCodeRedeemer.Common;
using WhereWindsMeetItemCodeRedeemer.Models;
using WhereWindsMeetItemCodeRedeemer.Services;
using WhereWindsMeetItemCodeRedeemer.ViewModels;
using Xunit;

namespace WhereWindsMeetItemCodeRedeemer.Tests;

public class FakeKeyboard
{
    private readonly ConcurrentDictionary<int, bool> _keys = new();

    public bool IsDown(int virtualKey) => _keys.TryGetValue(virtualKey, out var down) && down;

    public void Press(int virtualKey) => _keys[virtualKey] = true;

    public void Release(int virtualKey) => _keys[virtualKey] = false;
}

public class FakeHotkeyMonitor : IHotkeyMonitor
{
    private Action? _onPressed;

    public int? WatchedKey { get; private set; }

    public bool IsWatching => _onPressed != null;

    public IDisposable Watch(int virtualKey, Action onPressed)
    {
        WatchedKey = virtualKey;
        _onPressed = onPressed;
        return new Watcher(this);
    }

    public void Press() => _onPressed?.Invoke();

    private sealed class Watcher : IDisposable
    {
        private readonly FakeHotkeyMonitor _owner;

        public Watcher(FakeHotkeyMonitor owner) => _owner = owner;

        public void Dispose() => _owner._onPressed = null;
    }
}

public class FoundGameWindowService : IGameWindowService
{
    public void EnsureDpiAwareness() { }
    public GameWindowInfo? FindGameWindow(string? processName = null, int? processId = null) =>
        new GameWindowInfo { Hwnd = 1, ProcessId = 4242, ProcessName = "wwm.exe", ClientWidth = 1920, ClientHeight = 1080 };
    public bool FocusGameWindow(nint hwnd) => true;
    public (int Width, int Height) GetClientSize(nint hwnd) => (1920, 1080);
    public (int ScreenX, int ScreenY) ClientToScreen(nint hwnd, int clientX, int clientY) => (clientX, clientY);
    public (int ClientX, int ClientY) ScreenToClient(nint hwnd, int screenX, int screenY) => (screenX, screenY);
    public NormalizedPoint? GetCursorClientNormalized(nint hwnd) => new NormalizedPoint(0.5, 0.5);
}

/// <summary>Simulates a redemption step that is still in progress until it is cancelled.</summary>
public class SlowInputSimulationService : IInputSimulationService
{
    private int _redeemCalls;

    public int RedeemCalls => Volatile.Read(ref _redeemCalls);
    public TaskCompletionSource FirstRedeemStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void SendKeyDown(ushort vk) { }
    public void SendKeyUp(ushort vk) { }
    public void SendKeyPress(ushort vk, int holdMs = 80) { }
    public void SendChord(ushort modifier, ushort vk) { }
    public void TypeText(string text) { }
    public void MoveCursorToNormalized(nint hwnd, NormalizedPoint point) { }
    public void ClickNormalized(nint hwnd, NormalizedPoint point) { }

    public async Task RedeemOneAsync(nint hwnd, string code, UiConfig ui, double uiDelaySeconds, double resultWaitSeconds, IProgress<string>? log = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _redeemCalls);
        FirstRedeemStarted.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}

public class HotkeyMonitorTests
{
    private const int KeyA = 0x41;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(200);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Watch_ReportsEachNewPressOfTheWatchedKey()
    {
        var keyboard = new FakeKeyboard();
        var monitor = new HotkeyMonitor(keyboard.IsDown, PollInterval);
        var presses = 0;

        using var watch = monitor.Watch(NativeMethods.VK_ESCAPE, () => Interlocked.Increment(ref presses));
        await Task.Delay(SettleTime);

        keyboard.Press(NativeMethods.VK_ESCAPE);
        await WaitUntilAsync(() => Volatile.Read(ref presses) == 1);

        // Holding the key down is a single press.
        await Task.Delay(SettleTime);
        Assert.Equal(1, Volatile.Read(ref presses));

        keyboard.Release(NativeMethods.VK_ESCAPE);
        await Task.Delay(SettleTime);
        keyboard.Press(NativeMethods.VK_ESCAPE);
        await WaitUntilAsync(() => Volatile.Read(ref presses) == 2);
    }

    [Fact]
    public async Task Watch_IgnoresOtherKeys()
    {
        var keyboard = new FakeKeyboard();
        var monitor = new HotkeyMonitor(keyboard.IsDown, PollInterval);
        var presses = 0;

        using var watch = monitor.Watch(NativeMethods.VK_ESCAPE, () => Interlocked.Increment(ref presses));
        await Task.Delay(SettleTime);

        keyboard.Press(KeyA);
        await Task.Delay(SettleTime);

        Assert.Equal(0, Volatile.Read(ref presses));
    }

    [Fact]
    public async Task Watch_WhenKeyAlreadyHeld_WaitsForReleaseAndNewPress()
    {
        var keyboard = new FakeKeyboard();
        keyboard.Press(KeyA);
        var monitor = new HotkeyMonitor(keyboard.IsDown, PollInterval);
        var presses = 0;

        using var watch = monitor.Watch(KeyA, () => Interlocked.Increment(ref presses));

        await Task.Delay(SettleTime);
        Assert.Equal(0, Volatile.Read(ref presses));

        keyboard.Release(KeyA);
        await Task.Delay(SettleTime);
        Assert.Equal(0, Volatile.Read(ref presses));

        keyboard.Press(KeyA);
        await WaitUntilAsync(() => Volatile.Read(ref presses) == 1);
    }

    [Fact]
    public async Task Dispose_StopsWatching()
    {
        var keyboard = new FakeKeyboard();
        var monitor = new HotkeyMonitor(keyboard.IsDown, PollInterval);
        var presses = 0;

        var watch = monitor.Watch(NativeMethods.VK_ESCAPE, () => Interlocked.Increment(ref presses));
        await Task.Delay(SettleTime);
        watch.Dispose();
        watch.Dispose(); // a second Dispose must be harmless

        keyboard.Press(NativeMethods.VK_ESCAPE);
        await Task.Delay(SettleTime);

        Assert.Equal(0, Volatile.Read(ref presses));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0xFF)]
    public void Watch_WithInvalidVirtualKey_Throws(int virtualKey)
    {
        var monitor = new HotkeyMonitor(_ => false, PollInterval);

        Assert.Throws<ArgumentOutOfRangeException>(() => monitor.Watch(virtualKey, () => { }));
    }
}

public class StopHotkeyViewModelTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StartRedemption_WhenEscPressed_StopsAndKeepsCodesPending()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WWM_VMTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configService = new ConfigService(Path.Combine(tempDir, "config.json"));
            var ui = configService.CurrentConfig.Ui;
            ui.ExchangeButton = new NormalizedPoint(0.5, 0.2);
            ui.CodeInput = new NormalizedPoint(0.5, 0.5);
            ui.SubmitButton = new NormalizedPoint(0.55, 0.65);
            ui.CancelButton = new NormalizedPoint(0.45, 0.65);

            var input = new SlowInputSimulationService();
            var hotkey = new FakeHotkeyMonitor();
            var vm = new MainViewModel(
                configService,
                new CodeScraperService(),
                new FoundGameWindowService(),
                input,
                new MockCalibrationService(),
                hotkey,
                new MockUpdateService());

            vm.Codes.Add(new RedeemCodeItem { Code = "FIRSTCODE", Source = "Test", Status = CodeStatus.Pending, IsSelected = true });
            vm.Codes.Add(new RedeemCodeItem { Code = "SECONDCODE", Source = "Test", Status = CodeStatus.Pending, IsSelected = true });

            var run = vm.StartRedemptionAsync();
            await input.FirstRedeemStarted.Task.WaitAsync(WaitLimit);

            Assert.True(vm.IsRedeeming);
            Assert.True(hotkey.IsWatching);
            Assert.Equal((int?)NativeMethods.VK_ESCAPE, hotkey.WatchedKey);

            hotkey.Press();
            hotkey.Press(); // a second press while stopping must be harmless
            await run.WaitAsync(WaitLimit);

            Assert.False(vm.IsRedeeming);
            Assert.False(hotkey.IsWatching);
            Assert.Equal(1, input.RedeemCalls);
            Assert.All(vm.Codes, item => Assert.Equal(CodeStatus.Pending, item.Status));
            Assert.Empty(configService.LoadRedeemedCodes());
            Assert.Single(vm.LogEntries, line => line.Contains("[Esc]"));
            Assert.Contains(vm.LogEntries, line => line.Contains("[Cancelled]"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
