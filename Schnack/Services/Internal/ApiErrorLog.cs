using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Schnack.Services.Internal;

internal static class ApiErrorLog
{
    /// <summary>
    /// Loggt einen API-Fehler sanitisiert: nur error.type, error.code und Statuscode.
    /// Niemals error.message (kann User-Daten enthalten) — siehe Logging-Verbote in CLAUDE.md.
    /// </summary>
    internal static async Task LogSanitizedAsync(
        HttpResponseMessage response, ILogger logger, string serviceLabel, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var err = doc.RootElement.GetProperty("error");
            var type = err.TryGetProperty("type", out var t) ? t.GetString() : null;
            var code = err.TryGetProperty("code", out var c) ? c.GetString() : null;
            logger.LogWarning("{Service} API error type={Type} code={Code} status={Status}",
                serviceLabel, type, code, (int)response.StatusCode);
        }
        catch
        {
            logger.LogWarning("{Service} API error status={Status}", serviceLabel, (int)response.StatusCode);
        }
    }
}
