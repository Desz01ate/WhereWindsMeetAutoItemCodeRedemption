using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using WhereWindsMeetItemCodeRedeemer.Models;
using WhereWindsMeetItemCodeRedeemer.Services;
using WhereWindsMeetItemCodeRedeemer.ViewModels;
using Xunit;

namespace WhereWindsMeetItemCodeRedeemer.Tests;

public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _handler(request, cancellationToken);
}

public class MockUpdateService : IUpdateService
{
    public Version CurrentVersion { get; set; } = new(1, 0, 0);
    public string ReleasesPageUrl { get; set; } = "https://github.com/Desz01ate/WhereWindsMeetAutoItemCodeRedemption/releases";
    public UpdateCheckResult NextCheckResult { get; set; } = new() { IsUpdateAvailable = false };
    public string NextDownloadPath { get; set; } = string.Empty;
    public bool LaunchCalled { get; private set; }

    public Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(NextCheckResult);

    public Task<string> DownloadUpdateAsync(
        ReleaseAssetInfo asset,
        string? destinationDirectory = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new DownloadProgress { BytesDownloaded = 50, TotalBytes = 100 });
        progress?.Report(new DownloadProgress { BytesDownloaded = 100, TotalBytes = 100 });
        return Task.FromResult(NextDownloadPath);
    }

    public bool LaunchUpdaterAndApply(string zipFilePath, string? targetDirectory = null, int? currentProcessId = null)
    {
        LaunchCalled = true;
        return true;
    }
}

public class UpdateServiceTests
{
    private const string SampleReleaseJson = """
        {
          "tag_name": "v1.2.0",
          "name": "Release v1.2.0",
          "body": "Added auto-updater and bug fixes.",
          "html_url": "https://github.com/Desz01ate/WhereWindsMeetAutoItemCodeRedemption/releases/tag/v1.2.0",
          "assets": [
            {
              "name": "WhereWindsMeetItemCodeRedeemer-win-x64.zip",
              "browser_download_url": "https://github.com/Desz01ate/WhereWindsMeetAutoItemCodeRedemption/releases/download/v1.2.0/WhereWindsMeetItemCodeRedeemer-win-x64.zip",
              "size": 1048576
            },
            {
              "name": "WhereWindsMeetItemCodeRedeemer-win-x64-selfcontained.zip",
              "browser_download_url": "https://github.com/Desz01ate/WhereWindsMeetAutoItemCodeRedemption/releases/download/v1.2.0/WhereWindsMeetItemCodeRedeemer-win-x64-selfcontained.zip",
              "size": 52428800
            }
          ]
        }
        """;

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3, 0)]
    [InlineData("V2.0.1-beta.1", 2, 0, 1, 0)]
    [InlineData("1.5", 1, 5, 0, 0)]
    [InlineData("1.2.3.4", 1, 2, 3, 4)]
    public void ParseVersion_ValidTags_ReturnsExpectedVersion(string tag, int major, int minor, int build, int revision)
    {
        var parsed = UpdateService.ParseVersion(tag);
        Assert.NotNull(parsed);
        Assert.Equal(new Version(major, minor, build, revision), parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    public void ParseVersion_InvalidTags_ReturnsNull(string tag)
    {
        Assert.Null(UpdateService.ParseVersion(tag));
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenNewerVersionExists_SelectsMatchingAsset()
    {
        Uri? requestedUri = null;
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            requestedUri = req.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleReleaseJson, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);

        // Self-contained instance
        var selfContainedService = new UpdateService(httpClient, new Version(1, 0, 0), isSelfContained: true);
        var scResult = await selfContainedService.CheckForUpdateAsync();

        Assert.Equal(
            "https://api.github.com/repos/Desz01ate/WhereWindsMeetAutoItemCodeRedemption/releases/latest",
            requestedUri?.ToString());
        Assert.True(scResult.IsUpdateAvailable);
        Assert.Equal("v1.2.0", scResult.TagName);
        Assert.NotNull(scResult.Asset);
        Assert.Equal("WhereWindsMeetItemCodeRedeemer-win-x64-selfcontained.zip", scResult.Asset!.Name);
        Assert.Equal(2, scResult.AllAssets.Count);

        // Framework-dependent instance
        var fdService = new UpdateService(httpClient, new Version(1, 0, 0), isSelfContained: false);
        var fdResult = await fdService.CheckForUpdateAsync();

        Assert.True(fdResult.IsUpdateAvailable);
        Assert.NotNull(fdResult.Asset);
        Assert.Equal("WhereWindsMeetItemCodeRedeemer-win-x64.zip", fdResult.Asset!.Name);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenAlreadyLatest_ReportsNoUpdate()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleReleaseJson, Encoding.UTF8, "application/json")
            }));

        using var httpClient = new HttpClient(handler);
        var service = new UpdateService(httpClient, new Version(1, 2, 0), isSelfContained: true);

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(new Version(1, 2, 0, 0), result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenNoReleasesFound_ReturnsFriendlyError()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        using var httpClient = new HttpClient(handler);
        var service = new UpdateService(httpClient, new Version(1, 0, 0), isSelfContained: true);

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.IsUpdateAvailable);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("No published releases", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadUpdateAsync_StreamsArtifactAndReportsProgress()
    {
        var payload = Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
            response.Content.Headers.ContentLength = payload.Length;
            return Task.FromResult(response);
        });

        var tempDir = Path.Combine(Path.GetTempPath(), "WWM_UpdateTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var httpClient = new HttpClient(handler);
            var service = new UpdateService(httpClient, new Version(1, 0, 0), isSelfContained: true);

            var asset = new ReleaseAssetInfo
            {
                Name = "WhereWindsMeetItemCodeRedeemer-win-x64-selfcontained.zip",
                DownloadUrl = "https://github.com/Desz01ate/WhereWindsMeetAutoItemCodeRedemption/releases/download/v1.2.0/WhereWindsMeetItemCodeRedeemer-win-x64-selfcontained.zip",
                SizeBytes = payload.Length
            };

            var progressValues = new List<double>();
            var progress = new SynchronousProgress<DownloadProgress>(p =>
            {
                if (p.Percentage.HasValue) progressValues.Add(p.Percentage.Value);
            });

            var downloadedPath = await service.DownloadUpdateAsync(asset, tempDir, progress);

            Assert.True(File.Exists(downloadedPath));
            Assert.False(File.Exists(downloadedPath + ".part"));
            Assert.Equal(payload, await File.ReadAllBytesAsync(downloadedPath));
            Assert.Contains(100.0, progressValues);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task MainViewModel_CheckDownloadAndDismissUpdate_WorksEndToEnd()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WWM_VMUpdateTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var fakeZipPath = Path.Combine(tempDir, "WhereWindsMeetItemCodeRedeemer-win-x64-selfcontained.zip");
            await File.WriteAllBytesAsync(fakeZipPath, [1, 2, 3]);

            var mockUpdateService = new MockUpdateService
            {
                CurrentVersion = new Version(1, 0, 0),
                NextCheckResult = new UpdateCheckResult
                {
                    CurrentVersion = new Version(1, 0, 0),
                    LatestVersion = new Version(1, 2, 0),
                    TagName = "v1.2.0",
                    ReleaseName = "v1.2.0",
                    HtmlUrl = "https://github.com/Desz01ate/WhereWindsMeetAutoItemCodeRedemption/releases/tag/v1.2.0",
                    IsUpdateAvailable = true,
                    Asset = new ReleaseAssetInfo
                    {
                        Name = "WhereWindsMeetItemCodeRedeemer-win-x64-selfcontained.zip",
                        DownloadUrl = "https://example.com/update.zip",
                        SizeBytes = 1024
                    }
                },
                NextDownloadPath = fakeZipPath
            };

            var vm = new MainViewModel(
                new ConfigService(Path.Combine(tempDir, "config.json")),
                new FakeCodeScraperServiceFactory(),
                new MockGameWindowService(),
                new MockInputSimulationService(),
                new MockCalibrationService(),
                new FakeHotkeyMonitor(),
                mockUpdateService);

            Assert.Equal("v1.0.0", vm.CurrentVersionText);
            Assert.False(vm.IsUpdateBannerVisible);

            await vm.CheckForUpdatesAsync(silent: false);

            Assert.True(vm.HasUpdateAvailable);
            Assert.True(vm.IsUpdateBannerVisible);
            Assert.True(vm.CanDownloadUpdate);
            Assert.Contains("v1.2.0", vm.UpdateBannerTitle);

            await vm.DownloadUpdateAsync();

            Assert.True(vm.IsUpdateDownloaded);
            Assert.Equal(fakeZipPath, vm.DownloadedUpdatePath);
            Assert.False(vm.CanDownloadUpdate);

            bool shutdownRequested = false;
            vm.RequestApplicationShutdown += () => shutdownRequested = true;
            vm.InstallUpdate();

            Assert.True(mockUpdateService.LaunchCalled);
            Assert.True(shutdownRequested);

            vm.DismissUpdateBanner();
            Assert.False(vm.IsUpdateBannerVisible);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
