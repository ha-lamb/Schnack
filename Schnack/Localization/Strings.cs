using System.Globalization;
using System.Resources;

namespace Schnack.Localization;

/// <summary>
/// Typsicherer Zugriff auf Strings.resx (Deutsch) / Strings.en.resx (Englisch).
/// Die aktive Sprache kommt aus <see cref="CultureInfo.CurrentUICulture"/> — gesetzt vom LocalizationService.
/// Handgeschrieben statt generiert, weil die MSBuild-Generierung mit der WPF-Markup-Kompilierung kollidiert.
/// Neuer Text heißt: Eintrag in beide .resx UND eine Zeile hier (der Vollständigkeitstest prüft das).
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Rm =
        new("Schnack.Localization.Strings", typeof(Strings).Assembly);

    /// <summary>Liefert den Text zum Schlüssel; fehlt er, den Schlüssel selbst (sichtbar, aber kein Absturz).</summary>
    public static string Get(string key) => Rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    public static string Common_Save => Get(nameof(Common_Save));
    public static string Common_Cancel => Get(nameof(Common_Cancel));
    public static string Common_Close => Get(nameof(Common_Close));

    public static string Language_German => Get(nameof(Language_German));
    public static string Language_English => Get(nameof(Language_English));

    public static string Mode_German => Get(nameof(Mode_German));
    public static string Mode_English => Get(nameof(Mode_English));
    public static string Mode_GermanToEnglish => Get(nameof(Mode_GermanToEnglish));
    public static string Mode_EnglishToGerman => Get(nameof(Mode_EnglishToGerman));

    public static string Tray_Hint => Get(nameof(Tray_Hint));
    public static string Tray_Settings => Get(nameof(Tray_Settings));
    public static string Tray_FloatingButton => Get(nameof(Tray_FloatingButton));
    public static string Tray_About => Get(nameof(Tray_About));
    public static string Tray_CheckUpdates => Get(nameof(Tray_CheckUpdates));
    public static string Tray_Exit => Get(nameof(Tray_Exit));
    public static string Tray_UpdateInstall => Get(nameof(Tray_UpdateInstall));
    public static string Tray_TooltipIdle => Get(nameof(Tray_TooltipIdle));
    public static string Tray_TooltipRecording => Get(nameof(Tray_TooltipRecording));
    public static string Tray_TooltipProcessing => Get(nameof(Tray_TooltipProcessing));

    public static string Balloon_AppTitle => Get(nameof(Balloon_AppTitle));
    public static string Balloon_NoSpeech => Get(nameof(Balloon_NoSpeech));
    public static string Balloon_HintTitle => Get(nameof(Balloon_HintTitle));
    public static string Balloon_Truncated => Get(nameof(Balloon_Truncated));
    public static string Balloon_MicErrorTitle => Get(nameof(Balloon_MicErrorTitle));
    public static string Balloon_MicErrorStart => Get(nameof(Balloon_MicErrorStart));
    public static string Balloon_AlreadyRunning => Get(nameof(Balloon_AlreadyRunning));

    public static string Error_MicTimeoutTitle => Get(nameof(Error_MicTimeoutTitle));
    public static string Error_MicTimeout => Get(nameof(Error_MicTimeout));
    public static string Error_NoTargetWindowTitle => Get(nameof(Error_NoTargetWindowTitle));
    public static string Error_NoTargetWindow => Get(nameof(Error_NoTargetWindow));
    public static string Error_NoTargetWindowClipboard => Get(nameof(Error_NoTargetWindowClipboard));
    public static string Error_MissingOpenAiKeyTitle => Get(nameof(Error_MissingOpenAiKeyTitle));
    public static string Error_MissingOpenAiKey => Get(nameof(Error_MissingOpenAiKey));
    public static string Error_MissingAnthropicKeyTitle => Get(nameof(Error_MissingAnthropicKeyTitle));
    public static string Error_MissingAnthropicKey => Get(nameof(Error_MissingAnthropicKey));
    public static string Error_WhisperModelMissingTitle => Get(nameof(Error_WhisperModelMissingTitle));
    public static string Error_WhisperModelMissing => Get(nameof(Error_WhisperModelMissing));
    public static string Error_ApiKeyInvalidTitle => Get(nameof(Error_ApiKeyInvalidTitle));
    public static string Error_ApiKeyInvalid => Get(nameof(Error_ApiKeyInvalid));
    public static string Error_RateLimitTitle => Get(nameof(Error_RateLimitTitle));
    public static string Error_RateLimit => Get(nameof(Error_RateLimit));
    public static string Error_NetworkTitle => Get(nameof(Error_NetworkTitle));
    public static string Error_Network => Get(nameof(Error_Network));
    public static string Error_EmptyResponse => Get(nameof(Error_EmptyResponse));
    public static string Error_GenericTitle => Get(nameof(Error_GenericTitle));
    public static string Error_Generic => Get(nameof(Error_Generic));

    public static string Update_AvailableTitle => Get(nameof(Update_AvailableTitle));
    public static string Update_AvailableText => Get(nameof(Update_AvailableText));
    public static string Update_UpToDateTitle => Get(nameof(Update_UpToDateTitle));
    public static string Update_UpToDateText => Get(nameof(Update_UpToDateText));
    public static string Update_CheckFailedTitle => Get(nameof(Update_CheckFailedTitle));
    public static string Update_CheckFailedText => Get(nameof(Update_CheckFailedText));
    public static string Update_DownloadingTitle => Get(nameof(Update_DownloadingTitle));
    public static string Update_DownloadingText => Get(nameof(Update_DownloadingText));
    public static string Update_FailedTitle => Get(nameof(Update_FailedTitle));
    public static string Update_FailedText => Get(nameof(Update_FailedText));
    public static string Update_RestartConfirmTitle => Get(nameof(Update_RestartConfirmTitle));
    public static string Update_RestartConfirm => Get(nameof(Update_RestartConfirm));

    public static string Startup_DotNetRequiredTitle => Get(nameof(Startup_DotNetRequiredTitle));
    public static string Startup_DotNetRequired => Get(nameof(Startup_DotNetRequired));
    public static string Startup_HotkeyFailed => Get(nameof(Startup_HotkeyFailed));

    public static string Settings_Title => Get(nameof(Settings_Title));
    public static string Settings_Backend => Get(nameof(Settings_Backend));
    public static string Settings_BackendOpenAi => Get(nameof(Settings_BackendOpenAi));
    public static string Settings_BackendClaude => Get(nameof(Settings_BackendClaude));
    public static string Settings_OpenAiSection => Get(nameof(Settings_OpenAiSection));
    public static string Settings_SttModel => Get(nameof(Settings_SttModel));
    public static string Settings_ChatModel => Get(nameof(Settings_ChatModel));
    public static string Settings_ClaudeSection => Get(nameof(Settings_ClaudeSection));
    public static string Settings_ClaudeModel => Get(nameof(Settings_ClaudeModel));
    public static string Settings_MaxTokens => Get(nameof(Settings_MaxTokens));
    public static string Settings_WhisperSection => Get(nameof(Settings_WhisperSection));
    public static string Settings_WhisperModel => Get(nameof(Settings_WhisperModel));
    public static string Settings_WhisperStatus => Get(nameof(Settings_WhisperStatus));
    public static string Settings_Download => Get(nameof(Settings_Download));
    public static string Settings_GeneralSection => Get(nameof(Settings_GeneralSection));
    public static string Settings_UiLanguage => Get(nameof(Settings_UiLanguage));
    public static string Settings_DefaultMode => Get(nameof(Settings_DefaultMode));
    public static string Settings_Hotkey => Get(nameof(Settings_Hotkey));
    public static string Settings_Microphone => Get(nameof(Settings_Microphone));
    public static string Settings_RestoreClipboard => Get(nameof(Settings_RestoreClipboard));
    public static string Settings_ClipboardFree => Get(nameof(Settings_ClipboardFree));
    public static string Settings_ClipboardFreeOption => Get(nameof(Settings_ClipboardFreeOption));
    public static string Settings_ClipboardFreeHint => Get(nameof(Settings_ClipboardFreeHint));
    public static string Settings_DebugLogging => Get(nameof(Settings_DebugLogging));
    public static string Settings_DebugLoggingOption => Get(nameof(Settings_DebugLoggingOption));
    public static string Settings_DebugLoggingHint => Get(nameof(Settings_DebugLoggingHint));
    public static string Settings_VocabularySection => Get(nameof(Settings_VocabularySection));
    public static string Settings_VocabularyHint => Get(nameof(Settings_VocabularyHint));
    public static string Settings_ApiKeysSection => Get(nameof(Settings_ApiKeysSection));
    public static string Settings_AnthropicKey => Get(nameof(Settings_AnthropicKey));
    public static string Settings_OpenAiKey => Get(nameof(Settings_OpenAiKey));
    public static string Settings_KeyHint => Get(nameof(Settings_KeyHint));
    public static string Settings_KeyStored => Get(nameof(Settings_KeyStored));
    public static string Settings_KeyNotStored => Get(nameof(Settings_KeyNotStored));
    public static string Settings_MicDefault => Get(nameof(Settings_MicDefault));
    public static string Settings_MicFallback => Get(nameof(Settings_MicFallback));
    public static string Settings_ModelPresent => Get(nameof(Settings_ModelPresent));
    public static string Settings_ModelMissing => Get(nameof(Settings_ModelMissing));
    public static string Settings_ModelDownloaded => Get(nameof(Settings_ModelDownloaded));
    public static string Settings_Downloading => Get(nameof(Settings_Downloading));
    public static string Settings_DownloadingPercent => Get(nameof(Settings_DownloadingPercent));
    public static string Settings_DownloadFailed => Get(nameof(Settings_DownloadFailed));
    public static string Settings_DiscardChanges => Get(nameof(Settings_DiscardChanges));

    public static string About_Title => Get(nameof(About_Title));
    public static string About_Version => Get(nameof(About_Version));
    public static string About_Description => Get(nameof(About_Description));

    public static string FirstRun_Title => Get(nameof(FirstRun_Title));
    public static string FirstRun_Intro => Get(nameof(FirstRun_Intro));
    public static string FirstRun_Language => Get(nameof(FirstRun_Language));
    public static string FirstRun_Step1 => Get(nameof(FirstRun_Step1));
    public static string FirstRun_Step2 => Get(nameof(FirstRun_Step2));
    public static string FirstRun_OpenSettings => Get(nameof(FirstRun_OpenSettings));
}
