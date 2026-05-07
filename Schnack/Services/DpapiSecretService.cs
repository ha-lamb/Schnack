using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Schnack.Services;

public sealed class DpapiSecretService : ISecretService
{
    private static readonly string SecretsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Schnack", "secrets.dat");

    private static readonly string OpenAiSecretsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Schnack", "openai-secrets.dat");

    private readonly ILogger<DpapiSecretService> _logger;

    public DpapiSecretService(ILogger<DpapiSecretService> logger)
    {
        _logger = logger;
    }

    public string? GetApiKey()
    {
        var envKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            _logger.LogInformation("API key loaded from environment variable");
            return envKey;
        }

        if (!File.Exists(SecretsPath))
        {
            _logger.LogInformation("No API key found (no env var, no secrets.dat)");
            return null;
        }

        try
        {
            var encrypted = File.ReadAllBytes(SecretsPath);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            _logger.LogInformation("API key loaded from secrets.dat");
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetType().Name + ": Failed to read secrets.dat");
            return null;
        }
    }

    public bool HasApiKey() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))
        || File.Exists(SecretsPath);

    public void SaveApiKey(string apiKey)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SecretsPath)!);
            var bytes = Encoding.UTF8.GetBytes(apiKey);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(SecretsPath, encrypted);
            _logger.LogInformation("API key saved to secrets.dat");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetType().Name + ": Failed to save API key");
            throw;
        }
    }

    public string? GetOpenAiApiKey()
    {
        var envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            _logger.LogInformation("OpenAI API key loaded from environment variable");
            return envKey;
        }

        if (!File.Exists(OpenAiSecretsPath))
        {
            _logger.LogInformation("No OpenAI API key found (no env var, no openai-secrets.dat)");
            return null;
        }

        try
        {
            var encrypted = File.ReadAllBytes(OpenAiSecretsPath);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            _logger.LogInformation("OpenAI API key loaded from openai-secrets.dat");
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetType().Name + ": Failed to read openai-secrets.dat");
            return null;
        }
    }

    public bool HasOpenAiApiKey() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
        || File.Exists(OpenAiSecretsPath);

    public void SaveOpenAiApiKey(string apiKey)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OpenAiSecretsPath)!);
            var bytes = Encoding.UTF8.GetBytes(apiKey);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(OpenAiSecretsPath, encrypted);
            _logger.LogInformation("OpenAI API key saved to openai-secrets.dat");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetType().Name + ": Failed to save OpenAI API key");
            throw;
        }
    }
}
