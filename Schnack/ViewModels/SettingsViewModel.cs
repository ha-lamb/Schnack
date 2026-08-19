using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Schnack.Commands;
using Schnack.Localization;
using Schnack.Models;
using Schnack.Services;

namespace Schnack.ViewModels;

public sealed class MicrophoneOption
{
    public int? DeviceIndex { get; init; }
    public string Name { get; init; } = "";

    public override string ToString() => Name;
}

/// <summary>Auswahleintrag für eine der vier Diktat-Optionen.</summary>
public sealed class DictationOption
{
    public DictationChoice Value { get; init; }
    public string Display { get; init; } = "";
    public override string ToString() => Display;
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
    private readonly BackendProvider _baseBackendProvider;
    private readonly AppLanguage _baseUiLanguage;
    private readonly DictationChoice _baseDictation;
    private readonly string _baseOpenAiTranscriptionModel;
    private readonly string _baseOpenAiChatModel;
    private readonly string _baseClaudeModel;
    private readonly int _baseClaudeMaxTokens;
    private readonly string _baseWhisperModel;
    private readonly string _baseHotkey;
    private readonly bool _baseRestoreClipboard;
    private readonly bool _basePreferClipboardFreeInsertion;
    private readonly bool _baseDebugLogging;
    private readonly int? _baseMicrophoneDeviceId;

    private BackendProvider _backendProvider;
    private LanguageOption _uiLanguage;
    private DictationOption _selectedDictation;
    private string _openAiTranscriptionModel;
    private string _openAiChatModel;
    private string _claudeModel;
    private int _claudeMaxTokens;
    private string _whisperModel;
    private string _hotkey;
    private bool _restoreClipboard;
    private bool _preferClipboardFreeInsertion;
    private bool _debugLogging;
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
        DictationOptions = DictationChoice.All
            .Select(c => new DictationOption { Value = c, Display = c.DisplayName })
            .ToArray();

        var s = settingsService.Settings;
        _backendProvider = s.BackendProvider;
        _uiLanguage = LanguageOptions.First(l => l.Value == s.UiLanguage);
        var currentChoice = DictationChoice.FromSettings(s);
        _selectedDictation = DictationOptions.First(o => o.Value == currentChoice);
        _openAiTranscriptionModel = s.OpenAiTranscriptionModel;
        _openAiChatModel = s.OpenAiChatModel;
        _claudeModel = s.ClaudeModel;
        _claudeMaxTokens = s.ClaudeMaxTokens;
        _whisperModel = s.WhisperModel;
        _hotkey = s.Hotkey;
        _restoreClipboard = s.RestoreClipboard;
        _preferClipboardFreeInsertion = s.PreferClipboardFreeInsertion;
        _debugLogging = s.DebugLogging;

        // Baseline festhalten
        _baseBackendProvider = _backendProvider;
        _baseUiLanguage = s.UiLanguage;
        _baseDictation = currentChoice;
        _baseOpenAiTranscriptionModel = _openAiTranscriptionModel;
        _baseOpenAiChatModel = _openAiChatModel;
        _baseClaudeModel = _claudeModel;
        _baseClaudeMaxTokens = _claudeMaxTokens;
        _baseWhisperModel = _whisperModel;
        _baseHotkey = _hotkey;
        _baseRestoreClipboard = _restoreClipboard;
        _basePreferClipboardFreeInsertion = _preferClipboardFreeInsertion;
        _baseDebugLogging = _debugLogging;
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
        SaveOpenAiApiKeyCommand = new RelayCommand(apiKey => SaveOpenAiApiKey(apiKey as string ?? string.Empty));
        DownloadWhisperModelCommand = new RelayCommand(_ => StartWhisperDownload(), _ => !_isDownloading);
    }

    public bool IsDirty =>
        _backendProvider != _baseBackendProvider ||
        _uiLanguage.Value != _baseUiLanguage ||
        _selectedDictation.Value != _baseDictation ||
        _openAiTranscriptionModel != _baseOpenAiTranscriptionModel ||
        _openAiChatModel != _baseOpenAiChatModel ||
        _claudeModel != _baseClaudeModel ||
        _claudeMaxTokens != _baseClaudeMaxTokens ||
        _whisperModel != _baseWhisperModel ||
        _hotkey != _baseHotkey ||
        _restoreClipboard != _baseRestoreClipboard ||
        _preferClipboardFreeInsertion != _basePreferClipboardFreeInsertion ||
        _debugLogging != _baseDebugLogging ||
        _selectedMicrophone?.DeviceIndex != _baseMicrophoneDeviceId;

    public BackendProvider BackendProvider
    {
        get => _backendProvider;
        set
        {
            _backendProvider = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOpenAiBackend));
            OnPropertyChanged(nameof(IsClaudeBackend));
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public bool IsOpenAiBackend
    {
        get => _backendProvider == BackendProvider.OpenAi;
        set { if (value) BackendProvider = BackendProvider.OpenAi; }
    }

    public bool IsClaudeBackend
    {
        get => _backendProvider == BackendProvider.Claude;
        set { if (value) BackendProvider = BackendProvider.Claude; }
    }

    public LanguageOption[] LanguageOptions { get; }
    public DictationOption[] DictationOptions { get; }
    public ObservableCollection<MicrophoneOption> MicrophoneOptions { get; }

    public LanguageOption UiLanguage
    {
        get => _uiLanguage;
        set { _uiLanguage = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public DictationOption SelectedDictation
    {
        get => _selectedDictation;
        set { _selectedDictation = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public MicrophoneOption? SelectedMicrophone
    {
        get => _selectedMicrophone;
        set { _selectedMicrophone = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public string OpenAiTranscriptionModel
    {
        get => _openAiTranscriptionModel;
        set { _openAiTranscriptionModel = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public string OpenAiChatModel
    {
        get => _openAiChatModel;
        set { _openAiChatModel = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public string ClaudeModel
    {
        get => _claudeModel;
        set { _claudeModel = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public int ClaudeMaxTokens
    {
        get => _claudeMaxTokens;
        set { _claudeMaxTokens = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); }
    }

    public string WhisperModel
    {
        get => _whisperModel;
        set { _whisperModel = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); UpdateWhisperDownloadStatus(); }
    }

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

    public string AnthropicApiKeyStatus =>
        _secretService.HasApiKey() ? Strings.Settings_KeyStored : Strings.Settings_KeyNotStored;
    public bool IsAnthropicApiKeyStored => _secretService.HasApiKey();

    public string OpenAiApiKeyStatus =>
        _secretService.HasOpenAiApiKey() ? Strings.Settings_KeyStored : Strings.Settings_KeyNotStored;
    public bool IsOpenAiApiKeyStored => _secretService.HasOpenAiApiKey();

    public string[] OpenAiSttModelOptions { get; } = ["gpt-4o-mini-transcribe", "gpt-4o-transcribe", "whisper-1"];
    public string[] WhisperModelOptions { get; } = ["large-v3-turbo", "medium", "base", "small", "tiny"];

    public ICommand SaveCommand { get; }
    public ICommand SaveApiKeyCommand { get; }
    public ICommand SaveOpenAiApiKeyCommand { get; }
    public ICommand DownloadWhisperModelCommand { get; }

    private void SaveSettings()
    {
        var updated = _settingsService.Settings with
        {
            BackendProvider = _backendProvider,
            UiLanguage = _uiLanguage.Value,
            DictationLanguage = _selectedDictation.Value.Language,
            DefaultMode = _selectedDictation.Value.ModeValue,
            OpenAiTranscriptionModel = _openAiTranscriptionModel,
            OpenAiChatModel = _openAiChatModel,
            ClaudeModel = _claudeModel,
            ClaudeMaxTokens = _claudeMaxTokens,
            WhisperModel = _whisperModel,
            Hotkey = _hotkey,
            RestoreClipboard = _restoreClipboard,
            PreferClipboardFreeInsertion = _preferClipboardFreeInsertion,
            DebugLogging = _debugLogging,
            MicrophoneDeviceId = SelectedMicrophone?.DeviceIndex
        };

        _settingsService.UpdateSettings(updated);
        _ = _settingsService.SaveAsync();
    }

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

    private void SaveApiKey(string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _secretService.SaveApiKey(apiKey);
            OnPropertyChanged(nameof(AnthropicApiKeyStatus));
            OnPropertyChanged(nameof(IsAnthropicApiKeyStored));
        }
    }

    private void SaveOpenAiApiKey(string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _secretService.SaveOpenAiApiKey(apiKey);
            OnPropertyChanged(nameof(OpenAiApiKeyStatus));
            OnPropertyChanged(nameof(IsOpenAiApiKeyStored));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
