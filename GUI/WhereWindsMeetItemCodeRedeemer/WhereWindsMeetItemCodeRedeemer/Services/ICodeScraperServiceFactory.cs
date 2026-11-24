using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

/// <summary>
/// Creates the <see cref="ICodeScraperService"/> for a code source. A new source is added as its own
/// scraper class plus one case in the factory, without changing the existing scrapers.
/// </summary>
public interface ICodeScraperServiceFactory
{
    /// <summary>Creates the scraper for <paramref name="source"/>, set up from the current config.</summary>
    ICodeScraperService Create(CodeSourceMode source);
}
