using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Schnack.Interop;

namespace Schnack.Services;

public sealed class TextInsertionService : ITextInsertionService
{
    private const int ForegroundSettleDelayMs = 120;
    private const int ClipboardRestoreDelayMs = 500;
    private const int MaxBackupChars = 100_000;

    private readonly ISettingsService _settings;
    private readonly ILogger<TextInsertionService> _logger;

    public TextInsertionService(ISettingsService settings, ILogger<TextInsertionService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task InsertTextAsync(nint targetHwnd, string text, CancellationToken ct = default)
    {
        if (targetHwnd == 0)
        {
            _logger.LogWarning("No target window cached — placing text in clipboard for manual paste");
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try { Clipboard.SetText(text, TextDataFormat.UnicodeText); }
                catch (Exception ex) { _logger.LogWarning(ex.GetType().Name + ": Clipboard set failed (no target)"); }
            }, DispatcherPriority.Normal);
            throw new InvalidOperationException("Kein Zielfenster erkannt – Text liegt in der Zwischenablage.");
        }

        var op = Application.Current.Dispatcher.InvokeAsync(
            () => InsertOnUiThreadAsync(targetHwnd, text, ct),
            DispatcherPriority.Normal);
        await op.Task.Unwrap().ConfigureAwait(false);
        _logger.LogDebug("Text insertion UI work completed");
    }

    private async Task InsertOnUiThreadAsync(nint targetHwnd, string text, CancellationToken ct)
    {
        if (_settings.Settings.PreferClipboardFreeInsertion)
        {
            bool focused = await TryFocusWindowAsync(targetHwnd, ct);
            if (!focused)
            {
                // Unicode-SendInput ohne gültigen Fokus landet im Leeren → Fallback auf Clipboard+Strg+V
                _logger.LogWarning("SetForegroundWindow failed for Unicode path — falling back to clipboard insertion");
                await InsertViaClipboardAsync(targetHwnd, text, ct);
                return;
            }
            SendUnicodeRunes(text, ct);
            return;
        }

        await InsertViaClipboardAsync(targetHwnd, text, ct);
    }

    // Setzt den Fokus auf targetHwnd via AttachThreadInput-Trick.
    // Gibt true zurück, wenn das Fenster danach im Vordergrund ist.
    private async Task<bool> TryFocusWindowAsync(nint targetHwnd, CancellationToken ct)
    {
        uint targetThreadId = Win32.GetWindowThreadProcessId(targetHwnd, out _);
        uint currentThreadId = Win32.GetCurrentThreadId();
        bool attached = false;

        if (targetThreadId != 0 && targetThreadId != currentThreadId)
            attached = Win32.AttachThreadInput(currentThreadId, targetThreadId, true);

        try
        {
            bool fgResult = Win32.SetForegroundWindow(targetHwnd);
            _logger.LogDebug("TryFocusWindow hwnd={Hwnd} SetForegroundWindow={Result}", targetHwnd, fgResult);

            await Task.Delay(ForegroundSettleDelayMs, ct);

            nint fgAfter = Win32.GetForegroundWindow();
            bool match = fgAfter == targetHwnd;
            _logger.LogDebug("TryFocusWindow fgMatchAfterDelay={Match}", match);
            return match;
        }
        finally
        {
            if (attached)
                Win32.AttachThreadInput(currentThreadId, targetThreadId, false);
        }
    }

    private async Task InsertViaClipboardAsync(nint targetHwnd, string text, CancellationToken ct)
    {
        string? previousClipboard = null;
        if (_settings.Settings.RestoreClipboard && Clipboard.ContainsText())
        {
            var existing = Clipboard.GetText();
            if (existing.Length <= MaxBackupChars)
                previousClipboard = existing;
            else
                _logger.LogDebug("Clipboard backup skipped, content too large ({Chars} chars)", existing.Length);
        }

        try
        {
            Clipboard.Clear();
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex.GetType().Name + ": Clipboard set failed");
            throw;
        }

        uint targetThreadId = Win32.GetWindowThreadProcessId(targetHwnd, out _);
        uint currentThreadId = Win32.GetCurrentThreadId();
        bool attached = false;

        if (targetThreadId != 0 && targetThreadId != currentThreadId)
            attached = Win32.AttachThreadInput(currentThreadId, targetThreadId, true);

        try
        {
            bool fgResult = Win32.SetForegroundWindow(targetHwnd);

            if (!fgResult)
                _logger.LogWarning("SetForegroundWindow failed — text is in clipboard, please paste manually (Ctrl+V)");

            await Task.Delay(ForegroundSettleDelayMs, ct);

            nint fgAfter = Win32.GetForegroundWindow();
            _logger.LogDebug("Paste path: SendInput Ctrl+V, fgMatchAfterDelay {FgMatch}", fgAfter == targetHwnd);

            SendCtrlVScanCodes();
        }
        finally
        {
            if (attached)
                Win32.AttachThreadInput(currentThreadId, targetThreadId, false);
        }

        if (_settings.Settings.RestoreClipboard && previousClipboard != null)
        {
            await Task.Delay(ClipboardRestoreDelayMs, ct);
            try { Clipboard.SetText(previousClipboard); }
            catch (Exception ex) { _logger.LogWarning(ex.GetType().Name + ": Clipboard restore failed"); }
        }
    }

    // Sendet Unicode-Runen per SendInput (Fenster muss vorher fokussiert sein).
    private static void SendUnicodeRunes(string text, CancellationToken ct)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        Span<char> utf16Scratch = stackalloc char[2];
        foreach (var rune in normalized.EnumerateRunes())
        {
            ct.ThrowIfCancellationRequested();
            if (rune.Value == '\n')
            {
                SendUnicodeCodeUnit('\r');
                SendUnicodeCodeUnit('\n');
            }
            else
            {
                int len = rune.EncodeToUtf16(utf16Scratch);
                for (int i = 0; i < len; i++)
                    SendUnicodeCodeUnit(utf16Scratch[i]);
            }
        }
    }

    private static void SendUnicodeCodeUnit(char ch)
    {
        ushort u = ch;
        SendSingleUnicodeKey(u, keyUp: false);
        SendSingleUnicodeKey(u, keyUp: true);
    }

    private static void SendSingleUnicodeKey(ushort code, bool keyUp)
    {
        uint flags = Win32.KEYEVENTF_UNICODE;
        if (keyUp)
            flags |= Win32.KEYEVENTF_KEYUP;

        var input = new Win32.INPUT
        {
            type = Win32.INPUT_KEYBOARD,
            u = new Win32.INPUTUNION
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = code,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = 0
                }
            }
        };

        uint sent = Win32.SendInput(1, [input], Marshal.SizeOf<Win32.INPUT>());
        if (sent != 1)
            throw new ExternalException("SendInput Unicode failed: " + Marshal.GetLastWin32Error());
    }

    private static void SendCtrlVScanCodes()
    {
        ushort scCtrl = (ushort)Win32.MapVirtualKey(Win32.VK_CONTROL, Win32.MAPVK_VK_TO_VSC);
        ushort scV = (ushort)Win32.MapVirtualKey(Win32.VK_V, Win32.MAPVK_VK_TO_VSC);

        var inputs = new Win32.INPUT[]
        {
            MakeScancode(scCtrl, extended: false, keyUp: false),
            MakeScancode(scV, extended: false, keyUp: false),
            MakeScancode(scV, extended: false, keyUp: true),
            MakeScancode(scCtrl, extended: false, keyUp: true),
        };

        uint sent = Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
        if (sent != (uint)inputs.Length)
            throw new ExternalException("SendInput Ctrl+V failed: " + Marshal.GetLastWin32Error());
    }

    private static Win32.INPUT MakeScancode(ushort scan, bool extended, bool keyUp)
    {
        uint flags = Win32.KEYEVENTF_SCANCODE;
        if (keyUp)
            flags |= Win32.KEYEVENTF_KEYUP;
        if (extended)
            flags |= Win32.KEYEVENTF_EXTENDEDKEY;

        return new Win32.INPUT
        {
            type = Win32.INPUT_KEYBOARD,
            u = new Win32.INPUTUNION
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = 0
                }
            }
        };
    }
}
