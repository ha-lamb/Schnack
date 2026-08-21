using System.Text.Json.Serialization;

namespace Schnack.Models;

/// <summary>
/// Der KI-Dienst, der die Nachbearbeitung übernimmt — Glätten und Übersetzen.
/// Die Spracherkennung ist davon unberührt: sie läuft immer lokal über Whisper.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AiService
{
    [JsonStringEnumMemberName("openai")]
    OpenAi,
    [JsonStringEnumMemberName("claude")]
    Claude
}
