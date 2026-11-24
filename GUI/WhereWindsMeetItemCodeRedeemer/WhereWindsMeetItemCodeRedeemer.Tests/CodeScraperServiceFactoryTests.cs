using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using WhereWindsMeetItemCodeRedeemer.Models;
using WhereWindsMeetItemCodeRedeemer.Services;
using Xunit;

namespace WhereWindsMeetItemCodeRedeemer.Tests;

public class CodeScraperServiceFactoryTests
{
    /// <summary>An HttpClient that records every requested URL and answers each one with an empty JSON object.</summary>
    private static HttpClient CreateRecordingClient(List<string> requestedUrls) =>
        new(new MockHttpMessageHandler(request =>
        {
            requestedUrls.Add(request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }));

    [Fact]
    public async Task Create_ConfiguredSources_ScrapesTheSourcesInConfig()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WWM_FactoryTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new ConfigService(Path.Combine(tempDir, "config.json"));
            var requestedUrls = new List<string>();
            var factory = new CodeScraperServiceFactory(config, CreateRecordingClient(requestedUrls));

            // Changed after the factory was built: Create must read the config when it is called.
            config.CurrentConfig.Sources = ["https://html.test/page"];
            config.CurrentConfig.ApiSources = ["https://api.test/codes"];

            var scraper = factory.Create(CodeSourceMode.ConfiguredSources);
            await scraper.ScrapeAllAsync(10.0);

            Assert.IsType<CodeScraperService>(scraper);
            Assert.Equal(new[] { "https://api.test/codes", "https://html.test/page" }, requestedUrls);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Create_CodesYarGg_RequestsOnlyTheCodesYarGgApi()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WWM_FactoryTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new ConfigService(Path.Combine(tempDir, "config.json"));
            var requestedUrls = new List<string>();
            var factory = new CodeScraperServiceFactory(config, CreateRecordingClient(requestedUrls));

            var scraper = factory.Create(CodeSourceMode.CodesYarGg);
            await scraper.ScrapeAllAsync(10.0);

            Assert.IsType<CodesYarGgScraperService>(scraper);
            Assert.Equal(new[] { CodesYarGgScraperService.ApiUrl }, requestedUrls);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
