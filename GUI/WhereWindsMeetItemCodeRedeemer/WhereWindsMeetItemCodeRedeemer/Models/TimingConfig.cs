using System.Text.Json.Serialization;

namespace WhereWindsMeetItemCodeRedeemer.Models;

public class TimingConfig
{
    [JsonPropertyName("page_timeout_seconds")]
    public double PageTimeoutSeconds { get; set; } = 30.0;

    [JsonPropertyName("ui_delay_seconds")]
    public double UiDelaySeconds { get; set; } = 0.35;

    [JsonPropertyName("result_wait_seconds")]
    public double ResultWaitSeconds { get; set; } = 2.0;
}
