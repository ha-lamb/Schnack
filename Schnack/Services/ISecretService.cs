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
}
