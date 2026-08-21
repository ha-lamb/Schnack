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

        Assert.Equal("correct", service.Settings.DefaultMode);
        Assert.Equal("claude-haiku-4-5", service.Settings.ClaudeModel);
        Assert.Equal(4096, service.Settings.ClaudeMaxTokens);
        Assert.True(service.Settings.RestoreClipboard);
        Assert.True(service.Settings.PreferClipboardFreeInsertion);
        Assert.False(service.Settings.DebugLogging);
        Assert.Equal(4, service.Settings.SettingsSchema);
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

        Assert.Equal(4, service.Settings.SettingsSchema);
        Assert.Equal(AiService.OpenAi, service.Settings.AiService);

        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("settingsSchema", json, StringComparison.Ordinal);
    }

    // Schema 2→3: Sprachen kommen dazu, Modi verlieren ihr Sprachpräfix.
    [Theory]
    [InlineData("de_correct", "correct")]
    [InlineData("de_to_en", "translate")]
    public async Task LoadAsync_Schema2_MigratesModeAndLanguages(string oldMode, string expectedMode)
    {
        var path = Path.Combine(_tempDir, "settings.json");
        await File.WriteAllTextAsync(path,
            $$"""{"settingsSchema":2,"backendProvider":"claude","defaultMode":"{{oldMode}}","hotkey":"Ctrl+Alt+S"}""");

        var service = new TestableJsonSettingsService(
            Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);
        await service.LoadAsync();

        Assert.Equal(4, service.Settings.SettingsSchema);
        Assert.Equal(expectedMode, service.Settings.DefaultMode);
        // Bestandsnutzer bleiben auf Deutsch — ein Update darf die App nicht umstellen
        Assert.Equal(AppLanguage.De, service.Settings.UiLanguage);
        Assert.Equal(AppLanguage.De, service.Settings.DictationLanguage);
        // Der alte Backend-Wert muss bis in die Schema-4-Migration durchkommen
        Assert.Equal(AiService.Claude, service.Settings.AiService);

        // Migration muss zurückgeschrieben werden
        var reloaded = new TestableJsonSettingsService(
            Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);
        await reloaded.LoadAsync();
        Assert.Equal(expectedMode, reloaded.Settings.DefaultMode);
    }

    [Fact]
    public async Task LoadAsync_CurrentSchema_LeavesSettingsUntouched()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        await File.WriteAllTextAsync(path,
            """{"settingsSchema":4,"uiLanguage":"en","dictationLanguage":"en","defaultMode":"translate"}""");

        var service = new TestableJsonSettingsService(
            Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);
        await service.LoadAsync();

        Assert.Equal(AppLanguage.En, service.Settings.UiLanguage);
        Assert.Equal(AppLanguage.En, service.Settings.DictationLanguage);
        Assert.Equal("translate", service.Settings.DefaultMode);
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
        Assert.Equal("claude-sonnet-4-6", service2.Settings.ClaudeModel);
        Assert.Equal(2048, service2.Settings.ClaudeMaxTokens);
        Assert.Equal(0, service2.Settings.MicrophoneDeviceId);
        Assert.True(service2.Settings.DebugLogging);
        Assert.False(service2.Settings.RestoreClipboard);
        Assert.False(service2.Settings.PreferClipboardFreeInsertion);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsVocabulary()
    {
        var service = new TestableJsonSettingsService(
            Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);
        await service.LoadAsync();

        service.UpdateSettings(service.Settings with { Vocabulary = ["Kubernetes", "Krzysztof"] });
        await service.SaveAsync();

        var reloaded = new TestableJsonSettingsService(
            Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);
        await reloaded.LoadAsync();

        Assert.Equal(["Kubernetes", "Krzysztof"], reloaded.Settings.Vocabulary);
    }

    [Fact]
    public async Task UpdateSettings_ChangesSettingsProperty()
    {
        var service = new TestableJsonSettingsService(
            Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);
        await service.LoadAsync();

        var newSettings = service.Settings with { OpenAiChatModel = "gpt-4o" };
        service.UpdateSettings(newSettings);

        Assert.Equal("gpt-4o", service.Settings.OpenAiChatModel);
    }

    // ── Schema 4: aus der Stack-Wahl wird ein Schichtenmodell ─────────────

    private async Task<TestableJsonSettingsService> LoadFromJsonAsync(string json)
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "settings.json"), json);
        var service = new TestableJsonSettingsService(Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);
        await service.LoadAsync();
        return service;
    }

    [Theory]
    [InlineData("openai", AiService.OpenAi)]
    [InlineData("claude", AiService.Claude)]
    public async Task Schema4_MapsTheOldBackendToTheAiService(string legacy, AiService expected)
    {
        var service = await LoadFromJsonAsync(
            "{\"settingsSchema\":3,\"backendProvider\":\"" + legacy + "\"}");

        Assert.Equal(expected, service.Settings.AiService);
        Assert.Equal(4, service.Settings.SettingsSchema);
    }

    [Fact]
    public async Task Schema4_KeepsSmoothingOffWhenItWasOff()
    {
        // Wer die Glättung bewusst abgeschaltet hatte, darf sie nicht durchs Update zurückbekommen —
        // das hieße ungefragt wieder Cloud-Verkehr.
        var service = await LoadFromJsonAsync(
            "{\"settingsSchema\":3,\"backendProvider\":\"openai\",\"textSmoothing\":false}");

        Assert.False(service.Settings.TextSmoothing);
        Assert.Equal(AiService.OpenAi, service.Settings.AiService);
    }

    [Fact]
    public async Task Schema4_LegacyLocalTurnsSmoothingOff()
    {
        var service = await LoadFromJsonAsync(
            "{\"settingsSchema\":3,\"backendProvider\":\"local\",\"textSmoothing\":true}");

        Assert.False(service.Settings.TextSmoothing);
    }

    [Fact]
    public async Task Schema4_LegacyLocalDoesNotWipeTheOtherSettings()
    {
        // Der Wert "local" lässt sich nicht auf AiService abbilden. Würde er trotzdem
        // deserialisiert, fiele die Datei in den catch und ALLE Einstellungen wären weg.
        var service = await LoadFromJsonAsync(
            "{\"settingsSchema\":3,\"backendProvider\":\"local\"," +
            "\"hotkey\":\"Ctrl+Alt+Space\",\"vocabulary\":[\"Kubernetes\"],\"microphoneDeviceId\":2}");

        Assert.Equal("Ctrl+Alt+Space", service.Settings.Hotkey);
        Assert.Equal(["Kubernetes"], service.Settings.Vocabulary);
        Assert.Equal(2, service.Settings.MicrophoneDeviceId);
    }

    [Fact]
    public async Task Schema4_WritesTheNewFieldNameAndDropsTheOldOne()
    {
        await LoadFromJsonAsync("{\"settingsSchema\":3,\"backendProvider\":\"claude\"}");

        var json = await File.ReadAllTextAsync(Path.Combine(_tempDir, "settings.json"));
        Assert.Contains("aiService", json, StringComparison.Ordinal);
        Assert.DoesNotContain("backendProvider", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_DefaultsToSmoothingAndPreloadOn()
    {
        var service = new TestableJsonSettingsService(Mock.Of<ILogger<JsonSettingsService>>(), _tempDir);

        await service.LoadAsync();

        Assert.True(service.Settings.TextSmoothing);
        Assert.True(service.Settings.WhisperPreload);
        Assert.False(service.Settings.WhisperUseGpu);
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
