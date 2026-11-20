using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

public interface IUpdateService
{
    Version CurrentVersion { get; }
    string ReleasesPageUrl { get; }

    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    Task<string> DownloadUpdateAsync(
        ReleaseAssetInfo asset,
        string? destinationDirectory = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    bool LaunchUpdaterAndApply(
        string zipFilePath,
        string? targetDirectory = null,
        int? currentProcessId = null);
}
