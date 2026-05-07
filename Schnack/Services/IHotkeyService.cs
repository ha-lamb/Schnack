namespace Schnack.Services;

public interface IHotkeyService : IDisposable
{
    void Register(string hotkey, Action handler);
    void Unregister();
}
