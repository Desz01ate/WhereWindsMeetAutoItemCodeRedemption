using System.IO;
using WhereWindsMeetItemCodeRedeemer.Models;
using WhereWindsMeetItemCodeRedeemer.Services;
using WhereWindsMeetItemCodeRedeemer.ViewModels;
using Xunit;

namespace WhereWindsMeetItemCodeRedeemer.Tests;

public class FakeKey
{
    private volatile bool _isDown;

    public bool IsDown => _isDown;

    public void Press() => _isDown = true;

    public void Release() => _isDown = false;
}

public class FakeStopHotkeyMonitor : IStopHotkeyMonitor
{
    private Action? _onPressed;

    public bool IsWatching => _onPressed != null;

    public IDisposable Start(Action onPressed)
    {
        _onPressed = onPressed;
        return new Watch(this);
    }

    public void Press() => _onPressed?.Invoke();

    private sealed class Watch : IDisposable
    {
        private readonly FakeStopHotkeyMonitor _owner;

        public Watch(FakeStopHotkeyMonitor owner) => _owner = owner;

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

public class EscapeKeyMonitorTests
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task Start_WhenKeyIsPressed_InvokesCallbackOnlyOnce()
    {
        var key = new FakeKey();
        var monitor = new EscapeKeyMonitor(() => key.IsDown, PollInterval);
        var calls = 0;
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var watch = monitor.Start(() =>
        {
            Interlocked.Increment(ref calls);
            fired.TrySetResult();
        });

        await Task.Delay(SettleTime);
        key.Press();
        await fired.Task.WaitAsync(WaitLimit);

        // Further presses after the first one must not call back again.
        key.Release();
        await Task.Delay(SettleTime);
        key.Press();
        await Task.Delay(SettleTime);

        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Start_WhenKeyAlreadyHeld_WaitsForReleaseAndNewPress()
    {
        var key = new FakeKey();
        key.Press();
        var monitor = new EscapeKeyMonitor(() => key.IsDown, PollInterval);
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var watch = monitor.Start(() => fired.TrySetResult());

        await Task.Delay(SettleTime);
        Assert.False(fired.Task.IsCompleted);

        key.Release();
        await Task.Delay(SettleTime);
        Assert.False(fired.Task.IsCompleted);

        key.Press();
        await fired.Task.WaitAsync(WaitLimit);
    }

    [Fact]
    public async Task Dispose_StopsWatching()
    {
        var key = new FakeKey();
        var monitor = new EscapeKeyMonitor(() => key.IsDown, PollInterval);
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var watch = monitor.Start(() => fired.TrySetResult());
        await Task.Delay(SettleTime);
        watch.Dispose();
        watch.Dispose(); // a second Dispose must be harmless

        key.Press();
        await Task.Delay(SettleTime);

        Assert.False(fired.Task.IsCompleted);
    }
}

public class StopHotkeyViewModelTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StartRedemption_WhenStopHotkeyPressed_StopsAndKeepsCodesPending()
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
            var hotkey = new FakeStopHotkeyMonitor();
            var vm = new MainViewModel(
                configService,
                new CodeScraperService(),
                new FoundGameWindowService(),
                input,
                new MockCalibrationService(),
                new MockUpdateService(),
                hotkey);

            vm.Codes.Add(new RedeemCodeItem { Code = "FIRSTCODE", Source = "Test", Status = CodeStatus.Pending, IsSelected = true });
            vm.Codes.Add(new RedeemCodeItem { Code = "SECONDCODE", Source = "Test", Status = CodeStatus.Pending, IsSelected = true });

            var run = vm.StartRedemptionAsync();
            await input.FirstRedeemStarted.Task.WaitAsync(WaitLimit);

            Assert.True(vm.IsRedeeming);
            Assert.True(hotkey.IsWatching);

            hotkey.Press();
            await run.WaitAsync(WaitLimit);

            Assert.False(vm.IsRedeeming);
            Assert.False(hotkey.IsWatching);
            Assert.Equal(1, input.RedeemCalls);
            Assert.All(vm.Codes, item => Assert.Equal(CodeStatus.Pending, item.Status));
            Assert.Empty(configService.LoadRedeemedCodes());
            Assert.Contains(vm.LogEntries, line => line.Contains("[Esc]"));
            Assert.Contains(vm.LogEntries, line => line.Contains("[Cancelled]"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
