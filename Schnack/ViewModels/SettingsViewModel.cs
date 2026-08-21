using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Schnack.Commands;
using Schnack.Localization;
using Schnack.Models;
using Schnack.Services;
using Schnack.Services.Internal;

namespace Schnack.ViewModels;

public sealed class MicrophoneOption
{
    public int? DeviceIndex { get; init; }
    public string Name { get; init; } = "";

    public override string ToString() => Name;
}

public sealed class LanguageOption
{
    public AppLanguage Value { get; init; }
    public string Display { get; init; } = "";
    public override string ToString() => Display;
}

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;
    private readonly ISecretService _secretService;
    private readonly IWhisperModelDownloadService _downloadService;

    // Baseline für Dirty-Tracking
    private readonly AiService _baseAiService;
    private readonly AppLanguage _baseUiLanguage;
    private readonly string _baseOpenAiChatModel;
    private readonly string _baseOpenAiChatMaxTokens;
    private readonly string _baseClaudeModel;
    private readonly string _baseClaudeMaxTokens;
    private readonly string _baseWhisperModel;
    private readonly bool _baseTextSmoothing;
    private readonly bool _baseWhisperPreload;
    private readonly bool _baseWhisperUseGpu;
    private readonly string _baseHotkey;
    private readonly bool _baseRestoreClipboard;
    private readonly bool _basePreferClipboardFreeInsertion;
    private readonly bool _baseDebugLogging;
    private readonly string _baseVocabularyText;
    private readonly int? _baseMicrophoneDeviceId;

    private AiService _aiService;
    private LanguageOption _uiLanguage;
    private string _openAiChatModel;
    private string _claudeModel;
    // Beide Token-Grenzen bewusst als string: eine ungültige Eingabe ließe das ViewModel
    // sonst stumm auf dem alten Wert stehen, ohne dass der Nutzer etwas davon merkt.
    private string _claudeMaxTokens;
    private string _openAiChatMaxTokens;
    private string _whisperModel;
    private bool _textSmoothing;
    private bool _whisperPreload;
    private bool _whisperUseGpu;
    private string _hotkey;
    private bool _restoreClipboard;
    private bool _preferClipboardFreeInsertion;
    private bool _debugLogging;
    private string _vocabularyText;
    private MicrophoneOption? _selectedMicrophone;

    private string _whisperDownloadStatus = "";
    private double _whisperDownloadProgress;
    private bool _isDownloading;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(
        ISettingsService settingsService,
        ISecretService secretService,
        IWhisperModelDownloadService downloadService)
    {
        _settingsService = settingsService;
        _secretService = secretService;
        _downloadService = downloadService;

        LanguageOptions =
        [
            new LanguageOption { Value = AppLanguage.De, Display = Strings.Language_German },
            new LanguageOption { Value = AppLanguage.En, Display = Strings.Language_English }
        ];

        var s = settingsService.Settings;
        _aiService = s.AiService;
        _textSmoothing = s.TextSmoothing;
        _uiLanguage = LanguageOptions.First(l => l.Value == s.UiLanguage);
        _openAiChatModel = s.OpenAiChatModel;
        _openAiChatMaxTokens = s.OpenAiChatMaxTokens.ToString(CultureInfo.InvariantCulture);
        _claudeModel = s.ClaudeModel;
        _claudeMaxTokens = s.ClaudeMaxTokens.ToString(CultureInfo.InvariantCulture);
        _whisperModel = s.WhisperModel;
        _whisperPreload = s.WhisperPreload;
        _whisperUseGpu = s.WhisperUseGpu;
        _hotkey = s.Hotkey;
        _restoreClipboard = s.RestoreClipboard;
        _preferClipboardFreeInsertion = s.PreferClipboardFreeInsertion;
        _debugLogging = s.DebugLogging;
        _vocabularyText = string.Join(Environment.NewLine, s.Vocabulary);

        // Baseline festhalten
        _baseAiService = _aiService;
        _baseUiLanguage = s.UiLanguage;
        _baseOpenAiChatModel = _openAiChatModel;
        _baseOpenAiChatMaxTokens = _openAiChatMaxTokens;
        _baseClaudeModel = _claudeModel;
        _baseClaudeMaxTokens = _claudeMaxTokens;
        _baseWhisperModel = _whisperModel;
        _baseTextSmoothing = _textSmoothing;
        _baseWhisperPreload = _whisperPreload;
        _baseWhisperUseGpu = _whisperUseGpu;
        _baseHotkey = _hotkey;
        _baseRestoreClipboard = _restoreClipboard;
        _basePreferClipboardFreeInsertion = _preferClipboardFreeInsertion;
        _baseDebugLogging = _debugLogging;
        _baseVocabularyText = _vocabularyText;
        _baseMicrophoneDeviceId = s.MicrophoneDeviceId;

        MicrophoneOptions = new ObservableCollection<MicrophoneOption>
        {
            new() { DeviceIndex = null, Name = Strings.Settings_MicDefault }
        };
        foreach (var (index, name) in MicrophoneEnumerator.ListCaptureDevices())
            MicrophoneOptions.Add(new MicrophoneOption { DeviceIndex = index, Name = name });

        _selectedMicrophone = MicrophoneOptions.FirstOrDefault(m => m.DeviceIndex == s.MicrophoneDeviceId)
            ?? MicrophoneOptions[0];

        UpdateWhisperDownloadStatus();

        SaveCommand = new RelayCommand(_ => SaveSettings());
        SaveApiKeyCommand = new RelayCommand(apiKey => SaveApiKey(apiKey as string ?? string.Empty));
        DownloadWhisperModelCommand = new RelayCommand(_ => StartWhisperDownload(), _ => !_isDownloading);
    }

    public bool IsDirty =>
        _aiService != _baseAiService ||
        _uiLanguage.Value != _baseUiLanguage ||
        _openAiChatModel != _baseOpenAiChatModel ||
        _openAiChatMaxTokens != _baseOpenAiChatMaxTokens ||
        _claudeModel != _baseClaudeModel ||
        _claudeMaxTokens != _baseClaudeMaxTokens ||
        _whisperModel != _baseWhisperModel ||
        _textSmoothing != _baseTextSmoothing ||
        _whisperPreload != _baseWhisperPreload ||
        _whisperUseGpu != _baseWhisperUseGpu ||
        _hotkey != _baseHotkey ||
        _restoreClipboard != _baseRestoreClipboard ||
        _preferClipboardFreeInsertion != _basePreferClipboardFreeInsertion ||
        _debugLogging != _baseDebugLogging ||
        _vocabularyText != _baseVocabularyText ||
        _selectedMicrophone?.DeviceIndex != _baseMicrophoneDeviceId;

    // ── Nachbearbeitung ──────────────────────────────────────────────────

    public AiService AiService
    {
        get => _aiService;
        set
        {
            _aiService = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOpenAiService));
            OnPropertyChanged(nameof(IsClaudeService));
            NotifyKeyDependentState();
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public bool IsOpenAiService
    {
        get => _aiService == AiService.OpenAi;
        set { if (value) AiService = AiService.OpenAi; }
    }

    public bool IsClaudeService
    {
        get => _aiService == AiService.Claude;
        set { if (value) AiService = AiService.Claude; }
    }

    public bool TextSmoothing
    {
        get => _textSmoothing;
        set
        {
            _textSmoothing = value;
            OnPropertyChanged();
            NotifyKeyDependentState();
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    /// <summary>Hat der gewählte Dienst einen hinterlegten Schlüssel?</summary>
    public bool SmoothingAvailable => _secretService.HasKeyFor(_aiService);

    /// <summary>Ohne Schlüssel gibt es nichts zu glätten — der Schalter wird dann gesperrt.</summary>
    public bool IsTextSmoothingEnabled => SmoothingAvailable;

    /// <summary>Hinweis einblenden, solange die Glättung mangels Schlüssel nicht greifen kann.</summary>
    public bool ShowsMissingKeyNote => !SmoothingAvailable;

    public string ApiKeyStatus =>
        SmoothingAvailable ? Strings.Settings_KeyStored : Strings.Settings_KeyNotStored;

    public bool IsApiKeyStored => SmoothingAvailable;

    /// <summary>
    /// Alles, was von Dienstwahl oder Schlüssel-Verfügbarkeit abhängt, in einem Aufwasch.
    /// Wird auch nach dem Speichern eines Schlüssels gebraucht — sonst bliebe die
    /// Glättungs-Checkbox gesperrt, obwohl der Schlüssel jetzt da ist.
    /// </summary>
    private void NotifyKeyDependentState()
    {
        OnPropertyChanged(nameof(SmoothingAvailable));
        OnPropertyChanged(nameof(IsTextSmoothingEnabled));
        OnPropertyChanged(nameof(ShowsMissingKeyNote));
        OnPropertyChanged(nameof(ApiKeyStatus));
        OnPropertyChanged(nameof(IsApiKeyStored));
    }

    // ── Diktat-Optionen ──────────────────────────────────────────────────

    public LanguageOption[] LanguageOptions { get; }

    public ObservableCollection<MicrophoneOption> MicrophoneOptions { get; }

    public LanguageOption UiLanguage
    {
        get => _uiLanguage;
        set { _uiLanguage = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public MicrophoneOption? SelectedMicrophone
    {
        get => _selectedMicrophone;
        set { _selectedMicrophone = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public string OpenAiChatModel
    {
        get => _openAiChatModel;
        set { _openAiChatModel = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public string OpenAiChatMaxTokens
    {
        get => _openAiChatMaxTokens;
        set { _openAiChatMaxTokens = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public string ClaudeModel
    {
        get => _claudeModel;
        set { _claudeModel = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public string ClaudeMaxTokens
    {
        get => _claudeMaxTokens;
        set { _claudeMaxTokens = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    // ── Spracherkennung ──────────────────────────────────────────────────

    public string WhisperModel
    {
        get => _whisperModel;
        set
        {
            _whisperModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDirty));
            UpdateWhisperDownloadStatus();
        }
    }

    public bool WhisperPreload
    {
        get => _whisperPreload;
        set { _whisperPreload = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public bool WhisperUseGpu
    {
        get => _whisperUseGpu;
        set { _whisperUseGpu = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public string WhisperDownloadStatus
    {
        get => _whisperDownloadStatus;
        private set { _whisperDownloadStatus = value; OnPropertyChanged(); }
    }

    public double WhisperDownloadProgress
    {
        get => _whisperDownloadProgress;
        private set { _whisperDownloadProgress = value; OnPropertyChanged(); }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            _isDownloading = value;
            OnPropertyChanged();
            ((RelayCommand)DownloadWhisperModelCommand).RaiseCanExecuteChanged();
        }
    }

    public string[] WhisperModelOptions { get; } = ["large-v3-turbo", "medium", "small", "base", "tiny"];

    // ── Bedienung ────────────────────────────────────────────────────────

    public string Hotkey
    {
        get => _hotkey;
        set { _hotkey = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public bool RestoreClipboard
    {
        get => _restoreClipboard;
        set { _restoreClipboard = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public bool PreferClipboardFreeInsertion
    {
        get => _preferClipboardFreeInsertion;
        set { _preferClipboardFreeInsertion = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public bool DebugLogging
    {
        get => _debugLogging;
        set { _debugLogging = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public string VocabularyText
    {
        get => _vocabularyText;
        set { _vocabularyText = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    // ── Befehle ──────────────────────────────────────────────────────────

    public ICommand SaveCommand { get; }
    public ICommand SaveApiKeyCommand { get; }
    public ICommand DownloadWhisperModelCommand { get; }

    private void SaveSettings()
    {
        var current = _settingsService.Settings;
        var updated = current with
        {
            AiService = _aiService,
            TextSmoothing = _textSmoothing,
            UiLanguage = _uiLanguage.Value,
            // DictationLanguage und DefaultMode bewusst NICHT: der Diktat-Modus wird
            // ausschließlich über das Tray-Menü gesetzt. Würde er hier mitgeschrieben,
            // überschriebe ein Speichern die Wahl mit dem Stand vom Öffnen des Dialogs.
            OpenAiChatModel = _openAiChatModel,
            OpenAiChatMaxTokens = ParseTokens(_openAiChatMaxTokens, current.OpenAiChatMaxTokens),
            ClaudeModel = _claudeModel,
            ClaudeMaxTokens = ParseTokens(_claudeMaxTokens, current.ClaudeMaxTokens),
            WhisperModel = _whisperModel,
            WhisperPreload = _whisperPreload,
            WhisperUseGpu = _whisperUseGpu,
            Hotkey = _hotkey,
            RestoreClipboard = _restoreClipboard,
            PreferClipboardFreeInsertion = _preferClipboardFreeInsertion,
            DebugLogging = _debugLogging,
            Vocabulary = VocabularyPrompt.Normalize(
                _vocabularyText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)),
            MicrophoneDeviceId = SelectedMicrophone?.DeviceIndex
        };

        _settingsService.UpdateSettings(updated);
        _ = _settingsService.SaveAsync();
    }

    /// <summary>Unsinnige Eingaben fallen auf den bisherigen Wert zurück, statt still zu null zu werden.</summary>
    private static int ParseTokens(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;

    private void StartWhisperDownload()
    {
        IsDownloading = true;
        WhisperDownloadProgress = 0;
        WhisperDownloadStatus = Strings.Settings_Downloading;

        var model = _whisperModel;
        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<double>(p =>
                {
                    WhisperDownloadProgress = p * 100;
                    WhisperDownloadStatus = Strings.Format(
                        nameof(Strings.Settings_DownloadingPercent), (p * 100).ToString("0"));
                });
                await _downloadService.DownloadModelAsync(model, progress);
                WhisperDownloadStatus = Strings.Settings_ModelDownloaded;
            }
            catch (Exception)
            {
                WhisperDownloadStatus = Strings.Settings_DownloadFailed;
            }
            finally
            {
                IsDownloading = false;
                UpdateWhisperDownloadStatus();
            }
        });
    }

    private void UpdateWhisperDownloadStatus()
    {
        if (_isDownloading) return;
        WhisperDownloadStatus = _downloadService.IsModelDownloaded(_whisperModel)
            ? Strings.Settings_ModelPresent
            : Strings.Settings_ModelMissing;
    }

    /// <summary>
    /// Speichert den Schlüssel für den aktuell gewählten Dienst. Wirkt sofort und ist nicht
    /// Teil des Dirty-Trackings — Abbrechen nimmt ihn also nicht zurück.
    /// </summary>
    private void SaveApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return;

        if (_aiService == AiService.Claude)
            _secretService.SaveApiKey(apiKey);
        else
            _secretService.SaveOpenAiApiKey(apiKey);

        NotifyKeyDependentState();
        OnPropertyChanged(nameof(IsDirty));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
