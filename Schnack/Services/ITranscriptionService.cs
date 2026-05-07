namespace Schnack.Services;

public interface ITranscriptionService : IAsyncDisposable
{
    Task<string> TranscribeAsync(string wavFilePath, CancellationToken ct = default);
}
