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

    /// <summary>
    /// Regeln gehören hierhin, nicht in die Nutzernachricht: das System-Feld wiegt schwerer,
    /// und das Transkript kann so nicht als Anweisung missverstanden werden.
    /// </summary>
    [JsonPropertyName("system")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? System { get; init; }

    /// <summary>
    /// 0 für die Nachbearbeitung — sie ist eine analytische Aufgabe. Ohne Angabe läge der Wert
    /// bei 1,0, dem Maximum, und jedes Diktat wäre ein neuer Würfelwurf.
    /// Achtung: Opus 4.7 und neuer lehnen das Feld mit HTTP 400 ab; ClaudeService wiederholt
    /// den Aufruf dann ohne Temperatur.
    /// </summary>
    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; init; }
}
