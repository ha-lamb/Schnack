using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using Schnack.Models;
using Schnack.Services;

namespace Schnack.Tests;

public class OpenAiTranscriptionServiceTests : IDisposable
{
    private readonly string _tempWavPath;

    public OpenAiTranscriptionServiceTests()
    {
        // Minimal WAV file so File.OpenRead succeeds
        _tempWavPath = Path.GetTempFileName();
        File.WriteAllBytes(_tempWavPath, MinimalWavBytes());
    }

    public void Dispose()
    {
        if (File.Exists(_tempWavPath))
            File.Delete(_tempWavPath);
    }

    private static OpenAiTranscriptionService BuildService(
        HttpMessageHandler handler,
        string? apiKey = "test-key")
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("OpenAi")).Returns(httpClient);

        var secretService = new Mock<ISecretService>();
        secretService.Setup(s => s.GetOpenAiApiKey()).Returns(apiKey);

        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.Settings).Returns(new AppSettings());

        return new OpenAiTranscriptionService(
            factory.Object,
            secretService.Object,
            settingsService.Object,
            Mock.Of<ILogger<OpenAiTranscriptionService>>());
    }

    [Fact]
    public async Task TranscribeAsync_ValidResponse_ReturnsText()
    {
        var sut = BuildService(new FakeMessageHandler(OkResponse("Das ist ein Test.")));

        var result = await sut.TranscribeAsync(_tempWavPath);

        Assert.Equal("Das ist ein Test.", result);
    }

    [Fact]
    public async Task TranscribeAsync_EmptyText_ReturnsEmptyString()
    {
        var sut = BuildService(new FakeMessageHandler(OkResponse("  ")));

        var result = await sut.TranscribeAsync(_tempWavPath);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task TranscribeAsync_UnauthorizedResponse_ThrowsApiKeyInvalid()
    {
        var sut = BuildService(new FakeMessageHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var ex = await Assert.ThrowsAsync<SchnackException>(
            () => sut.TranscribeAsync(_tempWavPath));
        Assert.Equal(SchnackError.ApiKeyInvalid, ex.Code);
    }

    [Fact]
    public async Task TranscribeAsync_MissingApiKey_ThrowsMissingOpenAiKey()
    {
        var sut = BuildService(new FakeMessageHandler(OkResponse("{}")), apiKey: null);

        var ex = await Assert.ThrowsAsync<SchnackException>(
            () => sut.TranscribeAsync(_tempWavPath));
        Assert.Equal(SchnackError.MissingOpenAiKey, ex.Code);
    }

    [Fact]
    public async Task TranscribeAsync_ServiceUnavailableThenOk_RetriesAndReturns()
    {
        var handler = new FakeSequentialHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            OkResponse("Nach Retry"));

        var sut = BuildService(handler);
        var result = await sut.TranscribeAsync(_tempWavPath);

        Assert.Equal("Nach Retry", result);
        Assert.Equal(2, handler.SendCount);
    }

    private static HttpResponseMessage OkResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    // Minimal 44-byte WAV header (no audio data) — enough for File.OpenRead
    private static byte[] MinimalWavBytes()
    {
        // RIFF header for 0-byte PCM WAV (44 bytes total)
        var wav = new byte[44];
        var w = Encoding.ASCII;
        Array.Copy(w.GetBytes("RIFF"), 0, wav, 0, 4);
        Array.Copy(BitConverter.GetBytes(36), 0, wav, 4, 4);   // ChunkSize = 36
        Array.Copy(w.GetBytes("WAVE"), 0, wav, 8, 4);
        Array.Copy(w.GetBytes("fmt "), 0, wav, 12, 4);
        Array.Copy(BitConverter.GetBytes(16), 0, wav, 16, 4);  // Subchunk1Size = 16
        Array.Copy(BitConverter.GetBytes((short)1), 0, wav, 20, 2); // PCM
        Array.Copy(BitConverter.GetBytes((short)1), 0, wav, 22, 2); // mono
        Array.Copy(BitConverter.GetBytes(16000), 0, wav, 24, 4);    // 16 kHz
        Array.Copy(BitConverter.GetBytes(32000), 0, wav, 28, 4);    // ByteRate
        Array.Copy(BitConverter.GetBytes((short)2), 0, wav, 32, 2); // BlockAlign
        Array.Copy(BitConverter.GetBytes((short)16), 0, wav, 34, 2);// BitsPerSample
        Array.Copy(w.GetBytes("data"), 0, wav, 36, 4);
        Array.Copy(BitConverter.GetBytes(0), 0, wav, 40, 4);   // Subchunk2Size = 0
        return wav;
    }
}
