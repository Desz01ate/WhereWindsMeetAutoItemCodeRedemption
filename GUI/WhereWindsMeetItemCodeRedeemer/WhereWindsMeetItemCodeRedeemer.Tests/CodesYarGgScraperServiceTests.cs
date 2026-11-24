using System.Net;
using System.Net.Http;
using System.Text;
using WhereWindsMeetItemCodeRedeemer.Services;
using Xunit;

namespace WhereWindsMeetItemCodeRedeemer.Tests;

public class CodesYarGgScraperServiceTests
{
    /// <summary>Collects progress messages as they are reported (Progress&lt;T&gt; would post them later).</summary>
    private sealed class ListProgress : IProgress<string>
    {
        public List<string> Messages { get; } = [];

        public void Report(string value) => Messages.Add(value);
    }

    private static CodesYarGgScraperService CreateService(HttpStatusCode status, string body) =>
        new(new HttpClient(new MockHttpMessageHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        })));

    [Fact]
    public async Task ScrapeAllAsync_ReturnsActiveCodesOnly()
    {
        // Same shape as https://codes.yar.gg/api/codes (checked 2026-11-23).
        var json = """
        {
          "updatedAt": "2026-11-23T10:38:08.901Z",
          "active": [
            { "code": "WWMGIFT2026", "addedAt": "2026-11-20T00:00:00.000Z", "verifiedAt": "2026-11-21T00:00:00.000Z", "expiresAt": "2026-12-31T00:00:00.000Z" },
            { "code": "anndwtfd7qe", "addedAt": "2026-11-23T10:38:08.901Z", "verifiedAt": "2026-11-23T10:38:08.901Z" },
            { "code": "SHORT" }
          ],
          "expired": [
            { "code": "OLDCODE123", "addedAt": "2026-06-29T00:00:00.000Z", "expiredAt": "2026-07-15T00:00:00.000Z" }
          ],
          "maintenance": { "active": false, "notice": null }
        }
        """;
        var service = CreateService(HttpStatusCode.OK, json);

        var codes = await service.ScrapeAllAsync(10.0);

        Assert.Equal(new[] { "ANNDWTFD7QE", "WWMGIFT2026" }, codes.Select(c => c.Code).ToArray());
        Assert.All(codes, c => Assert.Equal(CodesYarGgScraperService.SourceName, c.Source));
    }

    [Fact]
    public async Task ScrapeAllAsync_ReportsMaintenanceNotice()
    {
        var json = """
        {
          "active": [{ "code": "STILLLISTED1" }],
          "maintenance": { "active": true, "notice": "Codes are being re-verified." }
        }
        """;
        var service = CreateService(HttpStatusCode.OK, json);
        var progress = new ListProgress();

        var codes = await service.ScrapeAllAsync(10.0, progress);

        Assert.Single(codes, c => c.Code == "STILLLISTED1");
        Assert.Contains(progress.Messages, m => m.Contains("under maintenance") && m.Contains("Codes are being re-verified."));
    }

    [Fact]
    public async Task ScrapeAllAsync_WhenTheRequestFails_ReturnsNoCodesAndWarns()
    {
        var service = CreateService(HttpStatusCode.InternalServerError, "oops");
        var progress = new ListProgress();

        var codes = await service.ScrapeAllAsync(10.0, progress);

        Assert.Empty(codes);
        Assert.Contains(progress.Messages, m => m.StartsWith("[Warning]") && m.Contains(CodesYarGgScraperService.ApiUrl));
    }
}
