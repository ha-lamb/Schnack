using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Schnack.Localization;
using Schnack.Models;
using Schnack.Services;
using Schnack.Services.Internal;

namespace Schnack.Tests;

public class DictationOrchestratorTests
{
    private readonly Mock<IRecordingService> _recording = new();
    private readonly Mock<ITextInsertionService> _textInsertion = new();
    private readonly Mock<ITrayService> _tray = new();
    private readonly Mock<IFloatingRecordUi> _floating = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly Mock<ITranscriptionService> _transcription = new();
    private readonly Mock<IPostProcessingService> _postProcessing = new();

    private readonly Mock<IPostProcessingService> _passthrough = new();
    private readonly Mock<ISecretService> _secrets = new();

    private DictationOrchestrator CreateSut(AppSettings? settings = null, bool keyAvailable = true)
    {
        _settings.Setup(s => s.Settings).Returns(settings ?? new AppSettings()); // Default: OpenAi + Glättung
        _secrets.Setup(s => s.HasKeyFor(It.IsAny<AiService>())).Returns(keyAvailable);

        var services = new ServiceCollection();
        services.AddKeyedSingleton(AiService.OpenAi.ToString(), _postProcessing.Object);
        services.AddKeyedSingleton(AiService.Claude.ToString(), _postProcessing.Object);
        // Getrenntes Mock unter dem Passthrough-Schlüssel, damit sichtbar wird, welcher der
        // beiden Wege tatsächlich gegangen wurde.
        services.AddKeyedSingleton(SmoothingPolicy.Passthrough, _passthrough.Object);
        var provider = services.BuildServiceProvider();

        return new DictationOrchestrator(
            provider,
            _settings.Object,
            _secrets.Object,
            _transcription.Object,
            _recording.Object,
            _textInsertion.Object,
            _tray.Object,
            _floating.Object,
            Mock.Of<ILogger<DictationOrchestrator>>());
    }

    [Fact]
    public async Task Toggle_FromIdle_StartsRecording()
    {
        var sut = CreateSut();

        await sut.ToggleRecordingAsync(123);

        Assert.Equal(RecordingState.Recording, sut.State);
        _recording.Verify(r => r.StartRecording(It.IsAny<string>()), Times.Once);
        _tray.Verify(t => t.UpdateState(RecordingState.Recording), Times.Once);
    }

    [Fact]
    public async Task Toggle_StartRecordingThrows_ResetsToIdleWithBalloon()
    {
        _recording.Setup(r => r.StartRecording(It.IsAny<string>()))
            .Throws(new InvalidOperationException("no mic"));
        var sut = CreateSut();

        await sut.ToggleRecordingAsync(123);

        Assert.Equal(RecordingState.Idle, sut.State);
        _tray.Verify(t => t.ShowBalloonTip("Mikrofon-Fehler", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Toggle_StopWithEmptyTranscript_ShowsHintAndReturnsToIdle()
    {
        _recording.Setup(r => r.StopRecording()).Returns(@"C:\nonexistent\rec.wav");
        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("   ");
        var sut = CreateSut();

        await sut.ToggleRecordingAsync(123);
        await sut.ToggleRecordingAsync(123);

        Assert.Equal(RecordingState.Idle, sut.State);
        _tray.Verify(t => t.ShowBalloonTip("Schnack", "Keine Sprache erkannt."), Times.Once);
        _textInsertion.Verify(t => t.InsertTextAsync(It.IsAny<nint>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Toggle_FullPipeline_InsertsProcessedTextIntoCachedHwnd()
    {
        _recording.Setup(r => r.StopRecording()).Returns(@"C:\nonexistent\rec.wav");
        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hallo welt");
        _postProcessing.Setup(p => p.ProcessAsync("hallo welt", DictationMode.Correct, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaudeProcessResult("Hallo Welt.", false));
        var sut = CreateSut();

        await sut.ToggleRecordingAsync(123);
        await sut.ToggleRecordingAsync(123);

        Assert.Equal(RecordingState.Idle, sut.State);
        _textInsertion.Verify(t => t.InsertTextAsync((nint)123, "Hallo Welt.", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Pipeline_WithoutSmoothing_UsesThePassthroughAndNotTheAiService()
    {
        var settings = new AppSettings { TextSmoothing = false };
        _recording.Setup(r => r.StopRecording()).Returns("nonexistent-rec.wav");
        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("roher text ohne glaettung");
        _passthrough.Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<DictationMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string t, DictationMode _, CancellationToken _) => new ClaudeProcessResult(t, false));
        var sut = CreateSut(settings);

        await sut.ToggleRecordingAsync(123);
        await sut.ToggleRecordingAsync(123);

        _textInsertion.Verify(t => t.InsertTextAsync((nint)123, "roher text ohne glaettung", It.IsAny<CancellationToken>()), Times.Once);
        _postProcessing.Verify(
            p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<DictationMode>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Pipeline_SmoothingWantedButNoKey_FallsBackToPassthroughAndWarnsOnce()
    {
        // Der Nutzer will glätten, aber es liegt kein Schlüssel vor: Rohtext plus genau ein Hinweis.
        var settings = new AppSettings { TextSmoothing = true };
        _recording.Setup(r => r.StopRecording()).Returns("nonexistent-rec.wav");
        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("rohtext ohne schluessel");
        _passthrough.Setup(p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<DictationMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string t, DictationMode _, CancellationToken _) => new ClaudeProcessResult(t, false));
        var sut = CreateSut(settings, keyAvailable: false);

        await sut.ToggleRecordingAsync(123);
        await sut.ToggleRecordingAsync(123);
        // Zweiter Lauf: der Hinweis darf sich nicht wiederholen.
        await sut.ToggleRecordingAsync(456);
        await sut.ToggleRecordingAsync(456);

        _postProcessing.Verify(
            p => p.ProcessAsync(It.IsAny<string>(), It.IsAny<DictationMode>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _tray.Verify(t => t.ShowBalloonTip(It.IsAny<string>(), Strings.Balloon_NoSmoothingWithoutKey), Times.Once);
    }

    // Diese Zuordnung lief früher über deutsche Exception-Texte und brach still bei Übersetzung.
    [Theory]
    [InlineData(SchnackError.MicrophoneStopTimeout, "Mikrofon antwortet nicht")]
    [InlineData(SchnackError.NoTargetWindow, "Kein Zielfenster")]
    [InlineData(SchnackError.MissingOpenAiKey, "OpenAI API-Key fehlt")]
    [InlineData(SchnackError.MissingAnthropicKey, "Anthropic API-Key fehlt")]
    [InlineData(SchnackError.WhisperModelMissing, "Whisper-Modell fehlt")]
    [InlineData(SchnackError.ApiKeyInvalid, "API-Key ungültig")]
    [InlineData(SchnackError.RateLimit, "Rate Limit")]
    public async Task Pipeline_SchnackException_ShowsSpecificBalloon(SchnackError code, string expectedTitle)
    {
        _recording.Setup(r => r.StopRecording()).Returns(@"C:\nonexistent\rec.wav");
        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SchnackException(code, "test"));
        var sut = CreateSut();

        await sut.ToggleRecordingAsync(123);
        await sut.ToggleRecordingAsync(123);

        _tray.Verify(t => t.ShowBalloonTip(expectedTitle, It.IsAny<string>()), Times.Once);
        _tray.Verify(t => t.ShowBalloonTip("Fehler", It.IsAny<string>()), Times.Never);
        Assert.Equal(RecordingState.Idle, sut.State);
    }

    [Fact]
    public async Task Pipeline_UnknownException_ShowsGenericBalloon()
    {
        _recording.Setup(r => r.StopRecording()).Returns(@"C:\nonexistent\rec.wav");
        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("something else"));
        var sut = CreateSut();

        await sut.ToggleRecordingAsync(123);
        await sut.ToggleRecordingAsync(123);

        _tray.Verify(t => t.ShowBalloonTip("Fehler", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Toggle_WhileProcessing_IsIgnored()
    {
        var blockTranscription = new TaskCompletionSource<string>();
        _recording.Setup(r => r.StopRecording()).Returns(@"C:\nonexistent\rec.wav");
        _transcription.Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(blockTranscription.Task);
        var sut = CreateSut();

        await sut.ToggleRecordingAsync(123);
        // Übergang Recording→Processing passiert synchron im Toggle, Pipeline hängt in der Transkription
        var pipelineTask = sut.ToggleRecordingAsync(123);
        Assert.Equal(RecordingState.Processing, sut.State);

        await sut.ToggleRecordingAsync(123); // muss ignoriert werden

        _recording.Verify(r => r.StartRecording(It.IsAny<string>()), Times.Once);

        blockTranscription.SetResult("");
        await pipelineTask;
        Assert.Equal(RecordingState.Idle, sut.State);
    }
}
