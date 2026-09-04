using System.Text.Json.Serialization;

namespace WhereWindsMeetItemCodeRedeemer.Models;

public class UiConfig
{
    [JsonPropertyName("exchange_button")]
    public NormalizedPoint? ExchangeButton { get; set; }

    [JsonPropertyName("code_input")]
    public NormalizedPoint? CodeInput { get; set; }

    [JsonPropertyName("submit_button")]
    public NormalizedPoint? SubmitButton { get; set; }

    [JsonPropertyName("cancel_button")]
    public NormalizedPoint? CancelButton { get; set; }

    [JsonPropertyName("coordinate_space")]
    public string CoordinateSpace { get; set; } = "client_normalized";

    [JsonPropertyName("calibrated_client_size")]
    public ClientSize? CalibratedClientSize { get; set; }

    [JsonIgnore]
    public bool IsFullyCalibrated =>
        ExchangeButton != null &&
        CodeInput != null &&
        SubmitButton != null &&
        CancelButton != null;
}
