using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Schnack.Models;
using Schnack.Services;

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

    private DictationOrchestrator CreateSut()
    {
        _settings.Setup(s => s.Settings).Returns(new AppSettings()); // Default: BackendProvider.OpenAi

        var services = new ServiceCollection();
        services.AddKeyedSingleton(BackendProvider.OpenAi.ToString(), _transcription.Object);
        services.AddKeyedSingleton(BackendProvider.OpenAi.ToString(), _postProcessing.Object);
        var provider = services.BuildServiceProvider();

        return new DictationOrchestrator(
            provider,
            _settings.Object,
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
