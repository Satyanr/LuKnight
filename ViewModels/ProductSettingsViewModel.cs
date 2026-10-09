using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using LuKnight.Models;
using LuKnight.Assistant;
using LuKnight.Services;
using System.Diagnostics;

namespace LuKnight.ViewModels;

public sealed record DesktopPermissionChoice(
    DesktopPermissionLevel Value,
    string Label);

public sealed class ProductSettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppServices _services;
    private readonly Action _clear, _shutdown;
    private readonly Func<bool> _safeToInstall, _save;
    private readonly CancellationTokenSource _lifetime = new();
    private string? _message;
    private bool _deferred;
    private bool _disposed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ProductSettingsViewModel(AppServices services, Action clear, Func<bool> safeToInstall, Func<bool> save, Action shutdown)
    {
        _services = services; _clear = clear; _safeToInstall = safeToInstall; _save = save; _shutdown = shutdown;
        TestCommand = new(async () => { _message = null; await services.Chat.TestConnection(_lifetime.Token); }, () => CanEditChat);
        CheckCommand = new(async () => { _deferred = false; await services.Updates.CheckForUpdate(false, _lifetime.Token); }, () => !_disposed && !services.Updates.IsBusy);
        DownloadCommand = new(async () => { _deferred = false; await services.Updates.DownloadUpdate(_lifetime.Token); }, () => CanDownload);
        InstallCommand = new(async () => { await services.Updates.InstallUpdate(_safeToInstall, _save, _shutdown, _lifetime.Token); }, () => CanInstall);
        PreviewVoiceCommand = new(PreviewVoiceAsync, () => !_disposed && !_services.Assistant.IsBusy && !_services.TextToSpeech.IsSpeaking);
        foreach (var command in Commands) command.Completed += Refresh;
        RemoveKeyCommand = new(() => Run(services.Chat.RemoveKey), () => CanEditChat);
        ClearCommand = new(() => Run(_clear), () => CanEditChat);
        SaveCommand = new(() => { services.Settings.Save(); Refresh(); }, () => !_disposed);
        LaterCommand = new(() => { _deferred = true; Refresh(); }, () => !_disposed && !services.Updates.IsBusy);
        DeleteVoiceModelCommand = new(DeleteSelectedVoiceModel, () => CanDeleteVoiceModel);
    }
    public AsyncSettingsCommand TestCommand { get; }
    public AsyncSettingsCommand CheckCommand { get; }
    public AsyncSettingsCommand DownloadCommand { get; }
    public AsyncSettingsCommand InstallCommand { get; }
    public AsyncSettingsCommand PreviewVoiceCommand { get; }
    public SettingsCommand RemoveKeyCommand { get; }
    public SettingsCommand ClearCommand { get; }
    public SettingsCommand SaveCommand { get; }
    public SettingsCommand LaterCommand { get; }
    public SettingsCommand DeleteVoiceModelCommand { get; }
    private AsyncSettingsCommand[] Commands => [TestCommand, CheckCommand, DownloadCommand, InstallCommand, PreviewVoiceCommand];
    public Array Providers => Enum.GetValues<ChatProvider>();
    public Array Languages => Enum.GetValues<ChatLanguage>();
    public Array Lengths => Enum.GetValues<ResponseLength>();
    public Array Styles => Enum.GetValues<ResponseStyle>();
    public Array VoiceLanguages => Enum.GetValues<SpeechLanguage>();
    public Array VoiceModels => Enum.GetValues<SpeechModel>();
    public Array VoiceSubmissionModes => Enum.GetValues<VoiceSubmissionMode>();
    public Array TextToSpeechModes => Enum.GetValues<TextToSpeechMode>();
    private const string DefaultVoiceChoice = "Windows default";
    public IReadOnlyList<string> TextToSpeechVoices
    {
        get
        {
            List<string> voices = [DefaultVoiceChoice];
            voices.AddRange(_services.TextToSpeech.GetInstalledVoices());
            return voices;
        }
    }
    public DesktopPermissionChoice[]
        DesktopPermissionChoices { get; } =
    [
        new(
            DesktopPermissionLevel.ObserveOnly,
            "Observe only"),

        new(
            DesktopPermissionLevel.Navigation,
            "Navigation"),

        new(
            DesktopPermissionLevel.Interaction,
            "Interaction"),

        new(
            DesktopPermissionLevel.Sensitive,
            "Sensitive")
    ];

    public DesktopPermissionLevel
        DesktopPermission
    {
        get =>
            _services.Chat.Options
                .DesktopPermission;

        set
        {
            if (value ==
                _services.Chat.Options
                    .DesktopPermission)
            {
                return;
            }

            Change(
                _services.Chat.Options with
                {
                    DesktopPermission =
                        value
                });
        }
    }

    public bool DesktopPermissionEnabled =>
        CanEditChat &&
        UseDesktopActions;

    public string DesktopPermissionDescription =>
        DesktopPermission switch
        {
            DesktopPermissionLevel.ObserveOnly =>
                "Lu-Knight hanya boleh membaca konteks desktop yang sudah diizinkan. Semua aksi desktop diblokir.",

            DesktopPermissionLevel.Navigation =>
                "Boleh membuka dan memfokuskan aplikasi/window serta membuka atau mencari melalui Explorer. Tetap meminta konfirmasi.",

            DesktopPermissionLevel.Interaction =>
                "Navigation + klik control dan mengisi text field aman. Tindakan sensitif tetap diblokir.",

            DesktopPermissionLevel.Sensitive =>
                "Interaction + tindakan sensitif seperti Save, Send, Delete, Upload, Pay, atau Shutdown. Sensitive selalu memerlukan konfirmasi dua tahap.",

            _ =>
                "Permission desktop tidak valid."
        };

    public bool CanEditChat => !_disposed && !_services.Assistant.IsShuttingDown && !_services.Assistant.IsBusy;
    public string CredentialStatus => _services.Chat.CredentialStatus;
    public string ChatStatus => _message ?? _services.Chat.Status;
    public string SaveStatus => _services.Settings.Status;
    public string UpdateStatus => _deferred ? "Update ditunda. Lanjutkan melalui About kapan saja." : _services.Updates.Status;
    public bool CanCheck => !_disposed && !_services.Updates.IsBusy;
    public bool CanDownload => !_disposed && !_services.Updates.IsBusy && _services.Updates.Available is not null && _services.Updates.VerifiedInstaller is null;
    public bool CanInstall => !_disposed && !_services.Updates.IsBusy && _services.Updates.VerifiedInstaller is not null && _safeToInstall();
    public ChatProvider Provider
    {
        get => _services.Chat.Options.Provider;
        set
        {
            if (value == _services.Chat.Options.Provider) return;
            Change(_services.Chat.Options with { Provider = value });
        }
    }
    public IReadOnlyList<string> GeminiModels => GeminiModelCatalog.AssistantModels;
    public bool AutoModelFallback
    {
        get => _services.Chat.Options.AutoModelFallback;
        set
        {
            if (value == _services.Chat.Options.AutoModelFallback) return;
            Change(_services.Chat.Options with { AutoModelFallback = value });
        }
    }
    public string Model
    {
        get => _services.Chat.Options.Model;
        set
        {
            string model = (value ?? string.Empty).Trim();
            if (string.Equals(model, _services.Chat.Options.Model, StringComparison.Ordinal)) return;
            Change(_services.Chat.Options with { Model = model });
        }
    }
    public ChatLanguage Language
    {
        get => _services.Chat.Options.Language;
        set
        {
            if (value == _services.Chat.Options.Language) return;
            Change(_services.Chat.Options with { Language = value });
        }
    }
    public ResponseLength Length
    {
        get => _services.Chat.Options.ResponseLength;
        set
        {
            if (value == _services.Chat.Options.ResponseLength) return;
            Change(_services.Chat.Options with { ResponseLength = value });
        }
    }
    public ResponseStyle Style
    {
        get => _services.Chat.Options.Style;
        set
        {
            if (value == _services.Chat.Options.Style) return;
            Change(_services.Chat.Options with { Style = value });
        }
    }
    public bool Remember
    {
        get => _services.Chat.Options.RememberConversation;
        set
        {
            if (value == _services.Chat.Options.RememberConversation) return;
            Change(_services.Chat.Options with { RememberConversation = value });
        }
    }
    public bool UseLongTermMemory
    {
        get => _services.Chat.Options.UseLongTermMemory;
        set
        {
            if (value == _services.Chat.Options.UseLongTermMemory) return;
            Change(_services.Chat.Options with { UseLongTermMemory = value });
        }
    }
    public bool UseApplicationContext
    {
        get =>
            _services.Chat.Options
                .UseApplicationContext;

        set
        {
            if (value ==
                _services.Chat.Options
                    .UseApplicationContext)
            {
                return;
            }


            Run(
                () =>
                {
                    AppSettings updated =
                        SettingsCapabilityPolicy
                            .SetApplicationAwareness(
                                _services.Settings
                                    .Current,
                                value);


                    AppSettings validated =
                        SettingsService.Validate(
                            updated);


                    //
                    // Keep runtime chat configuration and
                    // persisted settings synchronized.
                    //

                    _services.Chat.Configure(
                        validated.Chat);


                    _services.Settings.Update(
                        validated);
                });
        }
    }
    private static string
        AccessState(
            bool enabled) =>
                enabled
                    ? "ON"
                    : "OFF";


    public string AccessSummary
    {
        get
        {
            ChatSettings chat =
                _services.Chat.Options;


            CompanionSettings companion =
                _services.Settings
                    .Current
                    .Companion;


            return
                $"Application awareness: {AccessState(chat.UseApplicationContext)}\n" +
                $"File context: {AccessState(chat.UseFileContext)}\n" +
                $"Clipboard context: {AccessState(chat.UseClipboardContext)}\n" +
                $"System context: {AccessState(chat.UseSystemContext)}\n" +
                $"Screen context: {AccessState(chat.UseScreenContext)}\n" +
                $"Voice input: {AccessState(chat.UseVoiceInput)}\n" +
                $"Desktop actions: {AccessState(chat.UseDesktopActions)}\n" +
                $"Proactive companion: {AccessState(companion.Enabled)}";
        }
    }


    public string ContextProviderNotice =>
        _services.Chat.UsesGemini
            ? "Gemini aktif. Context yang Anda izinkan dan digunakan pada request " +
              "dapat dikirim ke provider Gemini. Eksekusi desktop action tetap " +
              "dilakukan oleh engine lokal setelah validasi dan konfirmasi."
            : "Mode lokal aktif. Chat tidak menggunakan provider Gemini. " +
              "Desktop action tetap melalui engine lokal, permission, dan confirmation.";
    public bool UseFileContext
    {
        get => _services.Chat.Options.UseFileContext;
        set
        {
            if (value == _services.Chat.Options.UseFileContext) return;
            Change(_services.Chat.Options with { UseFileContext = value });
        }
    }
    public bool UseClipboardContext
    {
        get => _services.Chat.Options.UseClipboardContext;
        set
        {
            if (value == _services.Chat.Options.UseClipboardContext) return;
            Change(_services.Chat.Options with { UseClipboardContext = value });
        }
    }
    public bool UseSystemContext
    {
        get => _services.Chat.Options.UseSystemContext;
        set
        {
            if (value == _services.Chat.Options.UseSystemContext) return;
            Change(_services.Chat.Options with { UseSystemContext = value });
        }
    }
    public bool UseScreenContext
    {
        get => _services.Chat.Options.UseScreenContext;
        set
        {
            if (value == _services.Chat.Options.UseScreenContext) return;
            Change(_services.Chat.Options with { UseScreenContext = value });
        }
    }
    public bool UseVoiceInput
    {
        get => _services.Chat.Options.UseVoiceInput;
        set
        {
            if (value == _services.Chat.Options.UseVoiceInput) return;
            Change(_services.Chat.Options with { UseVoiceInput = value });
        }
    }
    public SpeechLanguage VoiceLanguage
    {
        get => _services.Chat.Options.VoiceLanguage;
        set
        {
            if (value == _services.Chat.Options.VoiceLanguage) return;
            Change(_services.Chat.Options with { VoiceLanguage = value });
        }
    }
    public SpeechModel VoiceModel
    {
        get => _services.Chat.Options.VoiceModel;
        set
        {
            if (value == _services.Chat.Options.VoiceModel) return;
            Change(_services.Chat.Options with { VoiceModel = value });
        }
    }
    public VoiceSubmissionMode VoiceSubmissionMode
    {
        get => _services.Chat.Options.VoiceSubmissionMode;
        set
        {
            if (value == _services.Chat.Options.VoiceSubmissionMode) return;
            Change(_services.Chat.Options with { VoiceSubmissionMode = value });
        }
    }
    public string VoiceModelDescription => SpeechToTextCatalog.GetDescription(VoiceModel);
    public string TextToSpeechVoice
    {
        get
        {
            string voice = _services.Chat.Options.TextToSpeechVoice;
            return string.IsNullOrWhiteSpace(voice) ? DefaultVoiceChoice : voice;
        }
        set
        {
            string stored = string.Equals(value, DefaultVoiceChoice, StringComparison.Ordinal)
                ? string.Empty
                : value ?? string.Empty;
            if (stored == _services.Chat.Options.TextToSpeechVoice) return;
            Change(_services.Chat.Options with { TextToSpeechVoice = stored });
        }
    }
    public TextToSpeechMode TextToSpeechMode
    {
        get => _services.Chat.Options.TextToSpeechMode;
        set
        {
            if (value == _services.Chat.Options.TextToSpeechMode) return;
            Change(_services.Chat.Options with { TextToSpeechMode = value });
        }
    }
    public int TextToSpeechRate
    {
        get => _services.Chat.Options.TextToSpeechRate;
        set => Change(_services.Chat.Options with { TextToSpeechRate = value });
    }
    public int TextToSpeechVolume
    {
        get => _services.Chat.Options.TextToSpeechVolume;
        set => Change(_services.Chat.Options with { TextToSpeechVolume = value });
    }
    public string VoiceModelStatus => _services.SpeechToText.IsModelReady(VoiceModel)
        ? $"{VoiceModel} siap digunakan secara lokal."
        : $"{VoiceModel} belum diunduh. Model akan diunduh saat voice pertama digunakan.";
    public bool CanDeleteVoiceModel =>
        !_disposed && !_services.Assistant.IsBusy && _services.SpeechToText.IsModelReady(VoiceModel);
    public bool UseDesktopActions
    {
        get => _services.Chat.Options.UseDesktopActions;
        set
        {
            if (value == _services.Chat.Options.UseDesktopActions) return;
            Change(_services.Chat.Options with { UseDesktopActions = value });
        }
    }
    public int LongTermMemoryCount => _services.Memory.Count;
    public string LongTermMemorySummary => LongTermMemoryCount == 0
        ? "Belum ada long-term memory tersimpan."
        : $"{LongTermMemoryCount} long-term memory tersimpan.";
    public string LongTermMemoryStatus => string.IsNullOrWhiteSpace(_services.Memory.Status)
        ? LongTermMemorySummary
        : _services.Memory.Status;
    public bool CanClearLongTermMemory => !_services.Assistant.IsBusy && _services.Memory.Count > 0;
    private void Change(ChatSettings options)
    {
        if (options == _services.Chat.Options) return;
        Run(() =>
        {
            AppSettings config = SettingsService.Validate(_services.Settings.Current with { Chat = options });
            _services.Chat.Configure(options);
            _services.Settings.Update(config);
        });
    }
    private void Run(Action action)
    {
        if (_disposed) return;
        try
        {
            action();
            _message = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(DiagnosticPrivacy.TraceFailure("Settings change", ex));
            _message = "Perubahan pengaturan gagal. Nilai sebelumnya tetap digunakan.";
        }
        Refresh();
    }
    public void UpdateKey(string key) => Run(() => _services.Chat.UpdateKey(key));
    public void ClearLongTermMemory()
    {
        if (!CanClearLongTermMemory) return;
        Run(_services.Memory.Clear);
    }
    private void DeleteSelectedVoiceModel()
    {
        bool deleted = _services.SpeechToText.DeleteModel(VoiceModel);
        if (!deleted)
            throw new IOException("Model voice sedang digunakan atau tidak dapat dihapus.");
        Refresh();
    }
    private async Task PreviewVoiceAsync()
    {
        string voice = _services.Chat.Options.TextToSpeechVoice;
        TextToSpeechOptions options = new(voice, TextToSpeechRate, TextToSpeechVolume);
        await _services.TextToSpeech.SpeakAsync("Halo, saya Lu-Knight.", options, _lifetime.Token);
    }
    private void ChangeCompanion(
        CompanionSettings preferences)
    {
        if (preferences ==
            _services.Settings.Current.Companion)
        {
            return;
        }


        Run(
            () =>
            {
                AppSettings config =
                    SettingsService.Validate(
                        _services.Settings.Current with
                        {
                            Companion =
                                preferences
                        });

                _services.Settings.Update(
                    config);
            });
    }

    public bool ProactiveSuggestions
    {
        get =>
            _services.Settings
                .Current
                .Companion
                .Enabled;

        set
        {
            CompanionSettings current =
                _services.Settings
                    .Current
                    .Companion;

            if (current.Enabled == value)
                return;

            ChangeCompanion(
                current with
                {
                    Enabled =
                        value
                });
        }
    }


    public bool ProactiveSuggestionsAvailable =>
        CanEditChat &&
        UseApplicationContext;


    public bool ProactiveCategoryControlsEnabled =>
        ProactiveSuggestionsAvailable &&
        ProactiveSuggestions;

    public bool SuggestCoding
    {
        get =>
            _services.Settings
                .Current
                .Companion
                .Coding;

        set
        {
            CompanionSettings current =
                _services.Settings
                    .Current
                    .Companion;

            if (current.Coding == value)
                return;

            ChangeCompanion(
                current with
                {
                    Coding =
                        value
                });
        }
    }

    public bool SuggestBrowsing
    {
        get =>
            _services.Settings
                .Current
                .Companion
                .Browsing;

        set
        {
            CompanionSettings current =
                _services.Settings
                    .Current
                    .Companion;

            if (current.Browsing == value)
                return;

            ChangeCompanion(
                current with
                {
                    Browsing =
                        value
                });
        }
    }

    public bool SuggestCreative
    {
        get =>
            _services.Settings
                .Current
                .Companion
                .Creative;

        set
        {
            CompanionSettings current =
                _services.Settings
                    .Current
                    .Companion;

            if (current.Creative == value)
                return;

            ChangeCompanion(
                current with
                {
                    Creative =
                        value
                });
        }
    }

    public bool SuggestOffice
    {
        get =>
            _services.Settings
                .Current
                .Companion
                .Office;

        set
        {
            CompanionSettings current =
                _services.Settings
                    .Current
                    .Companion;

            if (current.Office == value)
                return;

            ChangeCompanion(
                current with
                {
                    Office =
                        value
                });
        }
    }

    public bool SuggestFiles
    {
        get =>
            _services.Settings
                .Current
                .Companion
                .Files;

        set
        {
            CompanionSettings current =
                _services.Settings
                    .Current
                    .Companion;

            if (current.Files == value)
                return;

            ChangeCompanion(
                current with
                {
                    Files =
                        value
                });
        }
    }

    public bool SuggestCommunication
    {
        get =>
            _services.Settings
                .Current
                .Companion
                .Communication;

        set
        {
            CompanionSettings current =
                _services.Settings
                    .Current
                    .Companion;

            if (current.Communication == value)
                return;

            ChangeCompanion(
                current with
                {
                    Communication =
                        value
                });
        }
    }

    public string CapabilitySummary
    {
        get
        {
            IReadOnlyList<
                AssistantCapabilityDescriptor>
                catalog =
                    _services.Capabilities
                        .Catalog;

            int skills =
                catalog.Count(
                    x =>
                        x.Kind ==
                        AssistantCapabilityKind.Skill);

            int actions =
                catalog.Count(
                    x =>
                        x.Kind ==
                        AssistantCapabilityKind.Action);

            int tools =
                catalog.Count(
                    x =>
                        x.Kind ==
                        AssistantCapabilityKind.Tool);

            return
                $"{skills} skills · " +
                $"{actions} actions · " +
                $"{tools} tools registered locally.";
        }
    }

    public bool FirstRunCompleted =>
        _services.Settings
            .Current
            .Onboarding
            .Completed;

    public bool CompleteFirstRun()
    {
        if (_disposed)
        {
            return false;
        }


        try
        {
            AppSettings updated =
                SettingsService.Validate(
                    FirstRunPolicy.Complete(
                        _services.Settings
                            .Current));


            bool saved =
                _services.Settings.Update(
                    updated);


            if (!saved)
            {
                _message =
                    "First-run belum dapat disimpan. " +
                    "Lu-Knight akan menampilkan panduan lagi saat startup berikutnya.";

                Refresh();

                return false;
            }


            _message =
                null;

            Refresh();

            return true;
        }
        catch (Exception ex)
            when (ex is
                ArgumentException or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException)
        {
            Debug.WriteLine(
                DiagnosticPrivacy.TraceFailure(
                    "First-run completion",
                    ex));


            _message =
                "First-run belum dapat disimpan. Pengaturan akses tidak diubah.";

            Refresh();

            return false;
        }
    }

    public void Refresh()
    {
        if (_disposed) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        foreach (var command in Commands) command.Refresh();
        RemoveKeyCommand.Refresh(); ClearCommand.Refresh(); LaterCommand.Refresh();
        DeleteVoiceModelCommand.Refresh();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var command in Commands) command.Completed -= Refresh;
        try { _lifetime.Cancel(); }
        catch (AggregateException ex)
        {
            Debug.WriteLine(DiagnosticPrivacy.TraceFailure("Settings shutdown", ex));
        }
        finally { _lifetime.Dispose(); }
    }
}

public sealed class AsyncSettingsCommand(Func<Task> execute, Func<bool> enabled) : ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public event Action? Completed;
    public bool CanExecute(object? parameter) => !_running && enabled();
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true; Refresh();
        try { await execute(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine(DiagnosticPrivacy.TraceFailure("Settings command", ex));
        }
        finally
        {
            _running = false;
            Refresh();
            Completed?.Invoke();
        }
    }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
