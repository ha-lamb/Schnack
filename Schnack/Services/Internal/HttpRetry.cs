using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Schnack.Services.Internal;

internal static class HttpRetry
{
    private const int DefaultMaxAttempts = 3;

    /// <summary>
    /// Führt <paramref name="sendAction"/> mit exponentiellem Backoff aus (250 / 500 / 1000 ms).
    /// Wirft <see cref="HttpRequestException"/> wenn alle Versuche ausgeschöpft sind.
    /// </summary>
    internal static async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAction,
        ILogger logger,
        string serviceLabel,
        CancellationToken ct,
        int maxAttempts = DefaultMaxAttempts)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            HttpResponseMessage response;
            try
            {
                response = await sendAction(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < maxAttempts)
            {
                logger.LogWarning("{Service} request timed out (attempt {Attempt})", serviceLabel, attempt);
                await DelayAsync(attempt, ct);
                continue;
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && IsTransient(ex))
            {
                logger.LogWarning("{Service} transient network error (attempt {Attempt}): {Type}",
                    serviceLabel, attempt, ex.GetType().Name);
                await DelayAsync(attempt, ct);
                continue;
            }

            if (ShouldRetry(response.StatusCode) && attempt < maxAttempts)
            {
                logger.LogWarning("{Service} HTTP {Status} (attempt {Attempt}), retrying",
                    serviceLabel, (int)response.StatusCode, attempt);
                response.Dispose();
                await DelayAsync(attempt, ct);
                continue;
            }

            return response;
        }

        throw new HttpRequestException($"{serviceLabel}: retries exhausted");
    }

    private static bool ShouldRetry(HttpStatusCode code) =>
        code == HttpStatusCode.RequestTimeout ||
        code == HttpStatusCode.InternalServerError ||
        code == HttpStatusCode.BadGateway ||
        code == HttpStatusCode.ServiceUnavailable ||
        code == HttpStatusCode.GatewayTimeout;

    private static bool IsTransient(HttpRequestException ex) =>
        ex.StatusCode is HttpStatusCode.RequestTimeout or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;

    private static Task DelayAsync(int attempt, CancellationToken ct) =>
        Task.Delay(250 * (1 << (attempt - 1)), ct); // 250, 500, 1000
}
