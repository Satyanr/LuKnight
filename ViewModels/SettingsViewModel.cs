using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using LuKnight.Views;
using LuKnight.Services;
using LuKnight.Behaviors;

namespace LuKnight.ViewModels;

public sealed record SettingsRuntime(bool AlwaysOnTop, bool Visible, string Renderer, string State,
    bool UsesGemini, string Model, ChatStatus ChatStatus, bool Sending, bool CanReset);

/// <summary>Session controls and read-only runtime diagnostics. Persistence belongs to a later phase.</summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly Func<SettingsRuntime> _read;
    private readonly Action<bool> _setTopmost, _setVisible;
    private SettingsRuntime _runtime;
    private readonly StartupService? _startup;
    private StartupStatus _startupStatus = new(false, false, false, "Startup tidak tersedia.");
    private string? _startupError;
    private string _selectedSection = "General";
    public event PropertyChangedEventHandler? PropertyChanged;
    public ProductSettingsViewModel? Product { get; set; }
    public SettingsCommand ResetPositionCommand { get; }
    public SettingsCommand ResetBehaviorCommand { get; }
    private readonly BehaviorSettings _behavior;
    private BehaviorOptions _lastBehavior;
    public sealed record Choice<T>(T Value, string Label);
    public Array ActivityChoices => Enum.GetValues<ActivityLevel>();
    public Array SpeedChoices => Enum.GetValues<MovementSpeed>();
    public Array NapChoices => Enum.GetValues<NapDuration>();
    public Choice<SleepDelay>[] SleepChoices { get; } =
    [new(SleepDelay.OneMinute, "1 min"), new(SleepDelay.TwoMinutes, "2 min"),
     new(SleepDelay.FiveMinutes, "5 min"), new(SleepDelay.Never, "Never")];
    public bool SleepControlsEnabled => AutonomousEnabled && AllowSleep;
    public bool AdventureControlsEnabled => AutonomousEnabled && ExploreWindows;
    private void SetBehavior(BehaviorOptions options)
    {
        if (options == _behavior.Current)
        {
            return;
        }

        try
        {
            _behavior.Apply(options);
            _lastBehavior = _behavior.Current;
            Changed(string.Empty);
        }
        catch (Exception ex)
            when (ex is ArgumentException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine(
                "[Lu-Knight][BehaviorSettings] " + ex);
        }
    }

    public bool AutonomousEnabled
    {
        get => _behavior.Current.Enabled;
        set
        {
            if (value == _behavior.Current.Enabled) return;
            SetBehavior(_behavior.Current with { Enabled = value });
        }
    }

    public ActivityLevel Activity
    {
        get => _behavior.Current.Activity;
        set
        {
            if (value == _behavior.Current.Activity) return;
            SetBehavior(_behavior.Current with { Activity = value });
        }
    }

    public MovementSpeed Speed
    {
        get => _behavior.Current.Speed;
        set
        {
            if (value == _behavior.Current.Speed) return;
            SetBehavior(_behavior.Current with { Speed = value });
        }
    }

    public bool AllowSleep
    {
        get => _behavior.Current.AllowSleep;
        set
        {
            if (value == _behavior.Current.AllowSleep) return;
            SetBehavior(_behavior.Current with { AllowSleep = value });
        }
    }

    public SleepDelay SleepAfter
    {
        get => _behavior.Current.SleepAfter;
        set
        {
            if (value == _behavior.Current.SleepAfter) return;
            SetBehavior(_behavior.Current with { SleepAfter = value });
        }
    }

    public NapDuration Nap
    {
        get => _behavior.Current.Nap;
        set
        {
            if (value == _behavior.Current.Nap) return;
            SetBehavior(_behavior.Current with { Nap = value });
        }
    }

    public bool ExploreWindows
    {
        get => _behavior.Current.ExploreWindows;
        set
        {
            if (value == _behavior.Current.ExploreWindows) return;
            SetBehavior(_behavior.Current with { ExploreWindows = value });
        }
    }

    public bool JumpBetweenWindows
    {
        get => _behavior.Current.JumpBetweenWindows;
        set
        {
            if (value == _behavior.Current.JumpBetweenWindows) return;
            SetBehavior(_behavior.Current with { JumpBetweenWindows = value });
        }
    }

    public bool HangingClimbing
    {
        get => _behavior.Current.HangingClimbing;
        set
        {
            if (value == _behavior.Current.HangingClimbing) return;
            SetBehavior(_behavior.Current with { HangingClimbing = value });
        }
    }

    public bool LookAtCursor
    {
        get => _behavior.Current.LookAtCursor;
        set
        {
            if (value == _behavior.Current.LookAtCursor) return;
            SetBehavior(_behavior.Current with { LookAtCursor = value });
        }
    }

    public bool ReactToCursor
    {
        get => _behavior.Current.ReactToCursor;
        set
        {
            if (value == _behavior.Current.ReactToCursor) return;
            SetBehavior(_behavior.Current with { ReactToCursor = value });
        }
    }

    public bool WakeAtCursor
    {
        get => _behavior.Current.WakeAtCursor;
        set
        {
            if (value == _behavior.Current.WakeAtCursor) return;
            SetBehavior(_behavior.Current with { WakeAtCursor = value });
        }
    }

    public SettingsViewModel(Func<SettingsRuntime> read, Action<bool> setTopmost, Action<bool> setVisible, Action reset, StartupService? startup = null, BehaviorSettings? behavior = null)
    {
        _behavior = behavior ?? new BehaviorSettings();
        _lastBehavior = _behavior.Current;
        ResetBehaviorCommand = new SettingsCommand(() => SetBehavior(new()), () => true);
        _read = read; _setTopmost = setTopmost; _setVisible = setVisible;
        _runtime = read();
        _startup = startup;
        if (_startup is not null) _startupStatus = _startup.ReadStatus();
        ResetPositionCommand = new SettingsCommand(() => { reset(); Refresh(); }, () => _runtime.CanReset);
    }

    public string SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (value is not ("General" or "Behavior" or "AI & Chat" or "About") || value == _selectedSection) return;
            _selectedSection = value; Changed();
        }
    }

    public bool AlwaysOnTop { get => _runtime.AlwaysOnTop; set { if (value != AlwaysOnTop) { _setTopmost(value); Refresh(); } } }
    public bool StartupAvailable => _startupStatus.Available;
    public bool StartWithWindows { get => _startupStatus.Enabled; set { if (value != StartWithWindows) ChangeStartup(() => { if (value) _startup!.Enable(); else _startup!.Disable(); }); } }
    public bool StartHidden { get => _startupStatus.StartHidden; set { if (value != StartHidden) ChangeStartup(() => _startup!.SetStartHidden(value)); } }
    public string StartupMessage => _startupError ?? _startupStatus.Message;
    private void ChangeStartup(Action change)
    {
        if (_startup is null) return;
        try { change(); _startupError = null; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException or InvalidOperationException)
        { _startupError = "Perubahan startup gagal: " + ex.Message; }
        _startupStatus = _startup.ReadStatus();
        Changed(string.Empty); // Read back actual state, including after a failed write.
    }
    public bool CharacterVisible { get => _runtime.Visible; set { if (value != CharacterVisible) { _setVisible(value); Refresh(); } } }
    public string Renderer => _runtime.Renderer;
    public string ApplicationStatus => _runtime.Visible ? $"Running · {_runtime.State}" : "Running · Hidden in tray";
    public string Provider => _runtime.UsesGemini ? "Gemini" : "Local fallback";
    public string Model => _runtime.UsesGemini ? _runtime.Model : "Tidak menggunakan model online";
    public string AiStatus => !_runtime.UsesGemini ? "Local fallback aktif"
        : _runtime.Sending ? "Sedang memproses pesan"
        : _runtime.ChatStatus == ChatStatus.Error ? "Permintaan AI terakhir gagal"
        : _runtime.ChatStatus == ChatStatus.Connected ? "Respons Gemini diterima"
        : "Gemini dikonfigurasi";
    public string ApiStatus => !_runtime.UsesGemini ? "API key belum dikonfigurasi; chat lokal tersedia."
        : _runtime.Sending ? "Permintaan API sedang berlangsung."
        : _runtime.ChatStatus == ChatStatus.Connected ? "Permintaan terakhir berhasil."
        : _runtime.ChatStatus == ChatStatus.Error ? "Permintaan terakhir gagal. Coba kembali melalui chat."
        : "API key tersedia. Koneksi belum terverifikasi.";

    private static Assembly ProductAssembly => typeof(SettingsViewModel).Assembly;
    public string Version => ProductAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? ProductAssembly.GetName().Version?.ToString() ?? "Development";
    public string Build => $"{ProductAssembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Development"} · {RuntimeInformation.ProcessArchitecture} · {ProductAssembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "unknown"}";
    public string Copyright => ProductAssembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "Lu-Knight contributors";
    public string RepositoryUrl => "https://github.com/Satyanr/LuKnight";

    public void Refresh()
    {
        Product?.Refresh();
        if (_lastBehavior != _behavior.Current) { _lastBehavior = _behavior.Current; Changed(string.Empty); }
        if (_startup is not null)
        {
            var status = _startup.ReadStatus();
            if (status != _startupStatus) { _startupStatus = status; Changed(string.Empty); }
        }
        var current = _read();
        if (current == _runtime) return;
        _runtime = current;
        Changed(string.Empty);
        ResetPositionCommand.Refresh();
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class SettingsCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
