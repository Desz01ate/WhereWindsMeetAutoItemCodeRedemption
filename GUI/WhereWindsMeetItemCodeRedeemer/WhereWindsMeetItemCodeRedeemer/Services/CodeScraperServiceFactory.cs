using System.Net.Http;
using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

public class CodeScraperServiceFactory : ICodeScraperServiceFactory
{
    private readonly IConfigService _configService;
    private readonly HttpClient _httpClient;

    public CodeScraperServiceFactory(IConfigService configService, HttpClient? httpClient = null)
    {
        _configService = configService;

        // Every scraper the factory creates shares one HttpClient, so connections are reused between fetches.
        _httpClient = httpClient ?? CodeScraperService.CreateDefaultHttpClient();
    }

    public ICodeScraperService Create(CodeSourceMode source)
    {
        var config = _configService.CurrentConfig;

        return source switch
        {
            CodeSourceMode.ConfiguredSources => new CodeScraperService(config.Sources, config.ApiSources, _httpClient),
            CodeSourceMode.CodesYarGg => new CodesYarGgScraperService(_httpClient),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown code source.")
        };
    }
}
