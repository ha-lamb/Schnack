using System.Text.Json.Serialization;

namespace Schnack.Models.Claude;

public record MessagesResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; init; }

    [JsonPropertyName("content")]
    public ContentBlock[] Content { get; init; } = [];
}
