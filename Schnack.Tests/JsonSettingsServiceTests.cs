using Microsoft.Extensions.Logging;
using Moq;
using Schnack.Models;
using Schnack.Services;

namespace Schnack.Tests;

public class JsonSettingsServiceTests : IDisposable
{
    // Each test gets its own isolated temp directory
    private readonly string _tempDir;

    public JsonSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SchnackTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsDefaultSettings()
    {
        var service = new TestableJsonSettingsService(
            Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);

        await service.LoadAsync();

        Assert.Equal("de_correct", service.Settings.DefaultMode);
        Assert.Equal("gpt-4o-mini-transcribe", service.Settings.OpenAiTranscriptionModel);
        Assert.Equal("claude-haiku-4-5", service.Settings.ClaudeModel);
        Assert.Equal(4096, service.Settings.ClaudeMaxTokens);
        Assert.True(service.Settings.RestoreClipboard);
        Assert.True(service.Settings.PreferClipboardFreeInsertion);
        Assert.False(service.Settings.DebugLogging);
        Assert.Equal(2, service.Settings.SettingsSchema);
        Assert.True(service.CreatedDefaultSettingsOnLastLoad);
    }

    [Fact]
    public async Task LoadAsync_LegacyJsonWithoutSchema_MigratesSchema()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        await File.WriteAllTextAsync(path,
            """{"defaultMode":"de_correct","whisperModel":"base","claudeModel":"claude-haiku-4-5","claudeMaxTokens":4096,"hotkey":"Ctrl+Alt+Space","restoreClipboard":true,"debugLogging":false}""");

        var service = new TestableJsonSettingsService(
            Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);
        await service.LoadAsync();

        Assert.Equal(2, service.Settings.SettingsSchema);
        Assert.Equal(BackendProvider.OpenAi, service.Settings.BackendProvider);

        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("settingsSchema", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_SetsCreatedDefaultFlag()
    {
        var service = new TestableJsonSettingsService(
            Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);

        await service.LoadAsync();
        Assert.True(service.CreatedDefaultSettingsOnLastLoad);

        await service.LoadAsync();
        Assert.False(service.CreatedDefaultSettingsOnLastLoad);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_CreatesFileWithDefaults()
    {
        var service = new TestableJsonSettingsService(
            Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);

        await service.LoadAsync();

        var settingsFile = Path.Combine(_tempDir, "settings.json");
        Assert.True(File.Exists(settingsFile));
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsSettings()
    {
        var service = new TestableJsonSettingsService(
            Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);

        await service.LoadAsync(); // creates defaults

        var updated = service.Settings with
        {
            DefaultMode = "de_to_en",
            OpenAiTranscriptionModel = "whisper-1",
            ClaudeModel = "claude-sonnet-4-6",
            ClaudeMaxTokens = 2048,
            MicrophoneDeviceId = 0,
            DebugLogging = true,
            RestoreClipboard = false,
            PreferClipboardFreeInsertion = false
        };
        service.UpdateSettings(updated);
        await service.SaveAsync();

        // Fresh service load from same directory
        var service2 = new TestableJsonSettingsService(
            Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);
        await service2.LoadAsync();

        Assert.Equal("de_to_en", service2.Settings.DefaultMode);
        Assert.Equal("whisper-1", service2.Settings.OpenAiTranscriptionModel);
        Assert.Equal("claude-sonnet-4-6", service2.Settings.ClaudeModel);
        Assert.Equal(2048, service2.Settings.ClaudeMaxTokens);
        Assert.Equal(0, service2.Settings.MicrophoneDeviceId);
        Assert.True(service2.Settings.DebugLogging);
        Assert.False(service2.Settings.RestoreClipboard);
        Assert.False(service2.Settings.PreferClipboardFreeInsertion);
    }

    [Fact]
    public async Task UpdateSettings_ChangesSettingsProperty()
    {
        var service = new TestableJsonSettingsService(
            Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);
        await service.LoadAsync();

        var newSettings = service.Settings with { OpenAiTranscriptionModel = "gpt-4o-transcribe" };
        service.UpdateSettings(newSettings);

        Assert.Equal("gpt-4o-transcribe", service.Settings.OpenAiTranscriptionModel);
    }
}

// Testable subclass that overrides the settings path to use our temp dir
internal class TestableJsonSettingsService : JsonSettingsService
{
    private readonly string _testSettingsPath;

    public TestableJsonSettingsService(
        ILogger<JsonSettingsService> logger, string tempDir)
        : base(logger)
    {
        _testSettingsPath = Path.Combine(tempDir, "settings.json");
    }

    protected override string SettingsFilePath => _testSettingsPath;
}
