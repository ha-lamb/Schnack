using Schnack.Models;
using Schnack.Services.Internal;

namespace Schnack.Tests;

public class FirstRunDefaultsTests
{
    [Fact]
    public void WithoutAnyKey_TurnsSmoothingOff()
    {
        var (_, smoothing) = FirstRunDefaults.Choose(hasOpenAiKey: false, hasAnthropicKey: false);

        Assert.False(smoothing);
    }

    [Fact]
    public void WithOpenAiKey_ChoosesOpenAiWithSmoothing()
    {
        var (service, smoothing) = FirstRunDefaults.Choose(hasOpenAiKey: true, hasAnthropicKey: false);

        Assert.Equal(AiService.OpenAi, service);
        Assert.True(smoothing);
    }

    [Fact]
    public void WithOnlyAnthropicKey_ChoosesClaudeWithSmoothing()
    {
        var (service, smoothing) = FirstRunDefaults.Choose(hasOpenAiKey: false, hasAnthropicKey: true);

        Assert.Equal(AiService.Claude, service);
        Assert.True(smoothing);
    }

    [Fact]
    public void WithBothKeys_PrefersOpenAi()
    {
        var (service, _) = FirstRunDefaults.Choose(hasOpenAiKey: true, hasAnthropicKey: true);

        Assert.Equal(AiService.OpenAi, service);
    }
}
