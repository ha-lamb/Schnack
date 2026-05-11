using System.Text.Json.Serialization;

namespace Schnack.Models.Claude;

public record MessageItem
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}

public record MessagesRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; }

    [JsonPropertyName("messages")]
    public MessageItem[] Messages { get; init; } = [];
}
