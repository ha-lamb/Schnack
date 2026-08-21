using Schnack.Models;

namespace Schnack.Services.Internal;

/// <summary>
/// Vorbelegung beim allerersten Start. Die Spracherkennung steht ohnehin fest (lokales
/// Whisper) — zu entscheiden ist nur, ob nachbearbeitet werden kann und von wem.
/// </summary>
internal static class FirstRunDefaults
{
    internal static (AiService Service, bool TextSmoothing) Choose(
        bool hasOpenAiKey, bool hasAnthropicKey)
    {
        if (hasOpenAiKey)
            return (AiService.OpenAi, true);

        if (hasAnthropicKey)
            return (AiService.Claude, true);

        // Ohne Schlüssel bleibt es beim Rohtext der Spracherkennung. Die Dienstwahl ist dann
        // nur eine schlafende Vorbelegung für den Fall, dass später ein Schlüssel dazukommt.
        return (AiService.OpenAi, false);
    }
}
