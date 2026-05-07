using Schnack.Models;

namespace Schnack.Services;

public interface IPostProcessingService
{
    Task<ClaudeProcessResult> ProcessAsync(string transcript, DictationMode mode, CancellationToken ct = default);
}
