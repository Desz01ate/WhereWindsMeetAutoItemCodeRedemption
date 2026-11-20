namespace WhereWindsMeetItemCodeRedeemer.Models;

public class ReleaseAssetInfo
{
    public string Name { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    public string FormattedSize => FormatBytes(SizeBytes);

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "Unknown size";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}

public class UpdateCheckResult
{
    public Version CurrentVersion { get; set; } = new(1, 0, 0);
    public Version? LatestVersion { get; set; }
    public string TagName { get; set; } = string.Empty;
    public string ReleaseName { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public ReleaseAssetInfo? Asset { get; set; }
    public List<ReleaseAssetInfo> AllAssets { get; set; } = new();
    public bool IsUpdateAvailable { get; set; }
    public string? ErrorMessage { get; set; }
}

public class DownloadProgress
{
    public long BytesDownloaded { get; init; }
    public long? TotalBytes { get; init; }
    public double? Percentage =>
        TotalBytes is > 0 ? Math.Clamp(BytesDownloaded * 100.0 / TotalBytes.Value, 0, 100) : null;
}
