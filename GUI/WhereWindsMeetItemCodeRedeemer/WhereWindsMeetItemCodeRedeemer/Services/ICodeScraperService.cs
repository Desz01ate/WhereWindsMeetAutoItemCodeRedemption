using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

/// <summary>
/// One way of finding codes. Each implementation knows its own sources, so callers can swap
/// scrapers without knowing where the codes come from. Created by <see cref="ICodeScraperServiceFactory"/>.
/// </summary>
public interface ICodeScraperService
{
    Task<List<RedeemCodeItem>> ScrapeAllAsync(
        double timeoutSeconds,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
