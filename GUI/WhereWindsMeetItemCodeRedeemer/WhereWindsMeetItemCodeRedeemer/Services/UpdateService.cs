using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

public class UpdateService : IUpdateService
{
    public const string DefaultOwner = "Desz01ate";
    public const string DefaultRepo = "WhereWindsMeetAutoItemCodeRedemption";

    private readonly HttpClient _httpClient;
    private readonly bool _isSelfContained;
    private readonly string _latestReleaseApiUrl;

    public Version CurrentVersion { get; }
    public string ReleasesPageUrl { get; }

    public UpdateService(
        HttpClient? httpClient = null,
        Version? currentVersion = null,
        bool? isSelfContained = null,
        string owner = DefaultOwner,
        string repo = DefaultRepo)
    {
        CurrentVersion = NormalizeVersion(currentVersion ?? GetAssemblyVersion());
        _isSelfContained = isSelfContained ?? DetectSelfContained();
        _latestReleaseApiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        ReleasesPageUrl = $"https://github.com/{owner}/{repo}/releases";
        _httpClient = httpClient ?? CreateDefaultHttpClient(CurrentVersion);
    }

    public static HttpClient CreateDefaultHttpClient(Version? version = null)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        var verStr = version != null ? FormatVersion(version) : "1.0.0";
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.Add("User-Agent", $"WhereWindsMeetItemCodeRedeemer/{verStr}");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        using var request = new HttpRequestMessage(HttpMethod.Get, _latestReleaseApiUrl);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                HtmlUrl = ReleasesPageUrl,
                IsUpdateAvailable = false,
                ErrorMessage = "No published releases found on GitHub yet."
            };
        }

        if (response.StatusCode == HttpStatusCode.Forbidden &&
            response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
            remaining.FirstOrDefault() == "0")
        {
            return new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                HtmlUrl = ReleasesPageUrl,
                IsUpdateAvailable = false,
                ErrorMessage = "GitHub API rate limit exceeded. Please try again later."
            };
        }

        response.EnsureSuccessStatusCode();

        var release = await response.Content.ReadFromJsonAsync<GitHubReleaseResponse>(cancellationToken: cts.Token);
        if (release == null)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                HtmlUrl = ReleasesPageUrl,
                IsUpdateAvailable = false,
                ErrorMessage = "GitHub returned an empty release response."
            };
        }

        var tagName = release.TagName ?? string.Empty;
        var releaseName = string.IsNullOrWhiteSpace(release.Name) ? tagName : release.Name;
        var body = release.Body ?? string.Empty;
        var htmlUrl = string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasesPageUrl : release.HtmlUrl;

        var assets = (release.Assets ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.BrowserDownloadUrl))
            .Select(a => new ReleaseAssetInfo
            {
                Name = a.Name!,
                DownloadUrl = a.BrowserDownloadUrl!,
                SizeBytes = a.Size
            })
            .ToList();

        var latestVersion = ParseVersion(tagName);
        var isUpdateAvailable = latestVersion != null && NormalizeVersion(latestVersion) > CurrentVersion;
        var selectedAsset = SelectBestAsset(assets, _isSelfContained);

        return new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = latestVersion != null ? NormalizeVersion(latestVersion) : null,
            TagName = tagName,
            ReleaseName = releaseName,
            ReleaseNotes = body,
            HtmlUrl = htmlUrl,
            Asset = selectedAsset,
            AllAssets = assets,
            IsUpdateAvailable = isUpdateAvailable
        };
    }

    private sealed record GitHubReleaseResponse(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("assets")] List<GitHubReleaseAsset>? Assets);

    private sealed record GitHubReleaseAsset(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl,
        [property: JsonPropertyName("size")] long Size);

    public async Task<string> DownloadUpdateAsync(
        ReleaseAssetInfo asset,
        string? destinationDirectory = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(asset.DownloadUrl))
            throw new ArgumentException("Asset download URL cannot be empty.", nameof(asset));

        var targetDir = string.IsNullOrWhiteSpace(destinationDirectory)
            ? Path.Combine(Path.GetTempPath(), "WhereWindsMeetItemCodeRedeemer", "Updates")
            : destinationDirectory;

        Directory.CreateDirectory(targetDir);

        var safeFileName = Path.GetFileName(asset.Name);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            safeFileName = "update-artifact.zip";
        }

        var finalPath = Path.Combine(targetDir, safeFileName);
        var tempPath = finalPath + ".part";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? (asset.SizeBytes > 0 ? asset.SizeBytes : null);

            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                progress?.Report(new DownloadProgress
                {
                    BytesDownloaded = 0,
                    TotalBytes = totalBytes
                });

                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalRead += bytesRead;

                    progress?.Report(new DownloadProgress
                    {
                        BytesDownloaded = totalRead,
                        TotalBytes = totalBytes
                    });
                }
            }

            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }
            File.Move(tempPath, finalPath);

            return finalPath;
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
            throw;
        }
    }

    public bool LaunchUpdaterAndApply(
        string zipFilePath,
        string? targetDirectory = null,
        int? currentProcessId = null)
    {
        if (!File.Exists(zipFilePath))
            throw new FileNotFoundException("Downloaded update archive not found.", zipFilePath);

        var appDir = string.IsNullOrWhiteSpace(targetDirectory)
            ? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : targetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var pid = currentProcessId ?? Environment.ProcessId;
        var stagingDir = Path.Combine(
            Path.GetTempPath(),
            "WhereWindsMeetItemCodeRedeemer",
            "Staging_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(stagingDir);
        ZipFile.ExtractToDirectory(zipFilePath, stagingDir, overwriteFiles: true);

        // Preserve existing user configuration and redeemed code history
        PreserveUserFileIfExists(appDir, stagingDir, "config.json");
        PreserveUserFileIfExists(appDir, stagingDir, "redeemed_codes.json");

        var exeName = Path.GetFileName(Environment.ProcessPath);
        if (string.IsNullOrWhiteSpace(exeName) || !exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            exeName = "WhereWindsMeetItemCodeRedeemer.exe";
        }

        var scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"wwm_updater_{Guid.NewGuid():N}.cmd");

        var scriptContent = $"""
            @echo off
            setlocal
            set "PID={pid}"
            set "STAGING={stagingDir}"
            set "TARGET={appDir}"
            set "ZIP={zipFilePath}"
            set "EXE={Path.Combine(appDir, exeName)}"

            :waitloop
            tasklist /FI "PID eq %PID%" 2>NUL | find /I "%PID%" >NUL
            if not errorlevel 1 (
                timeout /t 1 /nobreak >NUL
                goto waitloop
            )

            xcopy "%STAGING%\*" "%TARGET%\" /E /Y /I /Q >NUL
            rmdir /S /Q "%STAGING%" >NUL 2>&1
            del /F /Q "%ZIP%" >NUL 2>&1

            if exist "%EXE%" (
                start "" "%EXE%"
            )

            del /F /Q "%~f0" >NUL 2>&1
            """;

        File.WriteAllText(scriptPath, scriptContent);

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/C \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        var process = Process.Start(startInfo);
        return process != null;
    }

    public static ReleaseAssetInfo? SelectBestAsset(IReadOnlyList<ReleaseAssetInfo> assets, bool preferSelfContained)
    {
        if (assets.Count == 0) return null;

        var zipAssets = assets
            .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (zipAssets.Count == 0)
            return assets[0];

        static bool IsSelfContainedAsset(ReleaseAssetInfo a) =>
            a.Name.Contains("selfcontained", StringComparison.OrdinalIgnoreCase) ||
            a.Name.Contains("self-contained", StringComparison.OrdinalIgnoreCase);

        if (preferSelfContained)
        {
            return zipAssets.FirstOrDefault(IsSelfContainedAsset) ?? zipAssets[0];
        }

        return zipAssets.FirstOrDefault(a => !IsSelfContainedAsset(a)) ?? zipAssets[0];
    }

    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var cleaned = tag.Trim();
        if (cleaned.StartsWith('v') || cleaned.StartsWith('V'))
        {
            cleaned = cleaned[1..];
        }

        // Strip prerelease or build metadata (e.g. "1.2.3-beta.1+sha")
        var suffixIndex = cleaned.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            cleaned = cleaned[..suffixIndex];
        }

        return Version.TryParse(cleaned, out var parsed) ? NormalizeVersion(parsed) : null;
    }

    public static Version NormalizeVersion(Version version)
    {
        return new Version(
            Math.Max(0, version.Major),
            Math.Max(0, version.Minor),
            Math.Max(0, version.Build),
            Math.Max(0, version.Revision));
    }

    public static string FormatVersion(Version version)
    {
        var normalized = NormalizeVersion(version);
        return normalized.Revision > 0
            ? $"{normalized.Major}.{normalized.Minor}.{normalized.Build}.{normalized.Revision}"
            : $"{normalized.Major}.{normalized.Minor}.{normalized.Build}";
    }

    private static Version GetAssemblyVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly;
        return assembly.GetName().Version ?? new Version(1, 0, 0);
    }

    private static bool DetectSelfContained()
    {
        // In single-file self-contained publish, the managed DLL is bundled inside the .exe
        var managedDll = Path.Combine(AppContext.BaseDirectory, "WhereWindsMeetItemCodeRedeemer.dll");
        return !File.Exists(managedDll);
    }

    private static void PreserveUserFileIfExists(string appDir, string stagingDir, string fileName)
    {
        var existingUserFile = Path.Combine(appDir, fileName);
        var stagedFile = Path.Combine(stagingDir, fileName);
        if (File.Exists(existingUserFile) && File.Exists(stagedFile))
        {
            File.Delete(stagedFile);
        }
    }
}
