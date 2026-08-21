using Schnack.Models;

namespace Schnack.Services;

public interface ISecretService
{
    string? GetApiKey();
    void SaveApiKey(string apiKey);
    bool HasApiKey();

    /// <summary>OpenAI-Key für Speech-to-Text (Env <c>OPENAI_API_KEY</c> oder DPAPI-Datei).</summary>
    string? GetOpenAiApiKey();
    void SaveOpenAiApiKey(string apiKey);
    bool HasOpenAiApiKey();

    /// <summary>
    /// Ist für diesen Dienst ein Schlüssel hinterlegt? Eine Methode statt drei Aufrufer, die
    /// selbst zwischen den beiden Anbietern unterscheiden — die Frage stellen Pipeline,
    /// Tray-Menü und Einstellungsdialog gleichermaßen.
    /// </summary>
    bool HasKeyFor(AiService service);
}
