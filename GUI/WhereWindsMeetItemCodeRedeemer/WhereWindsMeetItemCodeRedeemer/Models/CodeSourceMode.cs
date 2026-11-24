namespace WhereWindsMeetItemCodeRedeemer.Models;

/// <summary>Which scraper "Fetch Latest Codes" uses. See <c>ICodeScraperServiceFactory</c>.</summary>
public enum CodeSourceMode
{
    /// <summary>The websites and APIs listed in config.json ("sources" + "api_sources").</summary>
    ConfiguredSources,

    /// <summary>Only the codes.yar.gg API.</summary>
    CodesYarGg
}
