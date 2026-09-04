using WhereWindsMeetItemCodeRedeemer.Models;

namespace WhereWindsMeetItemCodeRedeemer.Services;

public interface IConfigService
{
    string ConfigFilePath { get; }
    string StateFilePath { get; }
    AppConfig CurrentConfig { get; }

    AppConfig LoadConfig(string? customPath = null);
    void SaveConfig(AppConfig config);
    HashSet<string> LoadRedeemedCodes();
    void SaveRedeemedCodes(IEnumerable<string> redeemedCodes);
    void AddRedeemedCode(string code);
}
