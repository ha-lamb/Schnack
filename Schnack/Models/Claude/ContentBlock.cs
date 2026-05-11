using System.Text.Json.Serialization;

namespace Schnack.Models.Claude;

public record ContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}
