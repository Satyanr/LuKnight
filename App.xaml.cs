using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using LuKnight.Services;

namespace LuKnight;

public partial class App : Application
{
    private TrayIconService? _tray;
    private MainWindow? _character;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _character = new MainWindow();
        MainWindow = _character;
        _character.Closing += Character_Closing;
        _character.IsVisibleChanged += Character_VisibilityChanged;
        try
        {
            _tray = new TrayIconService(ToggleCharacterVisibility, _character.ShowFromTray,
                _character.OpenChatFromTray, RestartApplication, ExitApplication);
            _tray.Show();
        }
        catch (Exception ex)
        {
            _tray?.Dispose();
            _tray = null;
            // A failed shell icon must never leave an unreachable application.
            _character.ShowInTaskbar = true;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            Trace.WriteLine($"[Lu-Knight] Tray unavailable: {ex}");
            MessageBox.Show("System tray tidak dapat dibuat. Lu-Knight tetap tersedia di taskbar.", "Lu-Knight", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _character.Show();
    }

    private void ToggleCharacterVisibility()
    {
        if (_character is null) return;
        if (_character.IsVisible) _character.HideToTray();
        else _character.ShowFromTray();
    }

    private void Character_VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _tray?.SetCharacterVisible(_character?.IsVisible == true);

    private void Character_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting || _tray is null) return;
        e.Cancel = true;
        _character?.HideToTray();
    }

    private void RestartApplication()
    {
        try
        {
            var start = ApplicationRestart.CreateStartInfo(Environment.ProcessPath ?? "",
                Assembly.GetExecutingAssembly().Location, Environment.GetCommandLineArgs().Skip(1));
            using var process = Process.Start(start);
            if (process is null) throw new InvalidOperationException("Proses baru tidak dapat dijalankan.");
            ExitApplication();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Restart gagal:\n{ex.Message}", "Lu-Knight", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExitApplication()
    {
        if (_isExiting) return;
        _isExiting = true;
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (!e.Cancel) _isExiting = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_character is not null)
        {
            _character.Closing -= Character_Closing;
            _character.IsVisibleChanged -= Character_VisibilityChanged;
        }
        _tray?.Dispose();
        _tray = null;
        base.OnExit(e);
    }
}
