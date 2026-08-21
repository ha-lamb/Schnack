namespace Schnack.Services;

/// <summary>
/// Vorladen des lokalen Spracherkennungsmodells. Bewusst ein eigenes, schmales Interface statt
/// einer Erweiterung von <see cref="ITranscriptionService"/> — die Cloud-Erkennung hat nichts
/// vorzuladen und soll die Methode gar nicht erst anbieten.
/// </summary>
public interface IWhisperWarmup
{
    Task WarmUpAsync(CancellationToken ct = default);
}
