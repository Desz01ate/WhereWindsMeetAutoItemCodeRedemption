using System.Net;
using System.Net.Http;
using System.Text;
using WhereWindsMeetItemCodeRedeemer.Models;
using WhereWindsMeetItemCodeRedeemer.Services;
using Xunit;

namespace WhereWindsMeetItemCodeRedeemer.Tests;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }
}

public class CodeScraperServiceTests
{
    [Fact]
    public async Task ScrapeApiAsync_ExtractsValidCodes_AndIgnoresStopwords()
    {
        var jsonResponse = """
        {
          "active": [
            { "code": "VALIDCODE1", "rewards": "500 Coins" },
            { "code": "rewards", "rewards": "Stopword" },
            { "code": "active", "rewards": "Stopword" },
            { "code": "VALIDCODE2", "rewards": "Special Outfit" },
            { "code": "SHORT" }
          ]
        }
        """;

        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler);
        var service = new CodeScraperService(client);

        var codes = await service.ScrapeApiAsync("https://api.test/codes", 10.0);

        Assert.Equal(2, codes.Count);
        Assert.Contains(codes, c => c.Code == "VALIDCODE1");
        Assert.Contains(codes, c => c.Code == "VALIDCODE2");
    }

    [Fact]
    public async Task ScrapeHtmlAsync_ExtractsFromAttributesAndTables()
    {
        var htmlResponse = """
        <html>
        <body>
            <input type="text" value="ATTRCODE1" />
            <button data-code="ATTRCODE2">Copy</button>
            <table>
                <tr>
                    <td><span>TABLECODE1</span></td>
                    <td>100 Echoes</td>
                </tr>
                <tr>
                    <td><strong>EXPIRED</strong></td>
                    <td>Should be ignored</td>
                </tr>
                <tr>
                    <td>TABLECODE2</td>
                    <td>200 Silk</td>
                </tr>
            </table>
        </body>
        </html>
        """;

        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(htmlResponse, Encoding.UTF8, "text/html")
        });

        var client = new HttpClient(handler);
        var service = new CodeScraperService(client);

        var codes = await service.ScrapeHtmlAsync("https://site.test/codes", 10.0);

        Assert.Equal(4, codes.Count);
        Assert.Contains(codes, c => c.Code == "ATTRCODE1");
        Assert.Contains(codes, c => c.Code == "ATTRCODE2");
        Assert.Contains(codes, c => c.Code == "TABLECODE1");
        Assert.Contains(codes, c => c.Code == "TABLECODE2");
    }

    [Fact]
    public async Task ScrapeAllAsync_DeduplicatesAcrossMultipleSources()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("api"))
            {
                var apiJson = """{ "active": [{ "code": "SHAREDCODE" }, { "code": "APIONLY" }] }""";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(apiJson, Encoding.UTF8, "application/json")
                };
            }
            else
            {
                var html = """<button data-code="SHAREDCODE"></button><button data-code="HTMLONLY"></button>""";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(html, Encoding.UTF8, "text/html")
                };
            }
        });

        var client = new HttpClient(handler);
        var service = new CodeScraperService(client);

        var allCodes = await service.ScrapeAllAsync(
            new[] { "https://test.com/page" },
            new[] { "https://test.com/api" },
            10.0);

        Assert.Equal(3, allCodes.Count);
        Assert.Contains(allCodes, c => c.Code == "SHAREDCODE");
        Assert.Contains(allCodes, c => c.Code == "APIONLY");
        Assert.Contains(allCodes, c => c.Code == "HTMLONLY");
    }
}
