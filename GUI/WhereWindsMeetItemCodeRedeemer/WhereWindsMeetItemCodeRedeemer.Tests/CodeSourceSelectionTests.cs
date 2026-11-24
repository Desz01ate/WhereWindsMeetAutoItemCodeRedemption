using System.IO;
using WhereWindsMeetItemCodeRedeemer.Models;
using WhereWindsMeetItemCodeRedeemer.Services;
using WhereWindsMeetItemCodeRedeemer.ViewModels;
using Xunit;

namespace WhereWindsMeetItemCodeRedeemer.Tests;

/// <summary>Returns a fixed list of codes (fresh item objects every call, like the real scrapers).</summary>
public class FakeCodeScraperService : ICodeScraperService
{
    public string[] NextCodes { get; set; } = ["FETCHEDCODE1"];

    public Task<List<RedeemCodeItem>> ScrapeAllAsync(
        double timeoutSeconds,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(NextCodes
            .Select(code => new RedeemCodeItem { Code = code, Source = "test", Status = CodeStatus.Pending, IsSelected = true })
            .ToList());
}

/// <summary>Records which source the view model asked for and always hands out the same fake scraper.</summary>
public class FakeCodeScraperServiceFactory : ICodeScraperServiceFactory
{
    public FakeCodeScraperService Scraper { get; } = new();

    public List<CodeSourceMode> RequestedSources { get; } = [];

    public ICodeScraperService Create(CodeSourceMode source)
    {
        RequestedSources.Add(source);
        return Scraper;
    }
}

public class CodeSourceSelectionTests
{
    private static (MainViewModel Vm, FakeCodeScraperServiceFactory Factory, string TempDir) CreateViewModel()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WWM_VMTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var factory = new FakeCodeScraperServiceFactory();
        var vm = new MainViewModel(
            new ConfigService(Path.Combine(tempDir, "config.json")),
            factory,
            new MockGameWindowService(),
            new MockInputSimulationService(),
            new MockCalibrationService(),
            new FakeHotkeyMonitor(),
            new MockUpdateService());

        return (vm, factory, tempDir);
    }

    [Fact]
    public async Task FetchCodes_ByDefault_UsesTheConfiguredSourcesScraper()
    {
        var (vm, factory, tempDir) = CreateViewModel();
        try
        {
            Assert.Equal(CodeSourceMode.ConfiguredSources, vm.CodeSource);

            await vm.FetchCodesAsync();

            Assert.Equal(new[] { CodeSourceMode.ConfiguredSources }, factory.RequestedSources);
            Assert.Contains(vm.Codes, c => c.Code == "FETCHEDCODE1");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task FetchCodes_WithCodesYarGg_UsesThatScraper()
    {
        var (vm, factory, tempDir) = CreateViewModel();
        try
        {
            vm.CodeSource = CodeSourceMode.CodesYarGg;

            await vm.FetchCodesAsync();

            Assert.Equal(new[] { CodeSourceMode.CodesYarGg }, factory.RequestedSources);
            Assert.Contains(vm.Codes, c => c.Code == "FETCHEDCODE1");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task FetchCodes_ReplacesPreviouslyFetchedCodes()
    {
        var (vm, factory, tempDir) = CreateViewModel();
        try
        {
            factory.Scraper.NextCodes = ["OLDONLY01", "SHARED001"];
            await vm.FetchCodesAsync();

            vm.CodeSource = CodeSourceMode.CodesYarGg;
            factory.Scraper.NextCodes = ["SHARED001", "NEWONLY01"];
            await vm.FetchCodesAsync();

            Assert.Equal(new[] { "NEWONLY01", "SHARED001" }, vm.Codes.Select(c => c.Code).OrderBy(c => c).ToArray());
            Assert.Equal(2, vm.TotalCodesCount);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task FetchCodes_KeepsManuallyAddedCodes()
    {
        var (vm, factory, tempDir) = CreateViewModel();
        try
        {
            vm.ManualCodeInput = "MYCODE123";
            vm.AddManualCodeCommand.Execute(null);

            // The fetched list also contains the manual code: it must not be duplicated or replaced.
            factory.Scraper.NextCodes = ["MYCODE123", "FETCHEDCODE1"];
            await vm.FetchCodesAsync();

            Assert.Single(vm.Codes, c => c.Code == "MYCODE123");
            Assert.Equal("Manual", vm.Codes.Single(c => c.Code == "MYCODE123").Source);
            Assert.Contains(vm.Codes, c => c.Code == "FETCHEDCODE1");
            Assert.Contains(vm.LogEntries, line => line.Contains("Kept 1 manually added code"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task FetchCodes_WhenNothingFound_KeepsCurrentList()
    {
        var (vm, factory, tempDir) = CreateViewModel();
        try
        {
            factory.Scraper.NextCodes = ["KEEPME0001"];
            await vm.FetchCodesAsync();

            factory.Scraper.NextCodes = [];
            await vm.FetchCodesAsync();

            Assert.Single(vm.Codes, c => c.Code == "KEEPME0001");
            Assert.Contains(vm.LogEntries, line => line.Contains("current list was kept"));
            Assert.False(vm.IsBusy);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
