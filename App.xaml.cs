using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;
using LuKnight.Views;

namespace LuKnight;

public partial class App : Application
{
    private TrayIconService? _tray;
    private MainWindow? _character;
    private StartupService _startup = new();
    private AppServices? _services;
    private Mutex? _instance;
    private EventWaitHandle? _showRequest;
    private DispatcherTimer? _instanceTimer;
    private DispatcherTimer?
        _scheduleTimer;

    private readonly HashSet<Guid>
        _scheduleNotifiedThisSession =
            [];
    private DispatcherTimer?
        _companionTimer;


    private readonly
        CompanionSuggestionGate
        _companionGate =
            new();


    private readonly Dictionary<
        string,
        CompanionSuggestionCandidate>
        _companionSuggestions =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    public void ExitForUpdate() => ExitApplication();

    private SettingsWindow?
    _settingsWindow;
    private bool _isExiting;

    private void OpenSettingsWindow()
    {
        if (_isExiting || _character is null) return;
        var settings = GetOrCreateSettingsWindow();
        settings.Topmost = true;
        if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
        if (!settings.IsVisible) settings.Show();
        settings.Activate();
    }

    private SettingsWindow GetOrCreateSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow =
                new SettingsWindow(_character ?? throw new InvalidOperationException("Character is not initialized."), _startup);


            _settingsWindow.Closed +=
                SettingsWindow_Closed;
        }
        return _settingsWindow;
    }


    private void SettingsWindow_Closed(
        object? sender,
        EventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Closed -=
                SettingsWindow_Closed;
        }


        _settingsWindow =
            null;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string? wait = e.Args.FirstOrDefault(a => a.StartsWith("--wait-for-pid="));
        if (wait is not null && int.TryParse(wait.Split('=')[1], out int pid))
        { try { using var old = Process.GetProcessById(pid); old.WaitForExit(15000); } catch (ArgumentException) { } }
        _instance = new Mutex(false, @"Local\LuKnight-App", out bool firstInstance);
        _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\LuKnight-Show");
        if (!firstInstance) { _showRequest.Set(); Shutdown(); return; }
        string configPath = Path.Combine(SettingsService.UserDirectory, "settings.json");
        var config = new SettingsService(configPath); config.Load();
        if (!File.Exists(configPath))
        {
            try { config.Update(config.Current with { General = config.Current.General with { StartHidden = new RegistryStartupStore().ReadStartHidden() } }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        string memoryPath = Path.Combine(SettingsService.UserDirectory, "memory.json");
        var memory = new MemoryService(memoryPath);
        memory.Load();
        _services = new AppServices(config, memory: memory);
        _startup = new StartupService(new PersistentStartupStore(config), Environment.ProcessPath ?? "", Assembly.GetExecutingAssembly().Location, File.Exists);
        try { _startup.Validate(); }
        catch (Exception ex) { Trace.WriteLine($"[Lu-Knight] Startup registration could not be repaired: {ex.Message}"); }
        _character = new MainWindow(_services);
        MainWindow = _character;
        _character.Closing += Character_Closing;
        _character.IsVisibleChanged += Character_VisibilityChanged;
        try
        {
            _tray =
                new TrayIconService(
                    ToggleCharacterVisibility,
                    _character.ShowFromTray,
                    _character.OpenChatFromTray,
                    OpenSettingsWindow,
                    RestartApplication,
                    ExitApplication,
                    OpenScheduledReminderFromTray,
                    OpenCompanionSuggestionFromTray);
            _tray.Show();
        }
        catch (Exception ex)
        {
            if (_settingsWindow is not null)
            {
                _settingsWindow.Closed -=
                    SettingsWindow_Closed;


                _settingsWindow =
                    null;
            }

            _tray?.Dispose();
            _tray = null;
            // A failed shell icon must never leave an unreachable application.
            _character.ShowInTaskbar = true;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            Trace.WriteLine($"[Lu-Knight] Tray unavailable: {ex}");
            MessageBox.Show("System tray tidak dapat dibuat. Lu-Knight tetap tersedia di taskbar.", "Lu-Knight", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        // Do not Show/Hide: creating a hidden startup window must not flash or steal focus.
        if (!StartupService.ShouldStartHidden(e.Args, _startup.ReadStatus().StartHidden, _tray is not null))
        {
            _character.Show();
        }
        _tray?.SetCharacterVisible(_character.IsVisible);
        _scheduleTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromSeconds(
                        30)
            };

        _scheduleTimer.Tick +=
            ScheduleTimer_Tick;

        _scheduleTimer.Start();


        // Check once immediately.
        CheckDueScheduleReminder();
        _companionTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromSeconds(
                        30)
            };

        _companionTimer.Tick +=
            CompanionTimer_Tick;

        _companionTimer.Start();


        // Seed the dwell timer.
        // This cannot present immediately because
        // StabilityDelay has not elapsed.
        CheckCompanionSuggestion();
        _instanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _instanceTimer.Tick += (_, _) => { if (_showRequest.WaitOne(0)) _character.ShowFromTray(); };
        _instanceTimer.Start();
        _ = CheckUpdatesAtStartup();
    }
    private void CheckDueScheduleReminder()
    {
        if (_isExiting ||
            _services is null ||
            _tray is null)
        {
            return;
        }


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        ScheduledSkill? due =
            _services
                .Scheduler
                .GetReminderCandidates(
                    now)
                .FirstOrDefault(
                    item =>
                        !_scheduleNotifiedThisSession
                            .Contains(
                                item.Id));


        if (due is null)
            return;


        try
        {
            bool presented =
                _tray.NotifyReminder(
                    due.Id,
                    due.DisplayName);

            if (!presented)
                return;


            _scheduleNotifiedThisSession.Add(
                due.Id);


            if (!_services
                    .Scheduler
                    .MarkPresented(
                        due.Id,
                        now,
                        out string error))
            {
                Trace.WriteLine(
                    $"[Lu-Knight] Reminder '{due.Id}' " +
                    $"was shown but presentation state could not be persisted: {error}");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[Lu-Knight] Schedule reminder failed: {ex.Message}");
        }
    }

    private void ScheduleTimer_Tick(
        object? sender,
        EventArgs e)
    {
        CheckDueScheduleReminder();
    }

    private async Task CheckUpdatesAtStartup()
    {
        try
        {
            if (_services is not null && await _services.Updates.CheckForUpdate(true, _lifetime.Token) is { } update && !_isExiting)
                _tray?.NotifyUpdate(update.Version);
        }
        catch (OperationCanceledException) { }
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
        if (_isExiting) return;
        if (_tray is null) { _character?.SaveSession(); return; }
        e.Cancel = true;
        _character?.HideToTray();
    }

    private void RestartApplication()
    {
        try
        {
            var start = ApplicationRestart.CreateStartInfo(Environment.ProcessPath ?? "",
                Assembly.GetExecutingAssembly().Location, Environment.GetCommandLineArgs().Skip(1)
                    .Where(argument => !argument.Equals("--startup", StringComparison.OrdinalIgnoreCase) && !argument.StartsWith("--wait-for-pid="))
                    .Append("--wait-for-pid=" + Environment.ProcessId));
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
        _settingsWindow?.SavePlacement();
        _character?.SaveSession();
        _isExiting = true;
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (!e.Cancel) { _settingsWindow?.SavePlacement(); _character?.SaveSession(); _isExiting = true; }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_scheduleTimer is not null)
        {
            _scheduleTimer.Stop();

            _scheduleTimer.Tick -=
                ScheduleTimer_Tick;

            _scheduleTimer =
                null;
        }
        if (_companionTimer is not null)
        {
            _companionTimer.Stop();

            _companionTimer.Tick -=
                CompanionTimer_Tick;

            _companionTimer =
                null;
        }
        _lifetime.Cancel(); _instanceTimer?.Stop(); _showRequest?.Dispose(); _instance?.Dispose();
        if (_settingsWindow is not null)
        {
            _settingsWindow.Closed -= SettingsWindow_Closed;
            _settingsWindow = null;
        }
        if (_character is not null)
        {
            _character.Closing -= Character_Closing;
            _character.IsVisibleChanged -= Character_VisibilityChanged;
        }
        _tray?.Dispose();
        _tray = null;
        base.OnExit(e);
    }
    private void OpenScheduledReminderFromTray(
        Guid scheduleId)
    {
        if (_isExiting ||
            _character is null ||
            _services is null)
        {
            return;
        }

        _character.OpenScheduledReminderFromTray(
            scheduleId);
    }
    private void CheckCompanionSuggestion()
    {
        if (_isExiting ||
            _services is null ||
            _character is null ||
            _tray is null)
        {
            return;
        }


        CompanionSettings preferences = _services.Settings.Current.Companion;
        if (!preferences.Enabled || !_services.Chat.Options.UseApplicationContext)
        {
            _companionGate.ResetObservation();
            _companionSuggestions.Clear();
            _tray.CancelCompanionNotification();
            return;
        }

        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        //
        // Higher-priority activity suppresses
        // proactive suggestions.
        //

        if (_services.Assistant.IsBusy ||
            _services
                .Scheduler
                .GetReminderCandidates(
                    now)
                .Count >
            0)
        {
            _companionGate
                .ResetObservation();

            return;
        }


        AssistantRuntimeContext context =
            _services
                .Context
                .Capture();


        CompanionSuggestionCandidate?
            candidate =
                _services
                    .CompanionAdvisor
                    .Evaluate(
                        context,
                        preferences,
                        _services.Capabilities);


        CompanionSuggestionCandidate?
            ready =
                _companionGate
                    .Observe(
                        candidate,
                        now);


        if (ready is null)
            return;


        //
        // Notification presentation can fail
        // because a reminder/update balloon
        // currently owns the tray.
        //
        // Do not consume rate limit unless
        // presentation really happened.
        //

        if (!_tray.NotifyCompanion(
                ready.Key,
                ready.Message))
        {
            return;
        }


        if (!_companionGate
                .MarkPresented(
                    ready,
                    now))
        {
            return;
        }


        _companionSuggestions[
            ready.Key] =
                ready;
    }

    private void CompanionTimer_Tick(
        object? sender,
        EventArgs e)
    {
        CheckCompanionSuggestion();
    }

    private void OpenCompanionSuggestionFromTray(
        string key)
    {
        if (_isExiting ||
            _character is null)
        {
            return;
        }


        if (!_companionSuggestions
                .Remove(
                    key,
                    out CompanionSuggestionCandidate?
                        candidate))
        {
            return;
        }


        if (_services is null ||
            _services.CompanionAdvisor.Evaluate(_services.Context.Capture(),
                _services.Settings.Current.Companion, _services.Capabilities) is not { } current ||
            !string.Equals(current.Key, candidate.Key, StringComparison.OrdinalIgnoreCase))
            return;

        _character
            .OpenCompanionSuggestionFromTray(
                candidate);
    }
}
