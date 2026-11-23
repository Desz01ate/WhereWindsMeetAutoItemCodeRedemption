using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using WhereWindsMeetItemCodeRedeemer.Models;
using WhereWindsMeetItemCodeRedeemer.Services;
using WhereWindsMeetItemCodeRedeemer.ViewModels;
using Xunit;

namespace WhereWindsMeetItemCodeRedeemer.Tests;

/// <summary>Records which sources the view model asked for.</summary>
public class RecordingCodeScraperService : ICodeScraperService
{
    public List<string>? HtmlSources { get; private set; }
    public List<string>? ApiSources { get; private set; }

    /// <summary>Codes the next fetch returns (fresh item objects every call, like the real scraper).</summary>
    public string[] NextCodes { get; set; } = ["FETCHEDCODE1"];

    public Task<List<RedeemCodeItem>> ScrapeAllAsync(
        IEnumerable<string> htmlSources,
        IEnumerable<string> apiSources,
        double timeoutSeconds,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        HtmlSources = htmlSources.ToList();
        ApiSources = apiSources.ToList();
        return Task.FromResult(NextCodes
            .Select(code => new RedeemCodeItem { Code = code, Source = "test", Status = CodeStatus.Pending, IsSelected = true })
            .ToList());
    }

    public Task<List<RedeemCodeItem>> ScrapeApiAsync(string url, double timeoutSeconds, CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<RedeemCodeItem>());

    public Task<List<RedeemCodeItem>> ScrapeHtmlAsync(string url, double timeoutSeconds, CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<RedeemCodeItem>());
}

public class CodeSourceSelectionTests
{
    private static (MainViewModel Vm, RecordingCodeScraperService Scraper, ConfigService Config, string TempDir) CreateViewModel()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WWM_VMTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var config = new ConfigService(Path.Combine(tempDir, "config.json"));
        var scraper = new RecordingCodeScraperService();
        var vm = new MainViewModel(
            config,
            scraper,
            new MockGameWindowService(),
            new MockInputSimulationService(),
            new MockCalibrationService(),
            new FakeHotkeyMonitor(),
            new MockUpdateService());

        return (vm, scraper, config, tempDir);
    }

    [Fact]
    public async Task FetchCodes_ByDefault_UsesSourcesFromConfig()
    {
        var (vm, scraper, config, tempDir) = CreateViewModel();
        try
        {
            Assert.Equal(CodeSourceMode.ConfiguredSources, vm.CodeSource);

            await vm.FetchCodesAsync();

            Assert.Equal(config.CurrentConfig.Sources, scraper.HtmlSources);
            Assert.Equal(config.CurrentConfig.ApiSources, scraper.ApiSources);
            Assert.Contains(vm.Codes, c => c.Code == "FETCHEDCODE1");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task FetchCodes_WithCodesYarGg_UsesOnlyThatApi()
    {
        var (vm, scraper, _, tempDir) = CreateViewModel();
        try
        {
            vm.CodeSource = CodeSourceMode.CodesYarGg;

            await vm.FetchCodesAsync();

            Assert.Empty(scraper.HtmlSources!);
            Assert.Equal(new[] { CodeScraperService.CodesYarGgApiUrl }, scraper.ApiSources);
            Assert.Contains(vm.Codes, c => c.Code == "FETCHEDCODE1");
            Assert.Contains(vm.LogEntries, line => line.Contains("codes.yar.gg only"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task FetchCodes_ReplacesPreviouslyFetchedCodes()
    {
        var (vm, scraper, _, tempDir) = CreateViewModel();
        try
        {
            scraper.NextCodes = ["OLDONLY01", "SHARED001"];
            await vm.FetchCodesAsync();

            vm.CodeSource = CodeSourceMode.CodesYarGg;
            scraper.NextCodes = ["SHARED001", "NEWONLY01"];
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
        var (vm, scraper, _, tempDir) = CreateViewModel();
        try
        {
            vm.ManualCodeInput = "MYCODE123";
            vm.AddManualCodeCommand.Execute(null);

            // The fetched list also contains the manual code: it must not be duplicated or replaced.
            scraper.NextCodes = ["MYCODE123", "FETCHEDCODE1"];
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
        var (vm, scraper, _, tempDir) = CreateViewModel();
        try
        {
            scraper.NextCodes = ["KEEPME0001"];
            await vm.FetchCodesAsync();

            scraper.NextCodes = [];
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

    [Fact]
    public async Task ScrapeApiAsync_ParsesCodesYarGgResponseShape()
    {
        // Same shape as https://codes.yar.gg/api/codes (checked 2026-11-23).
        var json = """
        {
          "updatedAt": "2026-11-23T10:38:08.901Z",
          "active": [
            { "code": "ANNDWTFD7QE", "addedAt": "2026-11-23T10:38:08.901Z", "verifiedAt": "2026-11-23T10:38:08.901Z" },
            { "code": "WWMGIFT2026", "addedAt": "2026-11-20T00:00:00.000Z", "verifiedAt": "2026-11-21T00:00:00.000Z", "expiresAt": "2026-12-31T00:00:00.000Z" }
          ],
          "expired": [
            { "code": "OLDCODE123", "addedAt": "2026-06-29T00:00:00.000Z", "expiredAt": "2026-07-15T00:00:00.000Z" }
          ],
          "maintenance": { "active": false, "notice": null }
        }
        """;

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var service = new CodeScraperService(new HttpClient(handler));

        var codes = await service.ScrapeAllAsync([], [CodeScraperService.CodesYarGgApiUrl], 10.0);

        Assert.Equal(new[] { "ANNDWTFD7QE", "WWMGIFT2026" }, codes.Select(c => c.Code).ToArray());
        Assert.All(codes, c => Assert.Equal("codes.yar.gg", c.Source));
    }
}
