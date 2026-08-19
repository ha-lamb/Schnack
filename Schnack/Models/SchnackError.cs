namespace Schnack.Models;

/// <summary>
/// Fehlerursachen, die dem Nutzer eine spezifische Meldung wert sind.
/// Ersetzt das frühere Matching auf Exception-Texte — das brach, sobald die Texte übersetzt wurden.
/// </summary>
public enum SchnackError
{
    MicrophoneStopTimeout,
    NoTargetWindow,
    MissingOpenAiKey,
    MissingAnthropicKey,
    WhisperModelMissing,
    ApiKeyInvalid,
    RateLimit,
    EmptyApiResponse
}

/// <summary>
/// Anwendungsfehler mit maschinenlesbarer Ursache. Die Message ist englisch und rein
/// für Diagnose/Logs gedacht; die nutzersichtbare Meldung leitet der Orchestrator aus <see cref="Code"/> ab.
/// </summary>
public sealed class SchnackException : Exception
{
    public SchnackError Code { get; }

    public SchnackException(SchnackError code, string message) : base(message) => Code = code;
}
