using System.Windows;
using System.Windows.Controls;
using LuKnight.Views;
using LuKnight.Services;
using LuKnight.Assistant;

internal static partial class Program
{
    private static void
        CheckCompanionFoundation()
    {
        var advisor =
            new LocalCompanionAdvisor();


        var context =
            new AssistantRuntimeContext(
                RuntimeAvailable:
                    true,

                LocalTime:
                    new DateTimeOffset(
                        2026,
                        10,
                        5,
                        10,
                        0,
                        0,
                        TimeSpan.FromHours(
                            8)),

                TimeZoneId:
                    "Asia/Makassar",

                CharacterVisible:
                    true,

                ChatOpen:
                    false,

                CharacterState:
                    "Idle",

                CharacterMood:
                    "Neutral",

                AutonomousBehaviorEnabled:
                    true,

                Activity:
                    "Normal",

                MovementSpeed:
                    "Normal")
            {
                ApplicationContextEnabled =
                    true,

                PrimaryApplication =
                    "VerySecretProjectEditor (CodeEditor)",

                PrimaryApplicationKind =
                    "CodeEditor",

                VisibleApplications =
                [
                    "VerySecretProjectEditor (CodeEditor)",
                    "PrivateClientApp (Communication)"
                ]
            };


        CompanionSuggestionCandidate?
            coding =
                advisor.Evaluate(
                    context);


        Require(
            coding is
            {
                Key:
                    "app:code-editor",

                Kind:
                    CompanionSuggestionKind.Coding
            },
            "Code-editor context did not create the expected companion candidate.");


        Require(
            !coding!.Message.Contains(
                "VerySecretProjectEditor",
                StringComparison.OrdinalIgnoreCase) &&
            !coding.Message.Contains(
                "PrivateClientApp",
                StringComparison.OrdinalIgnoreCase) &&
            !coding.SuggestedPrompt.Contains(
                "VerySecretProjectEditor",
                StringComparison.OrdinalIgnoreCase),
            "Companion suggestion exposed application identity.");


        //
        // Deterministic.
        //

        CompanionSuggestionCandidate?
            repeated =
                advisor.Evaluate(
                    context);


        Require(
            coding ==
                repeated,
            "Companion advisor was not deterministic.");


        //
        // Existing user controls must suppress
        // proactive candidates.
        //

        Require(
            advisor.Evaluate(
                context with
                {
                    RuntimeAvailable =
                        false
                }) is null,
            "Unavailable runtime produced a proactive suggestion.");


        Require(
            advisor.Evaluate(
                context with
                {
                    CharacterVisible =
                        false
                }) is null,
            "Hidden companion produced a proactive suggestion.");


        Require(
            advisor.Evaluate(
                context with
                {
                    ChatOpen =
                        true
                }) is null,
            "Open chat produced an unsolicited companion suggestion.");


        Require(
            advisor.Evaluate(
                context with
                {
                    AutonomousBehaviorEnabled =
                        false
                }) is null,
            "Disabled autonomous behavior still produced a suggestion.");


        Require(
            advisor.Evaluate(
                context with
                {
                    ApplicationContextEnabled =
                        false
                }) is null,
            "Disabled application awareness still produced a suggestion.");


        //
        // Do not infer kind from display text.
        //

        Require(
            advisor.Evaluate(
                context with
                {
                    PrimaryApplicationKind =
                        null,

                    PrimaryApplication =
                        "VerySecretProjectEditor (CodeEditor)"
                }) is null,
            "Companion advisor inferred application kind from display text.");


        //
        // Unknown applications stay quiet.
        //

        Require(
            advisor.Evaluate(
                context with
                {
                    PrimaryApplicationKind =
                        "Unknown"
                }) is null,
            "Unknown application produced a proactive suggestion.");


        //
        // Known broad categories.
        //

        Require(
            advisor.Evaluate(
                context with
                {
                    PrimaryApplicationKind =
                        "Creative"
                })?.Kind ==
                CompanionSuggestionKind.Creative,
            "Creative application context was not recognized.");


        Require(
            advisor.Evaluate(
                context with
                {
                    PrimaryApplicationKind =
                        "Office"
                })?.Kind ==
                CompanionSuggestionKind.Office,
            "Office application context was not recognized.");


        Require(
            advisor.Evaluate(
                context with
                {
                    PrimaryApplicationKind =
                        "Browser"
                })?.Kind ==
                CompanionSuggestionKind.Browsing,
            "Browser context was not recognized.");


        Require(
            advisor.Evaluate(
                context with
                {
                    PrimaryApplicationKind =
                        "FileManager"
                })?.Kind ==
                CompanionSuggestionKind.Files,
            "File-manager context was not recognized.");


        Require(
            advisor.Evaluate(
                context with
                {
                    PrimaryApplicationKind =
                        "Communication"
                })?.Kind ==
                CompanionSuggestionKind.Communication,
            "Communication context was not recognized.");
        Require(advisor.Evaluate(context with
        {
            PrimaryApplication = "Unrelated private process",
            VisibleApplications = ["Other app", "Sensitive title"]
        }) == coding, "Application identity changed companion output.");
        Require(advisor.Evaluate(context with { PrimaryApplicationKind = "codeeditor" }) == coding,
            "Broad application category matching was not case-insensitive.");
        foreach (string? kind in new string?[] { null, "", "   ", "NotAnApplicationKind", "999" })
            Require(advisor.Evaluate(context with { PrimaryApplicationKind = kind }) is null,
                "Missing or invalid broad category produced a suggestion.");
        var provider = new AssistantContextProvider();
        Require(advisor.Evaluate(provider.Capture()) is null, "Detached context provider produced a suggestion.");
        provider.Attach(() => context);
        Require(advisor.Evaluate(provider.Capture()) == coding, "Advisor did not accept the supplied runtime context.");
        provider.Detach();
        Require(advisor.Evaluate(provider.Capture()) is null, "Detached provider retained proactive context.");
    }
    private static void
        CheckCompanionRateLimit()
    {
        var gate =
            new CompanionSuggestionGate();


        DateTimeOffset start =
            new(
                2026,
                10,
                5,
                10,
                0,
                0,
                TimeSpan.Zero);


        var coding =
            new CompanionSuggestionCandidate(
                "app:code-editor",
                CompanionSuggestionKind.Coding,
                "Coding suggestion",
                "Help coding");


        Require(
            gate.Observe(
                coding,
                start) is null,
            "Companion suggestion skipped dwell time.");


        Require(
            gate.Observe(
                coding,
                start +
                CompanionSuggestionGate
                    .StabilityDelay -
                TimeSpan.FromTicks(
                    1)) is null,
            "Companion suggestion became ready before dwell boundary.");


        Require(
            gate.Observe(
                coding,
                start +
                CompanionSuggestionGate
                    .StabilityDelay) ==
                coding,
            "Companion suggestion did not become ready at dwell boundary.");


        DateTimeOffset firstPresented =
            start +
            CompanionSuggestionGate
                .StabilityDelay;


        Require(
            gate.MarkPresented(
                coding,
                firstPresented),
            "Eligible suggestion could not be committed.");


        Require(
            gate.PresentedCount ==
                1,
            "Presented suggestion count was incorrect.");


        //
        // Same key cooldown.
        //

        Require(
            gate.Observe(
                coding,
                firstPresented +
                TimeSpan.FromHours(
                    1)) is null,
            "Same suggestion ignored key cooldown.");


        //
        // Null context resets dwell.
        //

        gate.Observe(
            null,
            firstPresented +
            TimeSpan.FromHours(
                5));


        Require(
            gate.Observe(
                coding,
                firstPresented +
                TimeSpan.FromHours(
                    5) +
                TimeSpan.FromSeconds(
                    1)) is null,
            "Suggestion retained dwell after context disappeared.");
        var browser =
            new CompanionSuggestionCandidate(
                "app:browser",
                CompanionSuggestionKind.Browsing,
                "Browser suggestion",
                "Help browsing");


        var keyGate =
            new CompanionSuggestionGate();


        keyGate.Observe(
            coding,
            start);


        Require(
            keyGate.Observe(
                browser,
                start +
                TimeSpan.FromSeconds(
                    89)) is null,
            "Changing app context inherited previous dwell.");


        Require(
            keyGate.Observe(
                browser,
                start +
                TimeSpan.FromSeconds(
                    89) +
                CompanionSuggestionGate
                    .StabilityDelay) ==
                browser,
            "New stable context never completed its own dwell.");

        var cooldownGate =
            new CompanionSuggestionGate();


        cooldownGate.Observe(
            coding,
            start);

        DateTimeOffset codingReady =
            start +
            CompanionSuggestionGate
                .StabilityDelay;

        Require(
            cooldownGate.MarkPresented(
                coding,
                codingReady),
            "First cooldown fixture was not committed.");


        DateTimeOffset browserStart =
            codingReady +
            TimeSpan.FromMinutes(
                1);

        cooldownGate.Observe(
            browser,
            browserStart);


        Require(
            cooldownGate.Observe(
                browser,
                browserStart +
                CompanionSuggestionGate
                    .StabilityDelay) is null,
            "Global cooldown was bypassed by a different key.");


        Require(
            cooldownGate.Observe(
                browser,
                codingReady +
                CompanionSuggestionGate
                    .GlobalCooldown) ==
                browser,
            "Suggestion remained blocked after global cooldown.");
            var sessionGate = new CompanionSuggestionGate();
            for (int n = 0; n < CompanionSuggestionGate.MaxSuggestionsPerSession; n++)
            {
                var next = coding with { Key = "category:" + n };
                DateTimeOffset observed = start.AddHours(n);
                Require(sessionGate.Observe(next, observed) is null &&
                    sessionGate.MarkPresented(next, observed + CompanionSuggestionGate.StabilityDelay),
                    "Session suggestion fixture could not be presented.");
            }
            var fourth = coding with { Key = "category:four" };
            sessionGate.Observe(fourth, start.AddDays(1));
            Require(sessionGate.Observe(fourth, start.AddDays(1).AddMinutes(2)) is null &&
                !sessionGate.MarkPresented(fourth, start.AddDays(1).AddMinutes(2)) && sessionGate.PresentedCount == 3,
                "Companion exceeded three suggestions per session.");
            Require(gate.Observe(coding, firstPresented.AddHours(5).AddSeconds(1) + CompanionSuggestionGate.StabilityDelay) == coding,
                "Uncommitted observation consumed presentation budget.");
            var boundaryGate = new CompanionSuggestionGate();
            boundaryGate.Observe(coding, start);
            Require(boundaryGate.MarkPresented(coding, firstPresented), "Key boundary fixture failed.");
            Require(boundaryGate.Observe(coding with { Key = " APP:CODE-EDITOR " },
                firstPresented + CompanionSuggestionGate.SameKeyCooldown - TimeSpan.FromTicks(1)) is null,
                "Key normalization bypassed same-key cooldown.");
            Require(boundaryGate.Observe(coding, firstPresented + CompanionSuggestionGate.SameKeyCooldown) == coding,
                "Same key was blocked at exact cooldown boundary.");
            var rollbackGate = new CompanionSuggestionGate();
            rollbackGate.Observe(coding, start);
            Require(rollbackGate.Observe(coding, start.AddSeconds(-1)) is null &&
                rollbackGate.Observe(coding, start.AddSeconds(88)) is null &&
                rollbackGate.Observe(coding, start.AddSeconds(89)) == coding,
                "Clock rollback did not restart dwell.");
            foreach (string invalid in new[] { "", "bad\0key", new string('x', 81) })
            {
                var invalidGate = new CompanionSuggestionGate();
                Require(invalidGate.Observe(coding with { Key = invalid }, start) is null &&
                    !invalidGate.MarkPresented(coding with { Key = invalid }, start.AddHours(5)),
                    "Invalid candidate key entered the suggestion gate.");
            }
    }

    private static void
        CheckCompanionSuggestionCard()
    {
        var panel =
            new ChatPanel();


        int submitted =
            0;

        panel.MessageSubmitted +=
            _ =>
                submitted++;


        var suggestion =
            new CompanionSuggestionCandidate(
                "app:code-editor",
                CompanionSuggestionKind.Coding,
                "Need coding help?",
                "Please help with my code.");


        panel.AddCompanionSuggestion(
            suggestion);


        var cards =
            Get<Dictionary<string, Border>>(
                panel,
                "_companionSuggestionCards");


        Require(
            cards.Count ==
                1,
            "Companion suggestion card was not created.");


        Border card =
            cards[
                suggestion.Key];

        var content =
            (StackPanel)card.Child;

        var buttons =
            (StackPanel)
                content.Children[2];


        ((Button)buttons.Children[0])
            .RaiseEvent(
                new RoutedEventArgs(
                    Button.ClickEvent));


        Require(
            submitted ==
                0,
            "Using proactive suggestion auto-submitted the prompt.");


        Require(
            panel.HasDraftMessage,
            "Using proactive suggestion did not create a draft.");


        Require(
            cards.Count ==
                0,
            "Used suggestion card remained visible.");
            Require(Get<TextBox>(panel, "MessageInput").Text == suggestion.SuggestedPrompt,
                "Use prompt did not preserve the exact suggested draft.");
            panel.AddCompanionSuggestion(suggestion);
            panel.AddCompanionSuggestion(suggestion with { Key = suggestion.Key.ToUpperInvariant() });
            Require(cards.Count == 1, "Suggestion cards were duplicated by key casing.");
            var dismissButtons = (StackPanel)((StackPanel)cards[suggestion.Key].Child).Children[2];
            ((Button)dismissButtons.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(cards.Count == 0 && submitted == 0 && Get<TextBox>(panel, "MessageInput").Text == suggestion.SuggestedPrompt,
                "Not now sent a prompt or changed the user draft.");
            Guid reminderId = Guid.NewGuid();
            panel.AddScheduledReminder(reminderId, "Keep reminder", DateTimeOffset.UtcNow);
            panel.AddCompanionSuggestion(suggestion);
            panel.ClearConversation();
            Require(cards.Count == 0 && Get<Dictionary<Guid, Border>>(panel, "_scheduledReminderCards").ContainsKey(reminderId),
                "Clear Chat did not remove ephemeral suggestions while preserving reminders.");
    }

    private static void CheckCompanionTrayRouting()
    {
        int settings = 0, reminders = 0, companion = 0;
        string? openedKey = null;
        using var tray = new TrayIconService(() => { }, () => { }, () => { }, () => settings++,
            () => { }, () => { }, _ => reminders++, key => { companion++; openedKey = key; });
        var icon = Get<System.Windows.Forms.NotifyIcon>(tray, "_icon");
        var click = typeof(System.Windows.Forms.NotifyIcon).GetMethod("OnBalloonTipClicked", Private)!;
        void Click() => click.Invoke(icon, Array.Empty<object>());
        void Expire() => typeof(TrayIconService).GetField("_balloonRouteExpiresAt", Private)!
            .SetValue(tray, DateTimeOffset.UtcNow.AddSeconds(-1));
        Require(!tray.NotifyCompanion("", "Message") && !tray.NotifyCompanion("key", new string('x', 241)) &&
            !tray.NotifyCompanion("key", "bad\nmessage"), "Invalid companion notification was accepted.");
        tray.NotifyUpdate("test");
        Require(!tray.NotifyCompanion("key", "Message"), "Companion replaced an active update balloon.");
        Click();
        Require(settings == 1 && companion == 0, "Companion priority changed update routing.");
        tray.NotifyReminder(Guid.NewGuid(), "Due reminder");
        Require(!tray.NotifyCompanion("key", "Message"), "Companion replaced an active reminder balloon.");
        Click();
        Require(reminders == 1 && companion == 0, "Companion priority changed reminder routing.");
        Require(tray.NotifyCompanion(" app:code-editor ", "Coding help"), "Companion notification was rejected.");
        Require(!tray.NotifyCompanion("app:browser", "Browser help"), "Companion replaced an active balloon.");
        Click(); Click();
        Require(companion == 1 && openedKey == "app:code-editor", "Companion lost normalized key or dispatched twice.");
        tray.NotifyCompanion("expired", "Message");
        Expire(); Click();
        Require(companion == 1, "Expired companion route dispatched a stale callback.");
        tray.NotifyUpdate("expired"); Expire();
        Require(tray.NotifyCompanion("replacement", "Message"), "Expired route blocked companion notification.");
        tray.NotifyReminder(Guid.NewGuid(), "Higher priority"); Click();
        Require(reminders == 2 && companion == 1, "Reminder did not replace a companion route.");
        tray.NotifyCompanion("disposed", "Message"); tray.Dispose(); Click();
        Require(!tray.NotifyCompanion("key", "Message") && companion == 1,
            "Disposed tray accepted or dispatched a companion notification.");
    }
}
