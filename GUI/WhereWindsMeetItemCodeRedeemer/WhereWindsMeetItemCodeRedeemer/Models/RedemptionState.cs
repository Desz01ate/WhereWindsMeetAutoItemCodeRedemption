using System.Text.Json.Serialization;

namespace WhereWindsMeetItemCodeRedeemer.Models;

public class RedemptionState
{
    [JsonPropertyName("redeemed")]
    public List<string> Redeemed { get; set; } = new();
}
