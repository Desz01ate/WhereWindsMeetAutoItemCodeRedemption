using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

public interface ICodeScraperService
{
    Task<List<RedeemCodeItem>> ScrapeAllAsync(
        IEnumerable<string> htmlSources,
        IEnumerable<string> apiSources,
        double timeoutSeconds,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task<List<RedeemCodeItem>> ScrapeApiAsync(
        string url,
        double timeoutSeconds,
        CancellationToken cancellationToken = default);

    Task<List<RedeemCodeItem>> ScrapeHtmlAsync(
        string url,
        double timeoutSeconds,
        CancellationToken cancellationToken = default);
}
