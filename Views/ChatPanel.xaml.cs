using System;
using LuKnight.Assistant;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LuKnight.Views;

public enum VoiceInteractionState
{
    Idle,
    Listening,
    Transcribing,
    Thinking,
    Speaking
}

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
    public event Action? VoiceToggleRequested;
    public event Action<Guid>?
        ScheduledReminderRunRequested;

    public event Action<Guid>?
        ScheduledReminderDismissRequested;

    private readonly Dictionary<Guid, Border>
        _scheduledReminderCards =
            [];
    private readonly Dictionary<
        string,
        Border>
        _companionSuggestionCards =
            new(
                StringComparer.OrdinalIgnoreCase);
    public ChatStatus CurrentStatus { get; private set; } = ChatStatus.Local;
    public VoiceInteractionState CurrentVoiceState { get; private set; } = VoiceInteractionState.Idle;
    public bool HasDraftMessage => !string.IsNullOrWhiteSpace(MessageInput.Text);
    public string VoicePrivacySummary => VoicePrivacyText.Text;

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

    private void VoiceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (VoiceButton.IsEnabled)
            VoiceToggleRequested?.Invoke();
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

    public void ClearConversation()
    {
        Border[] reminders =
            _scheduledReminderCards
                .Values
                .ToArray();


        MessagesPanel.Children.Clear();
        _companionSuggestionCards.Clear();


        foreach (Border reminder
                 in reminders)
        {
            MessagesPanel.Children.Add(
                reminder);
        }


        MessageInput.Clear();

        MessagesScrollViewer
            .ScrollToEnd();
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

    public void SetVoiceEnabled(bool enabled) => VoiceButton.IsEnabled = enabled;

    public void SetVoiceRecording(bool recording)
    {
        SetVoiceState(recording ? VoiceInteractionState.Listening : VoiceInteractionState.Idle);
        MessageInput.IsEnabled = !recording;
        SendButton.IsEnabled = !recording;
    }

    public void SetVoiceState(VoiceInteractionState state)
    {
        CurrentVoiceState = state;
        switch (state)
        {
            case VoiceInteractionState.Listening:
                VoiceButton.Content = "■";
                VoiceButton.ToolTip = "Stop recording";
                VoicePrivacyText.Text = "● Mic active · audio stays in memory only";
                break;
            case VoiceInteractionState.Transcribing:
                VoiceButton.Content = "…";
                VoiceButton.ToolTip = "Transcribing locally";
                VoicePrivacyText.Text = "Mic off · transcribing locally";
                break;
            case VoiceInteractionState.Thinking:
                VoiceButton.Content = "🎤";
                VoiceButton.ToolTip = "Voice request is being processed";
                VoicePrivacyText.Text = "Mic off · processing request";
                break;
            case VoiceInteractionState.Speaking:
                VoiceButton.Content = "⏹";
                VoiceButton.ToolTip = "Stop speaking and listen";
                VoicePrivacyText.Text = "Mic off · press mic to interrupt";
                break;
            default:
                VoiceButton.Content = "🎤";
                VoiceButton.ToolTip = "Push to talk";
                VoicePrivacyText.Text = "Mic off · push-to-talk only";
                break;
        }
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

    public void SetDraftMessage(
        string message)
    {
        MessageInput.Text =
            message;

        MessageInput.CaretIndex =
            MessageInput.Text.Length;

        FocusInput();
    }

    public void FocusInput()
    {
        if (!MessageInput.IsEnabled)
            return;

        MessageInput.Focus();
        Keyboard.Focus(MessageInput);
    }
    public void AddScheduledReminder(
        Guid id,
        string displayName,
        DateTimeOffset dueAtUtc)
    {
        if (id ==
                Guid.Empty ||
            _scheduledReminderCards
                .ContainsKey(
                    id))
        {
            return;
        }


        string name =
            displayName?.Trim() ??
            string.Empty;

        if (name.Length == 0)
            return;


        var title =
            new TextBlock
            {
                Text =
                    $"⏰ {name}",

                FontWeight =
                    FontWeights.SemiBold,

                TextWrapping =
                    TextWrapping.Wrap,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            64,
                            58,
                            52))
            };


        var description =
            new TextBlock
            {
                Text =
                    $"Jadwal sudah jatuh tempo " +
                    $"({dueAtUtc.ToLocalTime():g}). " +
                    "Workflow belum dijalankan.",

                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        8),

                TextWrapping =
                    TextWrapping.Wrap,

                FontSize =
                    12,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            90,
                            84,
                            78))
            };


        var run =
            new Button
            {
                Content =
                    "Run",

                MinWidth =
                    64,

                Margin =
                    new Thickness(
                        0,
                        0,
                        6,
                        0)
            };


        var dismiss =
            new Button
            {
                Content =
                    "Dismiss",

                MinWidth =
                    64
            };


        var buttons =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal
            };

        buttons.Children.Add(
            run);

        buttons.Children.Add(
            dismiss);


        var content =
            new StackPanel();

        content.Children.Add(
            title);

        content.Children.Add(
            description);

        content.Children.Add(
            buttons);


        var card =
            new Border
            {
                Child =
                    content,

                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            244,
                            239,
                            218)),

                CornerRadius =
                    new CornerRadius(
                        10),

                Padding =
                    new Thickness(
                        10),

                Margin =
                    new Thickness(
                        2,
                        4,
                        2,
                        4),

                MaxWidth =
                    230,

                HorizontalAlignment =
                    HorizontalAlignment.Left
            };


        run.Click +=
            (_, _) =>
                ScheduledReminderRunRequested?
                    .Invoke(
                        id);


        dismiss.Click +=
            (_, _) =>
                ScheduledReminderDismissRequested?
                    .Invoke(
                        id);

        _scheduledReminderCards[id] =
            card;

        MessagesPanel.Children.Add(
            card);

        MessagesScrollViewer.ScrollToEnd();
    }

    public void RemoveScheduledReminder(
        Guid id)
    {
        if (!_scheduledReminderCards
                .Remove(
                    id,
                    out Border? card))
        {
            return;
        }

        MessagesPanel.Children.Remove(
            card);
    }
    public void AddCompanionSuggestion(
        CompanionSuggestionCandidate
            candidate)
    {
        ArgumentNullException.ThrowIfNull(
            candidate);


        string key =
            candidate.Key.Trim();


        if (key.Length == 0 ||
            _companionSuggestionCards
                .ContainsKey(
                    key))
        {
            return;
        }


        var title =
            new TextBlock
            {
                Text =
                    "💡 Lu-Knight suggestion",

                FontWeight =
                    FontWeights.SemiBold,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            64,
                            58,
                            52))
            };


        var message =
            new TextBlock
            {
                Text =
                    candidate.Message,

                TextWrapping =
                    TextWrapping.Wrap,

                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        8),

                FontSize =
                    12,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            90,
                            84,
                            78))
            };


        var use =
            new Button
            {
                Content =
                    "Use prompt",

                MinWidth =
                    82,

                Margin =
                    new Thickness(
                        0,
                        0,
                        6,
                        0)
            };


        var dismiss =
            new Button
            {
                Content =
                    "Not now",

                MinWidth =
                    64
            };


        var buttons =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal
            };

        buttons.Children.Add(
            use);

        buttons.Children.Add(
            dismiss);


        var content =
            new StackPanel();

        content.Children.Add(
            title);

        content.Children.Add(
            message);

        content.Children.Add(
            buttons);


        var card =
            new Border
            {
                Child =
                    content,

                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            232,
                            241,
                            230)),

                CornerRadius =
                    new CornerRadius(
                        10),

                Padding =
                    new Thickness(
                        10),

                Margin =
                    new Thickness(
                        2,
                        4,
                        2,
                        4),

                MaxWidth =
                    230,

                HorizontalAlignment =
                    HorizontalAlignment.Left
            };


        use.Click +=
            (_, _) =>
            {
                //
                // IMPORTANT:
                // only pre-fill user draft.
                //
                // Do not submit automatically.
                //

                SetDraftMessage(
                    candidate.SuggestedPrompt);

                RemoveCompanionSuggestion(
                    key);
            };


        dismiss.Click +=
            (_, _) =>
                RemoveCompanionSuggestion(
                    key);


        _companionSuggestionCards[
            key] =
                card;

        MessagesPanel.Children.Add(
            card);

        MessagesScrollViewer
            .ScrollToEnd();
    }

    public void RemoveCompanionSuggestion(
        string key)
    {
        if (!_companionSuggestionCards
                .Remove(
                    key,
                    out Border? card))
        {
            return;
        }


        MessagesPanel.Children.Remove(
            card);
    }
}
