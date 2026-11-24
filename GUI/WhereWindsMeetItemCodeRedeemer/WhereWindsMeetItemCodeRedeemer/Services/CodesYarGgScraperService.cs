using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

/// <summary>
/// Reads the community-verified code list at https://codes.yar.gg (one request per fetch).
/// Its robots.txt explicitly allows /api/codes.
/// </summary>
public class CodesYarGgScraperService : ICodeScraperService
{
    public const string ApiUrl = "https://codes.yar.gg/api/codes";
    public const string SourceName = "codes.yar.gg";

    private static readonly Regex ExactCodePattern = new(@"^[A-Z0-9]{6,32}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;

    public CodesYarGgScraperService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CodeScraperService.CreateDefaultHttpClient();
    }

    public async Task<List<RedeemCodeItem>> ScrapeAllAsync(
        double timeoutSeconds,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report($"Fetching API: {ApiUrl}...");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));

            var json = await _httpClient.GetStringAsync(ApiUrl, cts.Token);
            var response = JsonSerializer.Deserialize<CodesResponse>(json, JsonOptions);

            if (response?.Maintenance?.Active == true)
            {
                progress?.Report($"[Warning] {SourceName} is under maintenance: {response.Maintenance.Notice ?? "no notice given"}");
            }

            // Only "active" codes; the "expired" list is ignored.
            var codes = (response?.Active ?? [])
                .Select(entry => entry.Code?.Trim().ToUpperInvariant())
                .OfType<string>()
                .Where(code => ExactCodePattern.IsMatch(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .Select(code => new RedeemCodeItem
                {
                    Code = code,
                    Source = SourceName,
                    Status = CodeStatus.Pending,
                    IsSelected = true
                })
                .ToList();

            progress?.Report($"[OK] {ApiUrl} returned {codes.Count} codes.");
            return codes;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            progress?.Report($"[Warning] API source unavailable: {ApiUrl} ({ex.Message})");
            return [];
        }
    }

    // The parts of the https://codes.yar.gg/api/codes response this scraper reads.
    private sealed record CodesResponse(List<CodeEntry>? Active, MaintenanceInfo? Maintenance);

    private sealed record CodeEntry(string? Code);

    private sealed record MaintenanceInfo(bool Active, string? Notice);
}
