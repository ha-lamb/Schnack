using Schnack.Models;
using Schnack.Services.Internal;

namespace Schnack.Tests;

public class SmoothingPolicyTests
{
    private static AppSettings Settings(bool textSmoothing, AiService service = AiService.OpenAi) =>
        new() { TextSmoothing = textSmoothing, AiService = service };

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]   // Schalter an, aber kein Schlüssel
    [InlineData(false, true, false)]   // Schlüssel da, aber bewusst abgeschaltet
    [InlineData(false, false, false)]
    public void IsActive_NeedsBothTheSwitchAndAKey(bool switchOn, bool keyAvailable, bool expected)
    {
        Assert.Equal(expected, SmoothingPolicy.IsActive(Settings(switchOn), keyAvailable));
    }

    [Theory]
    [InlineData(AiService.OpenAi)]
    [InlineData(AiService.Claude)]
    public void PostProcessingKey_WithSmoothing_ResolvesTheChosenService(AiService service)
    {
        Assert.Equal(service.ToString(),
            SmoothingPolicy.PostProcessingKey(Settings(true, service), keyAvailable: true));
    }

    [Theory]
    [InlineData(AiService.OpenAi)]
    [InlineData(AiService.Claude)]
    public void PostProcessingKey_WithoutKey_ResolvesThePassthrough(AiService service)
    {
        Assert.Equal(SmoothingPolicy.Passthrough,
            SmoothingPolicy.PostProcessingKey(Settings(true, service), keyAvailable: false));
    }

    [Fact]
    public void PostProcessingKey_SwitchedOff_ResolvesThePassthrough()
    {
        Assert.Equal(SmoothingPolicy.Passthrough,
            SmoothingPolicy.PostProcessingKey(Settings(false), keyAvailable: true));
    }

    [Fact]
    public void Passthrough_IsNotAServiceName()
    {
        // Der Passthrough-Schlüssel darf mit keinem Dienstnamen kollidieren, sonst löste die
        // Keyed-DI bei abgeschalteter Glättung einen echten Cloud-Dienst auf.
        Assert.NotEqual(SmoothingPolicy.Passthrough, AiService.OpenAi.ToString());
        Assert.NotEqual(SmoothingPolicy.Passthrough, AiService.Claude.ToString());
    }
}
