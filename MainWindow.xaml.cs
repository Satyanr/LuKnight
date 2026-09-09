using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using LuKnight.Services;
using LuKnight.Views;

namespace LuKnight;

public partial class MainWindow : Window
{
    private readonly IChatService _chatService;
    private readonly bool _usesGemini;

    private CancellationTokenSource? _requestCts;
    private bool _isSending;

    private Point _mouseDownPosition;
    private bool _leftMouseDown;
    private bool _dragStarted;

    public MainWindow()
    {
        InitializeComponent();

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
                "Halo! Aku Mimo. Gemini sudah dikonfigurasi dan siap dipakai.");
        }
        else
        {
            ChatPanelControl.SetStatus("Local mode • GEMINI_API_KEY belum ada", ChatStatus.Local);
            ChatPanelControl.AddAssistantMessage(
                "Halo! Aku Mimo. Aku sedang berjalan dalam local mode karena GEMINI_API_KEY belum terbaca.");
        }
    }

    private void Character_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _leftMouseDown = true;
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
            // Mouse bisa terlepas tepat saat ambang drag tercapai.
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
        ChatPopup.IsOpen = !ChatPopup.IsOpen;

        if (ChatPopup.IsOpen)
        {
            Dispatcher.BeginInvoke(ChatPanelControl.FocusInput);
        }
    }

    private async void ChatPanel_MessageSubmitted(string message)
    {
        if (_isSending)
            return;

        _isSending = true;

        using var requestCts = new CancellationTokenSource();
        _requestCts = requestCts;

        ChatPanelControl.AddUserMessage(message);
        ChatPanelControl.SetBusy(true);

        if (_usesGemini)
        {
            ChatPanelControl.SetStatus("Mimo sedang berpikir...", ChatStatus.Busy);
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
        base.OnClosed(e);
    }
}
