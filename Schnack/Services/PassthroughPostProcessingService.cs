using Microsoft.Extensions.Logging;
using Schnack.Models;

namespace Schnack.Services;

/// <summary>
/// Nachbearbeitung ohne Sprachmodell: reicht das Transkript unverändert durch.
/// Wird aufgelöst, wenn „Text glätten" aus ist oder der lokale Stack läuft — dadurch braucht
/// die Pipeline keinen Sonderfall.
/// Bewusst ohne jede Textkorrektur: was hier passierte, wäre stille Nachbearbeitung und
/// widerspräche genau der Erwartung, den Rohtext zu bekommen.
/// </summary>
public sealed class PassthroughPostProcessingService : IPostProcessingService
{
    private readonly ILogger<PassthroughPostProcessingService> _logger;

    public PassthroughPostProcessingService(ILogger<PassthroughPostProcessingService> logger)
    {
        _logger = logger;
    }

    public Task<ClaudeProcessResult> ProcessAsync(
        string transcript, DictationMode mode, CancellationToken ct = default)
    {
        _logger.LogInformation("Post-processing skipped (no language model in this configuration)");
        return Task.FromResult(new ClaudeProcessResult(transcript, false));
    }
}
