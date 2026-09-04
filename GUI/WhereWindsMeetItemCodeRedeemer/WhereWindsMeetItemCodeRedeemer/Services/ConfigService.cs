using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

public class ConfigService : IConfigService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public string ConfigFilePath { get; private set; } = string.Empty;
    public string StateFilePath => ResolveStatePath(ConfigFilePath, CurrentConfig.StateFile);
    public AppConfig CurrentConfig { get; private set; } = new();

    public ConfigService(string? explicitConfigPath = null)
    {
        LoadConfig(explicitConfigPath);
    }

    public static string FindConfigFile(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);

        // 1. Check current directory
        var cwdCandidate = Path.Combine(Environment.CurrentDirectory, "config.json");
        if (File.Exists(cwdCandidate))
            return Path.GetFullPath(cwdCandidate);

        // 2. Check base directory
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var baseCandidate = Path.Combine(baseDir, "config.json");
        if (File.Exists(baseCandidate))
            return Path.GetFullPath(baseCandidate);

        // 3. Search up from base directory (up to 5 levels)
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "config.json");
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
            dir = dir.Parent;
        }

        // Fallback: use AppDomain base directory
        return Path.Combine(baseDir, "config.json");
    }

    public static string ResolveStatePath(string configPath, string configuredStatePath)
    {
        if (string.IsNullOrWhiteSpace(configuredStatePath))
            configuredStatePath = "redeemed_codes.json";

        if (Path.IsPathRooted(configuredStatePath))
            return Path.GetFullPath(configuredStatePath);

        var configDir = string.IsNullOrWhiteSpace(configPath)
            ? AppDomain.CurrentDomain.BaseDirectory
            : Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? AppDomain.CurrentDomain.BaseDirectory;

        return Path.GetFullPath(Path.Combine(configDir, configuredStatePath));
    }

    public AppConfig LoadConfig(string? customPath = null)
    {
        ConfigFilePath = FindConfigFile(customPath);

        if (File.Exists(ConfigFilePath))
        {
            try
            {
                var json = File.ReadAllText(ConfigFilePath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
                CurrentConfig = loaded ?? new AppConfig();
            }
            catch (Exception)
            {
                CurrentConfig = new AppConfig();
            }
        }
        else
        {
            CurrentConfig = new AppConfig();
            SaveConfig(CurrentConfig);
        }

        EnsureStateFile();
        return CurrentConfig;
    }

    public void SaveConfig(AppConfig config)
    {
        CurrentConfig = config;
        var dir = Path.GetDirectoryName(ConfigFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(ConfigFilePath, json + Environment.NewLine);
    }

    public HashSet<string> LoadRedeemedCodes()
    {
        var path = StateFilePath;
        if (!File.Exists(path))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<RedemptionState>(json, _jsonOptions);
            return state?.Redeemed != null
                ? new HashSet<string>(state.Redeemed.Select(c => c.Trim().ToUpperInvariant()), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SaveRedeemedCodes(IEnumerable<string> redeemedCodes)
    {
        var path = StateFilePath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var sorted = redeemedCodes
            .Select(c => c.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();

        var state = new RedemptionState { Redeemed = sorted };
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        File.WriteAllText(path, json + Environment.NewLine);
    }

    public void AddRedeemedCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        var codes = LoadRedeemedCodes();
        codes.Add(code.Trim().ToUpperInvariant());
        SaveRedeemedCodes(codes);
    }

    private void EnsureStateFile()
    {
        var path = StateFilePath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (!File.Exists(path))
        {
            SaveRedeemedCodes(Enumerable.Empty<string>());
        }
    }
}
