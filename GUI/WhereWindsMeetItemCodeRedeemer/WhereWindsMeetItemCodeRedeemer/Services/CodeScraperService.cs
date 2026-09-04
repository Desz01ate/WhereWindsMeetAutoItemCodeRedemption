using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

public class CodeScraperService : ICodeScraperService
{
    private static readonly Regex CodePattern = new(@"\b[A-Z0-9]{6,32}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExactCodePattern = new(@"^[A-Z0-9]{6,32}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AttributePattern = new(@"(?:value|data-code)=[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TableRowPattern = new(@"<tr\b[^>]*>\s*<td\b[^>]*>(.*?)</td>\s*<td\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TagStripPattern = new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "REWARDS", "ACTIVE", "EXPIRED", "WHEREWINDS"
    };

    private readonly HttpClient _httpClient;

    public CodeScraperService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
    }

    public static HttpClient CreateDefaultHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.Add("User-Agent", "WhereWindsMeetCodeRedeemer/1.0");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        return client;
    }

    public async Task<List<RedeemCodeItem>> ScrapeAllAsync(
        IEnumerable<string> htmlSources,
        IEnumerable<string> apiSources,
        double timeoutSeconds,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, RedeemCodeItem>(StringComparer.OrdinalIgnoreCase);

        // 1. Scrape API sources
        foreach (var url in apiSources)
        {
            if (string.IsNullOrWhiteSpace(url)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Fetching API: {url}...");

            try
            {
                var codes = await ScrapeApiAsync(url, timeoutSeconds, cancellationToken);
                foreach (var codeItem in codes)
                {
                    if (ExactCodePattern.IsMatch(codeItem.Code) && !StopWords.Contains(codeItem.Code))
                    {
                        found.TryAdd(codeItem.Code, codeItem);
                    }
                }
                progress?.Report($"[OK] {url} returned {codes.Count} codes.");
            }
            catch (Exception ex)
            {
                progress?.Report($"[Warning] API source unavailable: {url} ({ex.Message})");
            }
        }

        // 2. Scrape HTML sources
        foreach (var url in htmlSources)
        {
            if (string.IsNullOrWhiteSpace(url)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Scraping HTML: {url}...");

            try
            {
                var codes = await ScrapeHtmlAsync(url, timeoutSeconds, cancellationToken);
                int added = 0;
                foreach (var codeItem in codes)
                {
                    if (found.TryAdd(codeItem.Code, codeItem))
                    {
                        added++;
                    }
                }
                progress?.Report($"[OK] {url} scanned ({added} new codes).");
            }
            catch (Exception ex)
            {
                progress?.Report($"[Warning] HTML source unavailable: {url} ({ex.Message})");
            }
        }

        return found.Values.OrderBy(c => c.Code).ToList();
    }

    public async Task<List<RedeemCodeItem>> ScrapeApiAsync(
        string url,
        double timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));

        var json = await _httpClient.GetStringAsync(url, cts.Token);
        var list = new List<RedeemCodeItem>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("active", out var activeElement) && activeElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in activeElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("code", out var codeProp))
                {
                    var codeStr = codeProp.GetString()?.Trim().ToUpperInvariant();
                    if (!string.IsNullOrEmpty(codeStr) && ExactCodePattern.IsMatch(codeStr) && !StopWords.Contains(codeStr))
                    {
                        list.Add(new RedeemCodeItem
                        {
                            Code = codeStr,
                            Source = GetSourceDomain(url),
                            Status = CodeStatus.Pending,
                            IsSelected = true
                        });
                    }
                }
            }
        }

        return list;
    }

    public async Task<List<RedeemCodeItem>> ScrapeHtmlAsync(
        string url,
        double timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));

        var body = await _httpClient.GetStringAsync(url, cts.Token);
        var decodedBody = WebUtility.HtmlDecode(body);

        var candidates = new List<string>();

        // Find value="..." or data-code="..."
        foreach (Match match in AttributePattern.Matches(decodedBody))
        {
            if (match.Groups.Count > 1)
            {
                candidates.Add(match.Groups[1].Value);
            }
        }

        // Find table row first cell
        foreach (Match rowMatch in TableRowPattern.Matches(decodedBody))
        {
            if (rowMatch.Groups.Count > 1)
            {
                var rowHtml = rowMatch.Groups[1].Value;
                var textOnly = TagStripPattern.Replace(rowHtml, " ");
                foreach (Match codeMatch in CodePattern.Matches(textOnly))
                {
                    candidates.Add(codeMatch.Value);
                }
            }
        }

        var sourceDomain = GetSourceDomain(url);
        var result = new Dictionary<string, RedeemCodeItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in candidates)
        {
            var value = raw.Trim().ToUpperInvariant();
            if (!ExactCodePattern.IsMatch(value) || StopWords.Contains(value))
                continue;

            result.TryAdd(value, new RedeemCodeItem
            {
                Code = value,
                Source = sourceDomain,
                Status = CodeStatus.Pending,
                IsSelected = true
            });
        }

        return result.Values.ToList();
    }

    private static string GetSourceDomain(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch
        {
            return url;
        }
    }
}
