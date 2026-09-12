using System.ComponentModel;
using System.Windows.Input;
using LuKnight.Models;
using LuKnight.Services;
using System.Diagnostics;

namespace LuKnight.ViewModels;

public sealed class ProductSettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppServices _services;
    private readonly Action _clear, _shutdown;
    private readonly Func<bool> _safeToInstall, _save;
    private readonly CancellationTokenSource _lifetime = new();
    private string? _message;
    private bool _deferred;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ProductSettingsViewModel(AppServices services, Action clear, Func<bool> safeToInstall, Func<bool> save, Action shutdown)
    {
        _services = services; _clear = clear; _safeToInstall = safeToInstall; _save = save; _shutdown = shutdown;
        TestCommand = new(async () => { _message = null; await services.Chat.TestConnection(_lifetime.Token); }, () => CanEditChat);
        CheckCommand = new(async () => { _deferred = false; await services.Updates.CheckForUpdate(false, _lifetime.Token); }, () => !services.Updates.IsBusy);
        DownloadCommand = new(async () => { _deferred = false; await services.Updates.DownloadUpdate(_lifetime.Token); }, () => CanDownload);
        InstallCommand = new(async () => { await services.Updates.InstallUpdate(_safeToInstall, _save, _shutdown, _lifetime.Token); }, () => CanInstall);
        foreach (var command in Commands) command.Completed += Refresh;
        RemoveKeyCommand = new(() => Run(services.Chat.RemoveKey), () => CanEditChat);
        ClearCommand = new(() => Run(_clear), () => CanEditChat);
        SaveCommand = new(() => { services.Settings.Save(); Refresh(); }, () => true);
        LaterCommand = new(() => { _deferred = true; Refresh(); }, () => !services.Updates.IsBusy);
    }
    public AsyncSettingsCommand TestCommand { get; }
    public AsyncSettingsCommand CheckCommand { get; }
    public AsyncSettingsCommand DownloadCommand { get; }
    public AsyncSettingsCommand InstallCommand { get; }
    public SettingsCommand RemoveKeyCommand { get; }
    public SettingsCommand ClearCommand { get; }
    public SettingsCommand SaveCommand { get; }
    public SettingsCommand LaterCommand { get; }
    private AsyncSettingsCommand[] Commands => [TestCommand, CheckCommand, DownloadCommand, InstallCommand];
    public Array Providers => Enum.GetValues<ChatProvider>();
    public Array Languages => Enum.GetValues<ChatLanguage>();
    public Array Lengths => Enum.GetValues<ResponseLength>();
    public Array Styles => Enum.GetValues<ResponseStyle>();
    public bool CanEditChat => !_services.Assistant.IsBusy;
    public string CredentialStatus => _services.Chat.CredentialStatus;
    public string ChatStatus => _message ?? _services.Chat.Status;
    public string SaveStatus => _services.Settings.Status;
    public string UpdateStatus => _deferred ? "Update ditunda. Lanjutkan melalui About kapan saja." : _services.Updates.Status;
    public bool CanCheck => !_services.Updates.IsBusy;
    public bool CanDownload => !_services.Updates.IsBusy && _services.Updates.Available is not null && _services.Updates.VerifiedInstaller is null;
    public bool CanInstall => !_services.Updates.IsBusy && _services.Updates.VerifiedInstaller is not null && _safeToInstall();
    public ChatProvider Provider
    {
        get => _services.Chat.Options.Provider;
        set
        {
            if (value == _services.Chat.Options.Provider) return;
            Change(_services.Chat.Options with { Provider = value });
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
        try
        {
            action();
            _message = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[Lu-Knight][Settings] " + ex);
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
    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        foreach (var command in Commands) command.Refresh();
        RemoveKeyCommand.Refresh(); ClearCommand.Refresh(); LaterCommand.Refresh();
    }
    public void Dispose() { _lifetime.Cancel(); foreach (var command in Commands) command.Completed -= Refresh; _lifetime.Dispose(); }
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
            Debug.WriteLine("[Lu-Knight][SettingsCommand] " + ex);
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
