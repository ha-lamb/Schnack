using System.Text.Json.Serialization;

namespace Schnack.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackendProvider
{
    [JsonStringEnumMemberName("openai")]
    OpenAi,
    [JsonStringEnumMemberName("claude")]
    Claude
}
