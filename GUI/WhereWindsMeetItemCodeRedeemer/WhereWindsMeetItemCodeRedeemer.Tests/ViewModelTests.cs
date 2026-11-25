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
    public Task RedeemOneAsync(nint hwnd, string code, UiConfig ui, double uiDelaySeconds, double resultWaitSeconds, IProgress<string>? log = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
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

            var vm = new MainViewModel(configService, scraperService, gameService, inputService, calService, new FakeHotkeyMonitor());

            // Add test codes
            vm.Codes.Add(new RedeemCodeItem { Code = "TESTCODE1", Source = "SiteA", Status = CodeStatus.Pending, IsSelected = true, IsActive = true });
            vm.Codes.Add(new RedeemCodeItem { Code = "TESTCODE2", Source = "SiteB", Status = CodeStatus.Redeemed, IsSelected = false, IsActive = true });
            vm.Codes.Add(new RedeemCodeItem { Code = "BONUSCODE", Source = "SiteA", Status = CodeStatus.Pending, IsSelected = true, IsActive = true });

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

            var vm = new MainViewModel(configService, scraperService, gameService, inputService, calService, new FakeHotkeyMonitor());

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

    [Fact]
    public async Task InputSimulationService_WhenNotCalibrated_ThrowsInvalidOperationException()
    {
        var gameService = new MockGameWindowService();
        var service = new InputSimulationService(gameService);

        var uncalibratedUi = new UiConfig
        {
            ExchangeButton = new NormalizedPoint(0.5, 0.5)
            // Missing code_input, submit_button, cancel_button
        };

        Assert.False(uncalibratedUi.IsFullyCalibrated);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RedeemOneAsync(1234, "TESTCODE", uncalibratedUi, 0.1, 0.1));
    }

    [Fact]
    public void ViewModel_IsFullyCalibrated_ReflectsConfigUiState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WWM_VMTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configService = new ConfigService(Path.Combine(tempDir, "config.json"));
            configService.CurrentConfig.Ui.ExchangeButton = null;

            var vm = new MainViewModel(
                configService,
                new CodeScraperService(),
                new MockGameWindowService(),
                new MockInputSimulationService(),
                new MockCalibrationService(),
                new FakeHotkeyMonitor());

            Assert.False(vm.IsFullyCalibrated);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ViewModel_ActivityFilter_DefaultsToActiveOnly_AndComposesCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WWM_VMTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configService = new ConfigService(Path.Combine(tempDir, "config.json"));
            var vm = new MainViewModel(
                configService,
                new CodeScraperService(),
                new MockGameWindowService(),
                new MockInputSimulationService(),
                new MockCalibrationService(),
                new FakeHotkeyMonitor());

            Assert.Equal(CodeActivityFilter.ActiveOnly, vm.CurrentActivityFilter);

            vm.Codes.Add(new RedeemCodeItem
            {
                Code = "ACTIVE1",
                Source = "codes.yar.gg",
                Status = CodeStatus.Pending,
                IsSelected = true,
                IsActive = true,
                ExpiredAt = DateTimeOffset.UtcNow.AddDays(5)
            });
            vm.Codes.Add(new RedeemCodeItem
            {
                Code = "PASTEXPIRY",
                Source = "codes.yar.gg",
                Status = CodeStatus.Pending,
                IsSelected = false,
                IsActive = true,
                ExpiredAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
            vm.Codes.Add(new RedeemCodeItem
            {
                Code = "EXPIRED1",
                Source = "codes.yar.gg",
                Status = CodeStatus.Pending,
                IsSelected = false,
                IsActive = false,
                ExpiredAt = DateTimeOffset.UtcNow.AddDays(-10)
            });
            vm.Codes.Add(new RedeemCodeItem
            {
                Code = "UNKNOWN1",
                Source = "game8.co",
                Status = CodeStatus.Pending,
                IsSelected = false,
                IsActive = null
            });

            // Default: ActiveOnly -> only ACTIVE1 (PASTEXPIRY is past ExpiredAt so effectively expired)
            Assert.Equal(1, vm.ActiveCodesCount);
            var activeOnly = vm.FilteredCodes.Cast<RedeemCodeItem>().ToList();
            Assert.Single(activeOnly);
            Assert.Equal("ACTIVE1", activeOnly[0].Code);

            // ActiveOrUnknown -> ACTIVE1 + UNKNOWN1
            vm.CurrentActivityFilter = CodeActivityFilter.ActiveOrUnknown;
            var activeOrUnknown = vm.FilteredCodes.Cast<RedeemCodeItem>().ToList();
            Assert.Equal(2, activeOrUnknown.Count);
            Assert.Contains(activeOrUnknown, c => c.Code == "ACTIVE1");
            Assert.Contains(activeOrUnknown, c => c.Code == "UNKNOWN1");

            // ExpiredOnly -> PASTEXPIRY + EXPIRED1
            vm.CurrentActivityFilter = CodeActivityFilter.ExpiredOnly;
            var expiredOnly = vm.FilteredCodes.Cast<RedeemCodeItem>().ToList();
            Assert.Equal(2, expiredOnly.Count);
            Assert.Contains(expiredOnly, c => c.Code == "PASTEXPIRY");
            Assert.Contains(expiredOnly, c => c.Code == "EXPIRED1");

            // UnknownOnly -> UNKNOWN1
            vm.CurrentActivityFilter = CodeActivityFilter.UnknownOnly;
            var unknownOnly = vm.FilteredCodes.Cast<RedeemCodeItem>().ToList();
            Assert.Single(unknownOnly);
            Assert.Equal("UNKNOWN1", unknownOnly[0].Code);

            // All -> all 4; SelectPendingOnly should still skip expired items
            vm.CurrentActivityFilter = CodeActivityFilter.All;
            Assert.Equal(4, vm.FilteredCodes.Cast<RedeemCodeItem>().Count());
            vm.SelectPendingOnlyCommand.Execute(null);
            Assert.True(vm.Codes.Single(c => c.Code == "ACTIVE1").IsSelected);
            Assert.True(vm.Codes.Single(c => c.Code == "UNKNOWN1").IsSelected);
            Assert.False(vm.Codes.Single(c => c.Code == "EXPIRED1").IsSelected);
            Assert.False(vm.Codes.Single(c => c.Code == "PASTEXPIRY").IsSelected);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
