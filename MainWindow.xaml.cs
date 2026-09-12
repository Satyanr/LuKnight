using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using LuKnight.Services;
using LuKnight.Views;
using LuKnight.Behaviors;
using LuKnight.Physics;
using LuKnight.ViewModels;
using LuKnight.Models;
using LuKnight.Assistant;

namespace LuKnight;

public partial class MainWindow : Window
{
    public BehaviorSettings BehaviorSettings { get; } = new();

    public SettingsRuntime GetSettingsRuntime() => new(Topmost, IsVisible,
        CharacterControl.RenderMode.ToString(), CharacterControl.CurrentState.ToString(),
        _usesGemini, _usesGemini ? Services.Chat.Options.Model : "", ChatPanelControl.CurrentStatus,
        _isSending, _behaviorController is not null);

    public void ResetCharacterPosition()
    {
        ShowFromTray();
        _leftMouseDown = false;
        _dragStarted = false;
        ReleaseMouseCapture();
        if (_activeTouchDevice is not null)
        {
            CharacterControl.ReleaseTouchCapture(_activeTouchDevice);
            _activeTouchDevice = null;
        }
        _physicsController?.ResetMotion();
        _behaviorController?.ResetToDesktop();
    }

    public AppServices Services { get; }
    private bool _usesGemini => Services.Chat.UsesGemini;
    public bool CanInstallUpdate => !_leftMouseDown && !_dragStarted && !_isSending && !Services.Assistant.IsBusy && !(_physicsController?.IsActive ?? false);
    public void SetAlwaysOnTop(bool value)
    {
        Topmost = value;
        Services.Settings.Update(Services.Settings.Current with { General = Services.Settings.Current.General with { AlwaysOnTop = value } });
    }
    public void ClearConversation()
    { Services.Assistant.ClearConversation(); ChatPanelControl.ClearConversation(); }
    public bool SaveSession()
    {
        var config = Services.Settings.Current;
        if (_behaviorController is not null && !(_physicsController?.IsActive ?? false))
        {
            var area = DesktopMonitorService.GetMonitorForWindow(this).WorkArea;
            var bounds = DesktopMonitorService.GetWindowBounds(this);
            config = config with { Mascot = new MascotPlacement(bounds.Left, area.Left, area.Top) };
        }
        return Services.Settings.Update(config with { General = config.General with { AlwaysOnTop = Topmost }, Behavior = BehaviorSettings.Current });
    }
    private void RestorePlacement()
    {
        if (Services.Settings.Current.Mascot is not { } saved) return;
        var monitors = DesktopMonitorService.GetAllMonitors();
        var monitor = monitors.FirstOrDefault(m => Math.Abs(m.WorkArea.Left - saved.MonitorLeft) < 1 && Math.Abs(m.WorkArea.Top - saved.MonitorTop) < 1);
        if (monitor.Handle == nint.Zero) monitor = DesktopMonitorService.GetMonitorForWindow(this);
        var bounds = DesktopMonitorService.GetWindowBounds(this);
        DesktopMonitorService.SetWindowPosition(this, Math.Clamp(saved.Left, monitor.WorkArea.Left, Math.Max(monitor.WorkArea.Left, monitor.WorkArea.Right - bounds.Width)),
            monitor.WorkArea.Bottom - CharacterGrounding.GetFootOffset(this, CharacterControl, bounds));
    }

    private BehaviorController? _behaviorController;

    private CancellationTokenSource? _requestCts;
    private bool _isSending;

    private Point _mouseDownPosition;
    private Point _mouseDownScreenPosition;
    private double _mouseDownTime;
    private bool _leftMouseDown;
    private bool _dragStarted;
    private TouchDevice? _activeTouchDevice;

    private CharacterPhysicsController?
        _physicsController;

    private void PhysicsController_Landed(
    double impactSpeed,
    bool wasShaken,
    nint? supportWindow)
    {
        _behaviorController?
            .SetSupportWindow(
                supportWindow);

        _behaviorController?
            .Resume(
                BehaviorPauseReason.Physics);


        CharacterControl
    .PlayLandingReaction(
        impactSpeed);


        if (wasShaken)
        {
            _behaviorController?
                .ReactDizzy();

            return;
        }


        if (impactSpeed > 650)
        {
            _behaviorController?
                .ReactConfused();
        }
        else
        {
            _behaviorController?
                .ReactHappy();
        }
    }

    private void BehaviorController_SupportLost()
    {
        _physicsController?
            .StartFallFromRest();
    }

    private void
    BehaviorController_SurfaceLaunchRequested(
        double velocityX,
        double velocityY,
        nint targetWindowHandle)
    {
        _physicsController?
            .StartFall(
                velocityX,
                velocityY,
                targetWindowHandle);
    }

    private void PhysicsController_Shaken()
    {
        _behaviorController?
            .ReactDizzy();
    }

    private void MainWindow_Loaded(
    object sender,
    RoutedEventArgs e)
    {
        if (_behaviorController is not null) return;
        _behaviorController =
            new BehaviorController(
                this,
                CharacterControl);

        _behaviorController.ApplySettings(BehaviorSettings.Current);
        _behaviorController.Start();
        RestorePlacement();
        _physicsController =
    new CharacterPhysicsController(
        this,
        CharacterControl);

        _physicsController.Landed +=
            PhysicsController_Landed;

        _physicsController.Shaken +=
            PhysicsController_Shaken;
        _behaviorController.SupportLost +=
            BehaviorController_SupportLost;

        _behaviorController.SurfaceLaunchRequested +=
            BehaviorController_SurfaceLaunchRequested;
    }

    public MainWindow() : this(null) { }
    public MainWindow(AppServices? services)
    {
        Services = services ?? new AppServices();
        InitializeComponent();
        Services.Context.Attach(CaptureAssistantContext);
        Topmost = Services.Settings.Current.General.AlwaysOnTop;
        BehaviorSettings.Apply(Services.Settings.Current.Behavior);
        BehaviorSettings.Changed += options =>
        {
            _behaviorController?.ApplySettings(options);
            Services.Settings.Update(Services.Settings.Current with { Behavior = options });
        };
        CharacterControl.AddHandler(PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(Character_PreviewMouseLeftButtonDown), true);
        CharacterControl.AddHandler(PreviewMouseMoveEvent,
            new MouseEventHandler(Character_PreviewMouseMove), true);
        CharacterControl.AddHandler(PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(Character_PreviewMouseLeftButtonUp), true);
        CharacterControl.AddHandler(PreviewTouchDownEvent,
            new EventHandler<TouchEventArgs>(Character_PreviewTouchDown), true);
        CharacterControl.AddHandler(PreviewTouchMoveEvent,
            new EventHandler<TouchEventArgs>(Character_PreviewTouchMove), true);
        CharacterControl.AddHandler(PreviewTouchUpEvent,
            new EventHandler<TouchEventArgs>(Character_PreviewTouchUp), true);
        CharacterControl.AddHandler(LostTouchCaptureEvent,
            new EventHandler<TouchEventArgs>(Character_LostTouchCapture), true);
        Loaded += MainWindow_Loaded;

        ChatPanelControl.MessageSubmitted += ChatPanel_MessageSubmitted;

        if (_usesGemini)
        {
            ChatPanelControl.SetStatus(
                $"Gemini siap • {Services.Chat.Options.Model}",
                ChatStatus.Ready);

            ChatPanelControl.AddAssistantMessage(
                "Halo! Aku Lu-Knight. Gemini sudah dikonfigurasi dan siap dipakai.");
        }
        else
        {
            ChatPanelControl.SetStatus("Local mode • GEMINI_API_KEY belum ada", ChatStatus.Local);
            ChatPanelControl.AddAssistantMessage(
                "Halo! Aku Lu-Knight. Mode lokal aktif. Kamu dapat mengatur Gemini melalui Settings ? AI & Chat.");
        }
    }

    private void Character_PreviewTouchDown(
        object? sender,
        TouchEventArgs e)
    {
        if (_activeTouchDevice is not null)
        {
            e.Handled = true;
            return;
        }

        _activeTouchDevice = e.TouchDevice;
        _leftMouseDown = true;
        _dragStarted = false;

        _behaviorController?.NotifyUserInteraction();
        _behaviorController?.Pause(BehaviorPauseReason.UserDrag);

        _mouseDownPosition = e.GetTouchPoint(this).Position;
        _mouseDownScreenPosition = PointToScreen(_mouseDownPosition);
        _mouseDownTime = System.Diagnostics.Stopwatch.GetTimestamp() /
            (double)System.Diagnostics.Stopwatch.Frequency;

        bool captured = CharacterControl.CaptureTouch(e.TouchDevice);
        if (!captured)
        {
            _activeTouchDevice = null;
            _leftMouseDown = false;
            _behaviorController?.Resume(BehaviorPauseReason.UserDrag);
        }
        else
        {
            _physicsController?.PrepareGrab(_mouseDownScreenPosition, _mouseDownTime);
        }

        e.Handled = true;
    }

    private void Character_PreviewTouchMove(
        object? sender,
        TouchEventArgs e)
    {
        if (_activeTouchDevice != e.TouchDevice || !_leftMouseDown)
            return;

        Point currentPosition = e.GetTouchPoint(this).Position;
        Point currentScreen = PointToScreen(currentPosition);
        _physicsController?.SamplePointer(currentScreen);

        double horizontalDistance = Math.Abs(currentPosition.X - _mouseDownPosition.X);
        double verticalDistance = Math.Abs(currentPosition.Y - _mouseDownPosition.Y);
        const double TouchDragThreshold = 8.0;
        bool movedEnough = horizontalDistance >= TouchDragThreshold ||
            verticalDistance >= TouchDragThreshold;

        if (!_dragStarted)
        {
            if (!movedEnough)
            {
                e.Handled = true;
                return;
            }

            if (ChatPopup.IsOpen)
            {
                ChatPopup.IsOpen = false;
                _behaviorController?.Resume(BehaviorPauseReason.Chat);
            }

            bool grabStarted = _physicsController?.BeginGrab(
                _mouseDownScreenPosition, _mouseDownTime) == true;
            if (!grabStarted)
            {
                _behaviorController?.Resume(BehaviorPauseReason.UserDrag);
                e.Handled = true;
                return;
            }

            _behaviorController?.ClearSupportWindow();
            _dragStarted = true;
        }

        e.Handled = true;
    }

    private void Character_PreviewTouchUp(
        object? sender,
        TouchEventArgs e)
    {
        if (_activeTouchDevice != e.TouchDevice)
            return;

        _leftMouseDown = false;
        CharacterControl.ReleaseTouchCapture(e.TouchDevice);
        _activeTouchDevice = null;

        if (_dragStarted)
        {
            _behaviorController?.Pause(BehaviorPauseReason.Physics);
            _behaviorController?.Resume(BehaviorPauseReason.UserDrag);
            _physicsController?.EndGrab();
            _dragStarted = false;
        }
        else
        {
            _physicsController?.CancelPreparedGrab();
            _behaviorController?.Resume(BehaviorPauseReason.UserDrag);
            _behaviorController?.ReactToClick();
            ToggleChat();
        }

        e.Handled = true;
    }

    private void Character_LostTouchCapture(
        object? sender,
        TouchEventArgs e)
    {
        if (_activeTouchDevice != e.TouchDevice)
            return;

        _activeTouchDevice = null;
        if (!_leftMouseDown)
            return;

        _leftMouseDown = false;
        if (_dragStarted)
        {
            _dragStarted = false;
            _behaviorController?.Pause(BehaviorPauseReason.Physics);
            _physicsController?.EndGrab();
        }
        else
        {
            _physicsController?.CancelPreparedGrab();
        }

        _behaviorController?.Resume(BehaviorPauseReason.UserDrag);
    }

    private void Character_PreviewMouseLeftButtonDown(
    object sender,
    MouseButtonEventArgs e)
    {
        _leftMouseDown = true;
        _dragStarted = false;

        _behaviorController?
            .NotifyUserInteraction();

        _behaviorController?
            .Pause(
        BehaviorPauseReason.UserDrag);

        _mouseDownPosition =
            e.GetPosition(this);
        _mouseDownScreenPosition = PointToScreen(_mouseDownPosition);
        _mouseDownTime = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

        if (!CaptureMouse())
        {
            _leftMouseDown = false;
            _behaviorController?.Resume(BehaviorPauseReason.UserDrag);
        }
        else _physicsController?.PrepareGrab(_mouseDownScreenPosition, _mouseDownTime);

        e.Handled = true;
    }

    private void Character_PreviewMouseMove(
     object sender,
     MouseEventArgs e)
    {
        _behaviorController?.ObservePointer(e.GetPosition(CharacterControl));
        if (!_leftMouseDown)
            return;

        if (e.LeftButton !=
            MouseButtonState.Pressed)
        {
            return;
        }

        Point currentPosition =
            e.GetPosition(this);
        _physicsController?.SamplePointer(PointToScreen(currentPosition));

        double horizontalDistance =
            Math.Abs(
                currentPosition.X -
                _mouseDownPosition.X);

        double verticalDistance =
            Math.Abs(
                currentPosition.Y -
                _mouseDownPosition.Y);

        bool movedEnough =
            horizontalDistance >=
                SystemParameters
                    .MinimumHorizontalDragDistance
            ||
            verticalDistance >=
                SystemParameters
                    .MinimumVerticalDragDistance;


        if (!_dragStarted)
        {
            if (!movedEnough)
                return;

            if (ChatPopup.IsOpen)
            {
                ChatPopup.IsOpen =
                    false;


                _behaviorController?
                    .Resume(
                        BehaviorPauseReason.Chat);
            }

            bool grabStarted =
                _physicsController?
                    .BeginGrab(_mouseDownScreenPosition, _mouseDownTime) == true;

            if (!grabStarted)
            {
                _behaviorController?
                    .Resume(
                        BehaviorPauseReason.UserDrag);

                return;
            }

            _behaviorController?
                .ClearSupportWindow();

            _dragStarted = true;
        }


        // Actual window movement remains on the render clock.

        e.Handled = true;
    }

    private void Character_PreviewMouseLeftButtonUp(
     object sender,
     MouseButtonEventArgs e)
    {
        if (!_leftMouseDown)
            return;

        _leftMouseDown = false;
        ReleaseMouseCapture();

        if (_dragStarted)
        {
            // Setelah tangan user lepas,
            // ownership pindah dari UserDrag
            // ke Physics.
            _behaviorController?
                .Pause(
                    BehaviorPauseReason.Physics);


            _behaviorController?
                .Resume(
                    BehaviorPauseReason.UserDrag);


            _physicsController?
                .EndGrab();


            _dragStarted = false;
        }
        else
        {
            _physicsController?.CancelPreparedGrab();
            _behaviorController?
                .Resume(
                    BehaviorPauseReason.UserDrag);


            _behaviorController?
                .ReactToClick();


            ToggleChat();
        }

        e.Handled = true;
    }
    private void Character_MouseEnter(object sender, MouseEventArgs e) =>
        _behaviorController?.ObservePointer(e.GetPosition(CharacterControl));

    private void Character_MouseLeave(object sender, MouseEventArgs e) =>
        _behaviorController?.PointerLeft();

    private void Character_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_leftMouseDown) return;
        _leftMouseDown = false;
        if (_dragStarted)
        {
            _dragStarted = false;
            _behaviorController?.Pause(BehaviorPauseReason.Physics);
            _physicsController?.EndGrab();
        }
        else _physicsController?.CancelPreparedGrab();
        _behaviorController?.Resume(BehaviorPauseReason.UserDrag);
    }

    private void ToggleChat()
    {
        _behaviorController?
            .NotifyUserInteraction();

        ChatPopup.IsOpen =
            !ChatPopup.IsOpen;

        if (ChatPopup.IsOpen)
        {
            _behaviorController?
                .Pause(
                    BehaviorPauseReason.Chat);


            Dispatcher.BeginInvoke(
                ChatPanelControl.FocusInput);
        }
        else
        {
            _behaviorController?
                .Resume(
                    BehaviorPauseReason.Chat);
        }
    }

    private AssistantRuntimeContext CaptureAssistantContext()
    {
        BehaviorOptions behavior = BehaviorSettings.Current;
        return new AssistantRuntimeContext(
            RuntimeAvailable: true,
            LocalTime: DateTimeOffset.Now,
            TimeZoneId: TimeZoneInfo.Local.Id,
            CharacterVisible: IsVisible,
            ChatOpen: ChatPopup.IsOpen,
            CharacterState: CharacterControl.CurrentState.ToString(),
            CharacterMood: CharacterControl.CurrentMood.ToString(),
            AutonomousBehaviorEnabled: behavior.Enabled,
            Activity: behavior.Activity.ToString(),
            MovementSpeed: behavior.Speed.ToString());
    }

    private async void ChatPanel_MessageSubmitted(string message)
    {
        if (_isSending)
            return;

        _isSending = true;

        _behaviorController?
            .NotifyUserInteraction();

        using var requestCts = new CancellationTokenSource();
        _requestCts = requestCts;

        ChatPanelControl.AddUserMessage(message);
        ChatPanelControl.SetBusy(true);
        _behaviorController?
            .SetThinking(true);

        if (_usesGemini)
        {
            ChatPanelControl.SetStatus("Lu-Knight sedang berpikir...", ChatStatus.Busy);
        }

        try
        {
            AssistantReply reply =
                await Services.Assistant.SendAsync(
                    new AssistantRequest(message, AssistantInputSource.Chat),
                    requestCts.Token);

            _behaviorController?
                .SetThinking(false);

            _behaviorController?
                .ReactHappy();

            ChatPanelControl.AddAssistantMessage(reply.Text);

            ChatPanelControl.SetStatus(Services.Chat.Status,
                reply.Backend == AssistantBackend.Gemini ? ChatStatus.Connected : ChatStatus.Local);
        }
        catch (OperationCanceledException)
        {
            _behaviorController?
                .SetThinking(false);
            ChatPanelControl.AddAssistantMessage("Permintaan dibatalkan.");

            if (_usesGemini)
                ChatPanelControl.SetStatus("Gemini • dibatalkan", ChatStatus.Ready);
        }
        catch (Exception ex)
        {
            _behaviorController?
                .SetThinking(false);

            _behaviorController?
                .ReactConfused();
            ChatPanelControl.AddAssistantMessage($"Terjadi kesalahan: {ex.Message}");

            ChatPanelControl.SetStatus(
                _usesGemini ? "Gemini • error" : "Local mode • error",
                ChatStatus.Error);
        }
        finally
        {
            ChatPanelControl.SetBusy(false);

            if (ReferenceEquals(_requestCts, requestCts))
                _requestCts = null;

            _isSending = false;
        }
    }

    public void ShowFromTray()
    {
        WindowState = WindowState.Normal;
        if (!IsVisible)
        {
            Show();
        }

        _physicsController?.SetSuspended(false);
        _behaviorController?
            .Resume(
                BehaviorPauseReason.Hidden);


        Activate();
    }

    public void HideToTray()
    {
        // Acquire Hidden first: closing chat/releasing capture must not resume movement.
        _behaviorController?.Pause(BehaviorPauseReason.Hidden);
        if (IsMouseCaptured) ReleaseMouseCapture();
        _physicsController?.SetSuspended(true);
        // Popup adalah window terpisah.
        // Tutup supaya tidak tertinggal
        // ketika mascot disembunyikan.
        if (ChatPopup.IsOpen)
        {
            ChatPopup.IsOpen =
                false;


            _behaviorController?
                .Resume(
                    BehaviorPauseReason.Chat);
        }


        Hide();
    }

    public void OpenChatFromTray()
    {
        ShowFromTray();


        _behaviorController?
            .NotifyUserInteraction();


        if (!ChatPopup.IsOpen)
        {
            ChatPopup.IsOpen =
                true;


            _behaviorController?
                .Pause(
                    BehaviorPauseReason.Chat);
        }


        Dispatcher.BeginInvoke(
            ChatPanelControl.FocusInput);
    }

    protected override void OnClosed(
    EventArgs e)
    {
        Services.Context.Detach();
        _requestCts?.Cancel();

        if (_physicsController is not null)
        {
            _physicsController.Shaken -=
                PhysicsController_Shaken;

            _physicsController.Landed -=
                PhysicsController_Landed;

            _physicsController.Dispose();
        }
        if (_behaviorController is not null)
        {
            _behaviorController.SupportLost -=
                BehaviorController_SupportLost;

            _behaviorController.SurfaceLaunchRequested -=
                BehaviorController_SurfaceLaunchRequested;
        }
        _behaviorController?.Dispose();

        base.OnClosed(e);
    }
}
