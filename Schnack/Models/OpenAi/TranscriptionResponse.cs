using System.Text.Json.Serialization;

namespace Schnack.Models.OpenAi;

public record TranscriptionResponse
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}
