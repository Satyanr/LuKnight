using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using LuKnight.Services;
using LuKnight.Views;
using LuKnight.Behaviors;
using LuKnight.Physics;

namespace LuKnight;

public partial class MainWindow : Window
{
    private readonly IChatService _chatService;
    private readonly bool _usesGemini;

    private BehaviorController? _behaviorController;

    private CancellationTokenSource? _requestCts;
    private bool _isSending;

    private Point _mouseDownPosition;
    private bool _leftMouseDown;
    private bool _dragStarted;

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
        _behaviorController =
            new BehaviorController(
                this,
                CharacterControl);

        _behaviorController.Start();
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

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;

        string? geminiApiKey =
            Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        _usesGemini = !string.IsNullOrWhiteSpace(geminiApiKey);

        _chatService = _usesGemini
            ? new GeminiChatService()
            : new LocalChatService();

        ChatPanelControl.MessageSubmitted += ChatPanel_MessageSubmitted;

        if (_usesGemini)
        {
            ChatPanelControl.SetStatus(
                $"Gemini siap • {GeminiChatService.ModelName}",
                ChatStatus.Ready);

            ChatPanelControl.AddAssistantMessage(
                "Halo! Aku Lu-Knight. Gemini sudah dikonfigurasi dan siap dipakai.");
        }
        else
        {
            ChatPanelControl.SetStatus("Local mode • GEMINI_API_KEY belum ada", ChatStatus.Local);
            ChatPanelControl.AddAssistantMessage(
                "Halo! Aku Lu-Knight. Aku sedang berjalan dalam local mode karena GEMINI_API_KEY belum terbaca.");
        }
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

        CharacterControl.CaptureMouse();

        e.Handled = true;
    }

    private void Character_PreviewMouseMove(
     object sender,
     MouseEventArgs e)
    {
        if (!_leftMouseDown)
            return;

        if (e.LeftButton !=
            MouseButtonState.Pressed)
        {
            return;
        }

        Point currentPosition =
            e.GetPosition(this);

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
                    .BeginGrab() == true;

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


        _physicsController?
            .UpdateGrab();

        e.Handled = true;
    }

    private void Character_PreviewMouseLeftButtonUp(
     object sender,
     MouseButtonEventArgs e)
    {
        if (!_leftMouseDown)
            return;

        CharacterControl
            .ReleaseMouseCapture();

        _leftMouseDown = false;

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
            _behaviorController?
                .Resume(
                    BehaviorPauseReason.UserDrag);


            _behaviorController?
                .ReactToClick();


            ToggleChat();
        }

        e.Handled = true;
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
            string reply =
                await _chatService.SendMessageAsync(
                    message,
                    requestCts.Token);

            _behaviorController?
                .SetThinking(false);

            _behaviorController?
                .ReactHappy();

            ChatPanelControl.AddAssistantMessage(reply);

            ChatPanelControl.SetStatus(
                _usesGemini
                    ? $"Gemini terhubung • {GeminiChatService.ModelName}"
                    : _chatService.DisplayName,
                _usesGemini ? ChatStatus.Connected : ChatStatus.Local);
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

    protected override void OnClosed(
    EventArgs e)
    {
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
