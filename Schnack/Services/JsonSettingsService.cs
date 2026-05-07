using System.Text.Json;
using Microsoft.Extensions.Logging;
using Schnack.Models;

namespace Schnack.Services;

// Not sealed: test subclass overrides SettingsFilePath to use a temp file
public class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<JsonSettingsService> _logger;

    public AppSettings Settings { get; private set; } = new();

    public bool CreatedDefaultSettingsOnLastLoad { get; private set; }

    public void UpdateSettings(AppSettings settings) => Settings = settings;

    // Virtual so tests can override the path
    protected virtual string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Schnack", "settings.json");

    public JsonSettingsService(ILogger<JsonSettingsService> logger)
    {
        _logger = logger;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        CreatedDefaultSettingsOnLastLoad = false;
        await _lock.WaitAsync(ct);
        try
        {
            var path = SettingsFilePath;
            if (!File.Exists(path))
            {
                CreatedDefaultSettingsOnLastLoad = true;
                _logger.LogInformation("Settings file not found, creating defaults at {Path}", path);
                Settings = new AppSettings();
                await WriteAsync(ct);
                return;
            }

            var json = await File.ReadAllTextAsync(path, ct);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            var schema = !JsonHasSettingsSchemaProperty(json) ? 0 : loaded.SettingsSchema;
            if (schema < 1)
            {
                loaded = loaded with { SettingsSchema = 1 };
                _logger.LogInformation("Settings migrated to schema 1");
            }
            if (schema < 2)
            {
                // Schema 1→2: BackendProvider eingeführt; Bestandsnutzer hatten OpenAI-STT → openai Default
                loaded = loaded with { SettingsSchema = 2, BackendProvider = Models.BackendProvider.OpenAi };
                Settings = loaded;
                await WriteAsync(ct);
                _logger.LogInformation("Settings migrated to schema 2");
            }
            else
                Settings = loaded;

            _logger.LogInformation("Settings loaded");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetType().Name + ": Failed to load settings, using defaults");
            Settings = new AppSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await WriteAsync(ct);
            _logger.LogInformation("Settings saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetType().Name + ": Failed to save settings");
        }
        finally
        {
            _lock.Release();
        }
    }

    private static bool JsonHasSettingsSchemaProperty(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("settingsSchema", out _);
        }
        catch
        {
            return false;
        }
    }

    // Caller must hold _lock
    private async Task WriteAsync(CancellationToken ct)
    {
        var path = SettingsFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }
}
