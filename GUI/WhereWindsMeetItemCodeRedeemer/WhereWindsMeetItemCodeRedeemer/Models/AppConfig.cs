using System.Text.Json.Serialization;

namespace WhereWindsMeetItemCodeRedeemer.Models;

public class AppConfig
{
    [JsonPropertyName("process_name")]
    public string ProcessName { get; set; } = "wwm.exe";

    [JsonPropertyName("state_file")]
    public string StateFile { get; set; } = "redeemed_codes.json";

    [JsonPropertyName("sources")]
    public List<string> Sources { get; set; } = new()
    {
        "https://game8.co/games/Where-Winds-Meet/archives/564660",
        "https://www.pcgamer.com/games/action/where-winds-meet-codes",
        "https://www.gamsgo.com/blog/where-winds-meet-codes"
    };

    [JsonPropertyName("api_sources")]
    public List<string> ApiSources { get; set; } = new()
    {
        "https://codes.yar.gg/api/codes"
    };

    [JsonPropertyName("timing")]
    public TimingConfig Timing { get; set; } = new();

    [JsonPropertyName("ui")]
    public UiConfig Ui { get; set; } = new();
}
