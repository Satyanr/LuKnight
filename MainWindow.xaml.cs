using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using LuKnight.Services;
using LuKnight.Views;
using LuKnight.Behaviors;

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

    private void MainWindow_Loaded(
    object sender,
    RoutedEventArgs e)
    {
        _behaviorController =
            new BehaviorController(
                this,
                CharacterControl);

        _behaviorController.Start();
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

        _behaviorController?
            .NotifyUserInteraction();

        _behaviorController?
            .Pause();
        _dragStarted = false;
        _mouseDownPosition = e.GetPosition(this);

        CharacterControl.CaptureMouse();
        e.Handled = true;
    }

    private void Character_PreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_leftMouseDown)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        Point currentPosition = e.GetPosition(this);

        double horizontalDistance =
            Math.Abs(currentPosition.X - _mouseDownPosition.X);

        double verticalDistance =
            Math.Abs(currentPosition.Y - _mouseDownPosition.Y);

        bool movedEnough =
            horizontalDistance >= SystemParameters.MinimumHorizontalDragDistance ||
            verticalDistance >= SystemParameters.MinimumVerticalDragDistance;

        if (!movedEnough)
            return;

        _dragStarted = true;
        _leftMouseDown = false;

        CharacterControl.ReleaseMouseCapture();
        ChatPopup.IsOpen = false;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Mouse bisa terlepas tepat saat
            // ambang drag tercapai.
        }
        finally
        {
            _behaviorController?.Resume();
        }

        e.Handled = true;
    }

    private void Character_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_leftMouseDown)
            return;

        CharacterControl.ReleaseMouseCapture();
        _leftMouseDown = false;

        if (!_dragStarted)
            ToggleChat();

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
            _behaviorController?.Pause();

            Dispatcher.BeginInvoke(
                ChatPanelControl.FocusInput);
        }
        else
        {
            _behaviorController?.Resume();
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

            ChatPanelControl.AddAssistantMessage(reply);

            ChatPanelControl.SetStatus(
                _usesGemini
                    ? $"Gemini terhubung • {GeminiChatService.ModelName}"
                    : _chatService.DisplayName,
                _usesGemini ? ChatStatus.Connected : ChatStatus.Local);
        }
        catch (OperationCanceledException)
        {
            ChatPanelControl.AddAssistantMessage("Permintaan dibatalkan.");

            if (_usesGemini)
                ChatPanelControl.SetStatus("Gemini • dibatalkan", ChatStatus.Ready);
        }
        catch (Exception ex)
        {
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

    protected override void OnClosed(EventArgs e)
    {
        _requestCts?.Cancel();
        _behaviorController?.Dispose();
        base.OnClosed(e);
    }
}
