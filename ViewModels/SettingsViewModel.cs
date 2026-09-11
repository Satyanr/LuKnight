using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using LuKnight.Views;

namespace LuKnight.ViewModels;

public sealed record SettingsRuntime(bool AlwaysOnTop, bool Visible, string Renderer, string State,
    bool UsesGemini, string Model, ChatStatus ChatStatus, bool Sending, bool CanReset);

/// <summary>Session controls and read-only runtime diagnostics. Persistence belongs to a later phase.</summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly Func<SettingsRuntime> _read;
    private readonly Action<bool> _setTopmost, _setVisible;
    private SettingsRuntime _runtime;
    private string _selectedSection = "General";
    public event PropertyChangedEventHandler? PropertyChanged;
    public SettingsCommand ResetPositionCommand { get; }

    public SettingsViewModel(Func<SettingsRuntime> read, Action<bool> setTopmost, Action<bool> setVisible, Action reset)
    {
        _read = read; _setTopmost = setTopmost; _setVisible = setVisible;
        _runtime = read();
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
