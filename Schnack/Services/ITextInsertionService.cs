namespace Schnack.Services;

public interface ITextInsertionService
{
    Task InsertTextAsync(nint targetHwnd, string text, CancellationToken ct = default);
}
