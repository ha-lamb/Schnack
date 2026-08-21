using Microsoft.Extensions.DependencyInjection;
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
}
