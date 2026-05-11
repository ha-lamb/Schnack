using System.Text.Json.Serialization;

namespace Schnack.Models.OpenAi;

public record ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}

public record ChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("messages")]
    public ChatMessage[] Messages { get; init; } = [];

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; init; }
}
