using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NHotkey;
using NHotkey.Wpf;

namespace Schnack.Services;

public sealed class HotkeyService : IHotkeyService
{
    private const string HotkeyId = "SchnackToggle";
    private readonly ILogger<HotkeyService> _logger;
    private bool _registered;
    private bool _disposed;

    public HotkeyService(ILogger<HotkeyService> logger)
    {
        _logger = logger;
    }

    public void Register(string hotkey, Action handler)
    {
        var (key, modifiers) = ParseHotkey(hotkey);

        try
        {
            HotkeyManager.Current.Remove(HotkeyId);
        }
        catch
        {
            // noch nicht registriert
        }

        HotkeyManager.Current.AddOrReplace(HotkeyId, key, modifiers,
            (sender, e) => handler());

        _registered = true;
        _logger.LogInformation("Hotkey registered: {Hotkey}", hotkey);
    }

    public void Unregister()
    {
        if (!_registered) return;
        try
        {
            HotkeyManager.Current.Remove(HotkeyId);
            _registered = false;
            _logger.LogInformation("Hotkey unregistered");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetType().Name + ": Failed to unregister hotkey");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }

    private static (Key key, ModifierKeys modifiers) ParseHotkey(string hotkey)
    {
        var parts = hotkey.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            throw new ArgumentException($"Invalid hotkey format: {hotkey}");

        var modifiers = ModifierKeys.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            modifiers |= parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "alt" => ModifierKeys.Alt,
                "shift" => ModifierKeys.Shift,
                "win" or "windows" => ModifierKeys.Windows,
                _ => throw new ArgumentException($"Unknown modifier: {parts[i]}")
            };
        }

        if (!Enum.TryParse<Key>(parts[^1], true, out var key))
            throw new ArgumentException($"Unknown key: {parts[^1]}");

        return (key, modifiers);
    }
}
