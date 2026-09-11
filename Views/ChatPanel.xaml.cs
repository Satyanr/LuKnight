using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LuKnight.Views;

public enum ChatStatus
{
    Local,
    Ready,
    Busy,
    Connected,
    Error
}

public partial class ChatPanel : UserControl
{
    public event Action<string>? MessageSubmitted;
    public ChatStatus CurrentStatus { get; private set; } = ChatStatus.Local;

    public ChatPanel()
    {
        InitializeComponent();
    }

    private void SendButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SubmitMessage();
    }

    private void MessageInput_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            SubmitMessage();
            e.Handled = true;
        }
    }

    private void SubmitMessage()
    {
        if (!MessageInput.IsEnabled)
            return;

        string message = MessageInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(message))
            return;

        MessageInput.Clear();
        MessageSubmitted?.Invoke(message);
    }

    public void AddUserMessage(string message)
    {
        AddMessage(
            message,
            HorizontalAlignment.Right,
            new SolidColorBrush(Color.FromRgb(225, 236, 255)));
    }

    public void AddAssistantMessage(string message)
    {
        AddMessage(
            message,
            HorizontalAlignment.Left,
            new SolidColorBrush(Color.FromRgb(240, 236, 228)));
    }

    public void SetBusy(bool isBusy)
    {
        MessageInput.IsEnabled = !isBusy;
        SendButton.IsEnabled = !isBusy;
        SendButton.Content = isBusy ? "..." : "Kirim";

        if (!isBusy)
            FocusInput();
    }

    public void SetStatus(string text, ChatStatus status)
    {
        CurrentStatus = status;
        StatusText.Text = text;

        StatusDot.Fill = status switch
        {
            ChatStatus.Connected =>
                new SolidColorBrush(Color.FromRgb(84, 166, 108)),

            ChatStatus.Ready =>
                new SolidColorBrush(Color.FromRgb(91, 135, 197)),

            ChatStatus.Busy =>
                new SolidColorBrush(Color.FromRgb(220, 163, 66)),

            ChatStatus.Error =>
                new SolidColorBrush(Color.FromRgb(194, 87, 87)),

            _ =>
                new SolidColorBrush(Color.FromRgb(150, 144, 136))
        };
    }

    private void AddMessage(
        string message,
        HorizontalAlignment alignment,
        Brush background)
    {
        TextBlock text = new()
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(64, 58, 52))
        };

        Border bubble = new()
        {
            Child = text,
            Background = background,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(2, 3, 2, 3),
            MaxWidth = 230,
            HorizontalAlignment = alignment
        };

        MessagesPanel.Children.Add(bubble);
        MessagesScrollViewer.ScrollToEnd();
    }

    public void FocusInput()
    {
        if (!MessageInput.IsEnabled)
            return;

        MessageInput.Focus();
        Keyboard.Focus(MessageInput);
    }
}
