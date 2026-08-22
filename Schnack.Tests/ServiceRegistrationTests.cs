using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Schnack.Models;
using Schnack.Services;
using Schnack.Services.Internal;

namespace Schnack.Tests;

/// <summary>
/// Die Keyed-Auflösung schlägt bei einem Tippfehler im Schlüssel erst mitten im Diktat fehl.
/// Dieser Test zieht den Fehler in den Build vor: jeder Schlüssel, den SmoothingPolicy je
/// liefern kann, muss auflösbar sein.
/// </summary>
public class ServiceRegistrationTests
{
    public static TheoryData<AppSettings, bool> Configurations()
    {
        var data = new TheoryData<AppSettings, bool>();
        foreach (var service in new[] { AiService.OpenAi, AiService.Claude })
            foreach (var smoothing in new[] { true, false })
                foreach (var key in new[] { true, false })
                    data.Add(new AppSettings { AiService = service, TextSmoothing = smoothing }, key);
        return data;
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void EveryKeySmoothingPolicyCanProduce_IsRegistered(AppSettings settings, bool keyAvailable)
    {
        var services = new ServiceCollection();
        // Dieselben Schlüssel wie in App.BuildServiceProvider — bewusst mit Attrappen, damit
        // der Test keine HttpClients oder Whisper-Modelle braucht.
        services.AddKeyedSingleton(AiService.OpenAi.ToString(), Mock.Of<IPostProcessingService>());
        services.AddKeyedSingleton(AiService.Claude.ToString(), Mock.Of<IPostProcessingService>());
        services.AddKeyedSingleton(SmoothingPolicy.Passthrough, Mock.Of<IPostProcessingService>());
        using var provider = services.BuildServiceProvider();

        var key = SmoothingPolicy.PostProcessingKey(settings, keyAvailable);

        // Wirft, wenn der Schlüssel nicht registriert ist — genau der Fehler, der sonst erst
        // beim Diktieren aufträte.
        Assert.NotNull(provider.GetRequiredKeyedService<IPostProcessingService>(key));
    }

    // ── Entsorgung ─────────────────────────────────────────────────────────
    //
    // Die drei Registrierungen des Whisper-Dienstes zeigen bewusst auf DIESELBE Instanz —
    // sonst laege das Modell doppelt im Speicher. Der Container erfasst aber jede realisierte
    // Faktor-Instanz einzeln zum Entsorgen und ruft DisposeAsync entsprechend mehrfach auf.
    // Warf der zweite Aufruf, uebersprang App.CleanupAndShutdown die Mutex-Freigabe und
    // Shutdown(0): Der Prozess lief unsichtbar weiter und blockierte jeden neuen Start.

    private static ServiceCollection WhisperRegistrations()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<ISettingsService>(s => s.Settings == new AppSettings()));
        services.AddSingleton(Mock.Of<IWhisperModelDownloadService>());
        services.AddSingleton(Mock.Of<ILogger<WhisperLocalTranscriptionService>>());

        // Wortgleich mit App.BuildServiceProvider.
        services.AddSingleton<WhisperLocalTranscriptionService>();
        services.AddSingleton<ITranscriptionService>(sp => sp.GetRequiredService<WhisperLocalTranscriptionService>());
        services.AddSingleton<IWhisperWarmup>(sp => sp.GetRequiredService<WhisperLocalTranscriptionService>());
        return services;
    }

    [Fact]
    public async Task DisposingTheProvider_DoesNotThrow_DespiteForwardedRegistrations()
    {
        var provider = WhisperRegistrations().BuildServiceProvider();

        // Alle drei Sichten aufloesen — erst dann steht die Instanz mehrfach in der
        // Entsorgungsliste des Containers.
        provider.GetRequiredService<WhisperLocalTranscriptionService>();
        provider.GetRequiredService<ITranscriptionService>();
        provider.GetRequiredService<IWhisperWarmup>();

        await provider.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        await using var provider = WhisperRegistrations().BuildServiceProvider();
        var service = provider.GetRequiredService<WhisperLocalTranscriptionService>();

        await service.DisposeAsync();
        await service.DisposeAsync();
    }
}
