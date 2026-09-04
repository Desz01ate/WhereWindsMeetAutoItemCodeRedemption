using System.IO;
using WhereWindsMeetItemCodeRedeemer.Models;
using WhereWindsMeetItemCodeRedeemer.Services;
using WhereWindsMeetItemCodeRedeemer.ViewModels;
using Xunit;

namespace WhereWindsMeetItemCodeRedeemer.Tests;

public class MockGameWindowService : IGameWindowService
{
    public void EnsureDpiAwareness() { }
    public GameWindowInfo? FindGameWindow(string? processName = null, int? processId = null) => null;
    public bool FocusGameWindow(nint hwnd) => true;
    public (int Width, int Height) GetClientSize(nint hwnd) => (1920, 1080);
    public (int ScreenX, int ScreenY) ClientToScreen(nint hwnd, int clientX, int clientY) => (clientX, clientY);
    public (int ClientX, int ClientY) ScreenToClient(nint hwnd, int screenX, int screenY) => (screenX, screenY);
    public NormalizedPoint? GetCursorClientNormalized(nint hwnd) => new NormalizedPoint(0.5, 0.5);
}

public class MockInputSimulationService : IInputSimulationService
{
    public void SendKeyDown(ushort vk) { }
    public void SendKeyUp(ushort vk) { }
    public void SendKeyPress(ushort vk, int holdMs = 80) { }
    public void SendChord(ushort modifier, ushort vk) { }
    public void TypeText(string text) { }
    public void MoveCursorToNormalized(nint hwnd, NormalizedPoint point) { }
    public void ClickNormalized(nint hwnd, NormalizedPoint point) { }
    public Task RedeemOneAsync(nint hwnd, string code, UiConfig ui, double uiDelaySeconds, double resultWaitSeconds, bool spaceFallback = false, IProgress<string>? log = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class MockCalibrationService : ICalibrationService
{
    public Task ShowTargetsAsync(nint hwnd, UiConfig ui, IProgress<string>? log = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public NormalizedPoint? CaptureCurrentCursor(nint hwnd) => new NormalizedPoint(0.5, 0.5);
}

public class ViewModelTests
{
    [Fact]
    public void ViewModel_FilteringAndSelection_WorksCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WWM_VMTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configService = new ConfigService(Path.Combine(tempDir, "config.json"));
            var scraperService = new CodeScraperService();
            var gameService = new MockGameWindowService();
            var inputService = new MockInputSimulationService();
            var calService = new MockCalibrationService();

            var vm = new MainViewModel(configService, scraperService, gameService, inputService, calService);

            // Add test codes
            vm.Codes.Add(new RedeemCodeItem { Code = "TESTCODE1", Source = "SiteA", Status = CodeStatus.Pending, IsSelected = true });
            vm.Codes.Add(new RedeemCodeItem { Code = "TESTCODE2", Source = "SiteB", Status = CodeStatus.Redeemed, IsSelected = false });
            vm.Codes.Add(new RedeemCodeItem { Code = "BONUSCODE", Source = "SiteA", Status = CodeStatus.Pending, IsSelected = true });

            Assert.Equal(3, vm.TotalCodesCount);
            Assert.Equal(2, vm.PendingCodesCount);
            Assert.Equal(1, vm.RedeemedCodesCount);
            Assert.Equal(2, vm.SelectedPendingCount);

            // Test filter pending only
            vm.CurrentFilter = CodeFilter.PendingOnly;
            var pendingList = vm.FilteredCodes.Cast<RedeemCodeItem>().ToList();
            Assert.Equal(2, pendingList.Count);
            Assert.All(pendingList, item => Assert.Equal(CodeStatus.Pending, item.Status));

            // Test search
            vm.CurrentFilter = CodeFilter.All;
            vm.SearchText = "BONUS";
            var searchList = vm.FilteredCodes.Cast<RedeemCodeItem>().ToList();
            Assert.Single(searchList);
            Assert.Equal("BONUSCODE", searchList[0].Code);

            // Test clear selection
            vm.SearchText = string.Empty;
            vm.DeselectAllCommand.Execute(null);
            Assert.All(vm.Codes, item => Assert.False(item.IsSelected));
            Assert.Equal(0, vm.SelectedPendingCount);

            // Test select pending only
            vm.SelectPendingOnlyCommand.Execute(null);
            Assert.True(vm.Codes[0].IsSelected);
            Assert.False(vm.Codes[1].IsSelected);
            Assert.True(vm.Codes[2].IsSelected);
            Assert.Equal(2, vm.SelectedPendingCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ViewModel_AddManualCode_AddsAndNormalizes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WWM_VMTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configService = new ConfigService(Path.Combine(tempDir, "config.json"));
            var scraperService = new CodeScraperService();
            var gameService = new MockGameWindowService();
            var inputService = new MockInputSimulationService();
            var calService = new MockCalibrationService();

            var vm = new MainViewModel(configService, scraperService, gameService, inputService, calService);

            vm.ManualCodeInput = "  my_new_code  ";
            vm.AddManualCodeCommand.Execute(null);

            Assert.Single(vm.Codes);
            Assert.Equal("MY_NEW_CODE", vm.Codes[0].Code);
            Assert.Equal("Manual", vm.Codes[0].Source);
            Assert.Equal(CodeStatus.Pending, vm.Codes[0].Status);
            Assert.True(vm.Codes[0].IsSelected);
            Assert.Empty(vm.ManualCodeInput);

            // Adding duplicate should not add again
            vm.ManualCodeInput = "my_new_code";
            vm.AddManualCodeCommand.Execute(null);
            Assert.Single(vm.Codes);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
