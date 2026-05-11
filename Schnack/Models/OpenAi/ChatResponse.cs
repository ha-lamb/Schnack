using System.Text.Json.Serialization;

namespace Schnack.Models.OpenAi;

public record ChatResponseMessage
{
    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}

public record ChatChoice
{
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }

    [JsonPropertyName("message")]
    public ChatResponseMessage Message { get; init; } = new();
}

public record ChatResponse
{
    [JsonPropertyName("choices")]
    public ChatChoice[] Choices { get; init; } = [];
}
