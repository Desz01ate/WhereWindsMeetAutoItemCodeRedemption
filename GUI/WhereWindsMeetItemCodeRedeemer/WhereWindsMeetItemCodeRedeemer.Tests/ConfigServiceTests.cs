using System.IO;
using System.Text.Json;
using WhereWindsMeetItemCodeRedeemer.Models;
using WhereWindsMeetItemCodeRedeemer.Services;
using Xunit;

namespace WhereWindsMeetItemCodeRedeemer.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void ResolveStatePath_WithRelativePath_ResolvesRelativeToConfigDirectory()
    {
        var configPath = @"C:\Games\WWM\config.json";
        var statePath = "redeemed_codes.json";

        var resolved = ConfigService.ResolveStatePath(configPath, statePath);

        Assert.Equal(@"C:\Games\WWM\redeemed_codes.json", resolved);
    }

    [Fact]
    public void ResolveStatePath_WithAbsolutePath_KeepsAbsolutePath()
    {
        var configPath = @"C:\Games\WWM\config.json";
        var statePath = @"D:\CustomState\state.json";

        var resolved = ConfigService.ResolveStatePath(configPath, statePath);

        Assert.Equal(@"D:\CustomState\state.json", resolved);
    }

    [Fact]
    public void NormalizedPoint_SerializesAsTwoElementArray()
    {
        var point = new NormalizedPoint(0.552907, 0.246528);
        var json = JsonSerializer.Serialize(point);

        Assert.Equal("[0.552907,0.246528]", json);

        var deserialized = JsonSerializer.Deserialize<NormalizedPoint>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(0.552907, deserialized.X, 6);
        Assert.Equal(0.246528, deserialized.Y, 6);
    }

    [Fact]
    public void ClientSize_SerializesAsTwoElementArray()
    {
        var size = new ClientSize(3440, 1440);
        var json = JsonSerializer.Serialize(size);

        Assert.Equal("[3440,1440]", json);

        var deserialized = JsonSerializer.Deserialize<ClientSize>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(3440, deserialized.Width);
        Assert.Equal(1440, deserialized.Height);
    }

    [Fact]
    public void ConfigService_LoadAndSaveState_RoundtripsSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WWM_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "config.json");
            var service = new ConfigService(configPath);

            var sampleCodes = new[] { "CODE_ALPHA", "CODE_BETA", "CODE_GAMMA" };
            service.SaveRedeemedCodes(sampleCodes);

            var loaded = service.LoadRedeemedCodes();
            Assert.Equal(3, loaded.Count);
            Assert.Contains("CODE_ALPHA", loaded);
            Assert.Contains("CODE_BETA", loaded);
            Assert.Contains("CODE_GAMMA", loaded);

            service.AddRedeemedCode("CODE_DELTA");
            var updated = service.LoadRedeemedCodes();
            Assert.Equal(4, updated.Count);
            Assert.Contains("CODE_DELTA", updated);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
