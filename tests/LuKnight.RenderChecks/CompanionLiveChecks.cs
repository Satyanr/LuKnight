using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;
using LuKnight.Views;

internal static partial class Program
{
    private sealed class
        CompanionAcceptanceSkill :
            IAssistantSkill
    {
        public int Expansions
        {
            get;
            private set;
        }


        public string Id =>
            "acceptance-skill";

        public string DisplayName =>
            "Acceptance Skill";

        public string Description =>
            "Test-only skill that must never be invoked by proactive suggestions.";

        public IReadOnlyList<string>
            Aliases
        {
            get;
        } =
            ["acceptance-alias"];


        public SkillExpansionResult Expand(
            SkillInvocation invocation)
        {
            Expansions++;

            return SkillExpansionResult
                .Failure(
                    "Acceptance skill must not run.");
        }
    }


    private static async Task
        CheckCompanionLiveAsync()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                "LuKnight-CompanionLive",
                Guid.NewGuid()
                    .ToString("N"));

        Directory.CreateDirectory(
            directory);


        string settingsPath =
            Path.Combine(
                directory,
                "settings.json");


        try
        {
            //
            // ---------------------------------------------
            // Persist explicit proactive opt-in.
            // ---------------------------------------------
            //

            var settings =
                new SettingsService(
                    settingsPath);

            settings.Load();


            Require(
                !settings.Current
                    .Companion
                    .Enabled,
                "Fresh installation unexpectedly enabled proactive suggestions.");

            AppSettings enabledSettings =
                settings.Current with
                {
                    Chat =
                        settings.Current.Chat with
                        {
                            Provider =
                                ChatProvider.Gemini,

                            UseApplicationContext =
                                true
                        },

                    Companion =
                        new CompanionSettings
                        {
                            Enabled =
                                true,

                            Coding =
                                true,

                            Browsing =
                                true,

                            Creative =
                                true,

                            Office =
                                true,

                            Files =
                                true,

                            Communication =
                                true
                        }
                };


            Require(
                settings.Update(
                    enabledSettings),
                "Companion opt-in preferences could not be persisted.");


            //
            // Simulate restart.
            //

            var persistedSettings =
                new SettingsService(
                    settingsPath);

            persistedSettings.Load();


            Require(
                persistedSettings
                    .Current
                    .Companion
                    .Enabled &&
                persistedSettings
                    .Current
                    .Chat
                    .UseApplicationContext,
                "Proactive opt-in did not survive restart.");


            //
            // ---------------------------------------------
            // Production capability metadata.
            // ---------------------------------------------
            //

            var acceptanceSkill =
                new CompanionAcceptanceSkill();


            var skills =
                new AssistantSkillRouter(
                    new IAssistantSkill[]
                    {
                        acceptanceSkill
                    });


            var action =
                new PlanTestAction(
                    BuiltInActionNames
                        .DesktopOpenApplication);


            var actions =
                new AssistantActionRouter(
                    new IAssistantAction[]
                    {
                        action
                    },
                    () =>
                        DesktopPermissionLevel
                            .Sensitive);


            var tools =
                new AssistantToolRouter();


            AssistantCapabilityRegistry
                capabilities =
                    AssistantCapabilityRegistry
                        .Create(
                            skills,
                            actions,
                            tools);


            Require(
                capabilities.Contains(
                    "feature:chat"),
                "Live companion acceptance had no chat capability.");


            //
            // ---------------------------------------------
            // Only broad application category is required.
            // ---------------------------------------------
            //

            AssistantRuntimeContext runtimeContext =
                new(
                    RuntimeAvailable:
                        true,

                    LocalTime:
                        DateTimeOffset.Now,

                    TimeZoneId:
                        TimeZoneInfo.Local.Id,

                    CharacterVisible:
                        true,

                    ChatOpen:
                        false,

                    CharacterState:
                        "Idle",

                    CharacterMood:
                        "Neutral",

                    // Explicitly false:
                    // mascot autonomy is independent.
                    AutonomousBehaviorEnabled:
                        false,

                    Activity:
                        "Normal",

                    MovementSpeed:
                        "Normal")
                {
                    ApplicationContextEnabled =
                        true,

                    PrimaryApplication =
                        "PrivateProjectEditor (CodeEditor)",

                    PrimaryApplicationKind =
                        "CodeEditor",

                    VisibleApplications =
                    [
                        "PrivateProjectEditor (CodeEditor)",
                        "ConfidentialChatClient (Communication)"
                    ]
                };


            var provider =
                new AssistantContextProvider();


            provider.Attach(
                () =>
                    runtimeContext);


            var advisor =
                new LocalCompanionAdvisor();


            CompanionSuggestionCandidate?
                candidate =
                    advisor.Evaluate(
                        provider.Capture(),
                        persistedSettings
                            .Current
                            .Companion,
                        capabilities);


            Require(
                candidate is
                {
                    Key:
                        "app:code-editor",

                    Kind:
                        CompanionSuggestionKind.Coding
                },
                "Persisted proactive preference + broad context did not produce coding candidate.");


            Require(
                !candidate!.Message.Contains(
                    "PrivateProjectEditor",
                    StringComparison.OrdinalIgnoreCase) &&
                !candidate.Message.Contains(
                    "ConfidentialChatClient",
                    StringComparison.OrdinalIgnoreCase) &&
                !candidate.SuggestedPrompt.Contains(
                    "PrivateProjectEditor",
                    StringComparison.OrdinalIgnoreCase),
                "Proactive candidate leaked application identity.");


            //
            // ---------------------------------------------
            // Rate gate.
            // ---------------------------------------------
            //

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


            Require(
                gate.Observe(
                    candidate,
                    start) is null,
                "Live proactive integration bypassed dwell.");


            DateTimeOffset readyAt =
                start +
                CompanionSuggestionGate
                    .StabilityDelay;


            CompanionSuggestionCandidate?
                ready =
                    gate.Observe(
                        candidate,
                        readyAt);


            Require(
                ready ==
                    candidate,
                "Stable proactive candidate did not pass gate.");


            //
            // ---------------------------------------------
            // Assistant stack with observable side effects.
            // ---------------------------------------------
            //

            int geminiCalls =
                0;


            using var handler =
                new FakeHttp(
                    (_, _) =>
                    {
                        geminiCalls++;

                        return Task.FromResult(
                            JsonResponse(
                                new
                                {
                                    candidates =
                                        new[]
                                        {
                                            new
                                            {
                                                content =
                                                    new
                                                    {
                                                        parts =
                                                            new[]
                                                            {
                                                                new
                                                                {
                                                                    text =
                                                                        "Companion acceptance response."
                                                                }
                                                            }
                                                    }
                                            }
                                        }
                                }));
                    });


            using var client =
                new HttpClient(
                    handler);


            var credentials =
                new FakeCredentials
                {
                    Key =
                        "unused-companion-live-key"
                };


            var chat =
                new ChatCoordinator(
                    credentials,
                    persistedSettings
                        .Current
                        .Chat,
                    () =>
                        null,
                    client);


            var assistant =
                new AssistantController(
                    chat,
                    context:
                        provider,
                    actions:
                        actions,
                    skills:
                        skills);


            var panel =
                new ChatPanel();


            int submitted =
                0;


            var submittedReply =
                new TaskCompletionSource<
                    AssistantReply>(
                    TaskCreationOptions
                        .RunContinuationsAsynchronously);


            panel.MessageSubmitted +=
                message =>
                {
                    submitted++;

                    _ =
                        SubmitAsync(
                            message);
                };


            async Task SubmitAsync(
                string message)
            {
                try
                {
                    AssistantReply reply =
                        await assistant.SendAsync(
                            new AssistantRequest(
                                message,
                                AssistantInputSource.Chat));

                    submittedReply
                        .TrySetResult(
                            reply);
                }
                catch (Exception ex)
                {
                    submittedReply
                        .TrySetException(
                            ex);
                }
            }


            //
            // ---------------------------------------------
            // Tray stores only candidate key.
            // ---------------------------------------------
            //

            CompanionSuggestionCandidate readyCandidate = ready ??
                throw new InvalidOperationException("No ready companion candidate.");

            var pendingSuggestions =
                new Dictionary<
                    string,
                    CompanionSuggestionCandidate>(
                        StringComparer.OrdinalIgnoreCase)
                {
                    [readyCandidate.Key] =
                        readyCandidate
                };


            using var tray =
                new TrayIconService(
                    toggle:
                        () => { },

                    show:
                        () => { },

                    chat:
                        () => { },

                    settings:
                        () => { },

                    restart:
                        () => { },

                    exit:
                        () => { },

                    reminder:
                        null,

                    companion:
                        key =>
                        {
                            if (!pendingSuggestions
                                    .Remove(
                                        key,
                                        out CompanionSuggestionCandidate?
                                            stored))
                            {
                                return;
                            }


                            //
                            // Same live revalidation used
                            // by App before displaying card.
                            //

                            CompanionSuggestionCandidate?
                                current =
                                    advisor.Evaluate(
                                        provider.Capture(),
                                        persistedSettings
                                            .Current
                                            .Companion,
                                        capabilities);


                            if (current is null ||
                                !string.Equals(
                                    current.Key,
                                    stored.Key,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                return;
                            }


                            panel.AddCompanionSuggestion(
                                stored);
                        });


            tray.Show();


            Require(
                tray.NotifyCompanion(
                    readyCandidate.Key,
                    readyCandidate.Message),
                "Eligible proactive suggestion could not reach tray.");


            Require(
                gate.MarkPresented(
                    ready,
                    readyAt),
                "Presented proactive suggestion was not committed to rate gate.");


            //
            // Nothing has been requested by user.
            //

            Require(
                geminiCalls ==
                    0 &&
                handler.Calls ==
                    0 &&
                acceptanceSkill.Expansions ==
                    0 &&
                action.Preparations ==
                    0 &&
                action.Executions ==
                    0 &&
                submitted ==
                    0 &&
                assistant.Conversation
                    .GetRecentContext()
                    .Count ==
                    0,
                "Proactive notification caused assistant work before user interaction.");


            //
            // ---------------------------------------------
            // Synthetic tray balloon click.
            // ---------------------------------------------
            //

            var icon =
                Get<System.Windows.Forms.NotifyIcon>(
                    tray,
                    "_icon");


            var balloonClick =
                typeof(
                    System.Windows.Forms.NotifyIcon)
                    .GetMethod(
                        "OnBalloonTipClicked",
                        Private)
                ?? throw new MissingMethodException(
                    "NotifyIcon.OnBalloonTipClicked");


            balloonClick.Invoke(
                icon,
                Array.Empty<object>());


            var cards =
                Get<
                    Dictionary<string, Border>>(
                        panel,
                        "_companionSuggestionCards");


            Require(
                cards.Count ==
                    1 &&
                cards.ContainsKey(
                    readyCandidate.Key),
                "Tray click did not create companion suggestion card.");


            Require(
                geminiCalls ==
                    0 &&
                acceptanceSkill.Expansions ==
                    0 &&
                action.Preparations ==
                    0 &&
                action.Executions ==
                    0 &&
                submitted ==
                    0,
                "Opening proactive card triggered assistant execution.");


            //
            // ---------------------------------------------
            // Use prompt = DRAFT ONLY.
            // ---------------------------------------------
            //

            Border card =
                cards[
                    readyCandidate.Key];

            var content =
                (StackPanel)
                    card.Child;

            var buttons =
                (StackPanel)
                    content.Children[2];

            var usePrompt =
                (Button)
                    buttons.Children[0];


            usePrompt.RaiseEvent(
                new RoutedEventArgs(
                    Button.ClickEvent));


            TextBox input =
                Get<TextBox>(
                    panel,
                    "MessageInput");


            Require(
                input.Text ==
                    readyCandidate.SuggestedPrompt,
                "Use prompt did not place exact suggestion into draft.");


            Require(
                cards.Count ==
                    0,
                "Used companion card remained visible.");


            Require(
                submitted ==
                    0 &&
                geminiCalls ==
                    0 &&
                handler.Calls ==
                    0 &&
                acceptanceSkill.Expansions ==
                    0 &&
                action.Preparations ==
                    0 &&
                action.Executions ==
                    0 &&
                assistant.Conversation
                    .GetRecentContext()
                    .Count ==
                    0,
                "Use prompt auto-submitted or executed assistant work.");


            //
            // ---------------------------------------------
            // User explicitly presses Send.
            // This is the FIRST request boundary.
            // ---------------------------------------------
            //

            Button send =
                Get<Button>(
                    panel,
                    "SendButton");


            send.RaiseEvent(
                new RoutedEventArgs(
                    Button.ClickEvent));


            AssistantReply reply =
                await submittedReply
                    .Task
                    .WaitAsync(
                        TimeSpan.FromSeconds(
                            10));


            Require(
                submitted ==
                    1,
                "Explicit Send did not create exactly one user request.");


            Require(
                geminiCalls ==
                    1 &&
                handler.Calls ==
                    1,
                "Gemini was not called exactly once after explicit Send.");


            Require(
                reply.Backend ==
                    AssistantBackend.Gemini,
                "Explicit proactive draft did not enter normal chat path.");


            Require(
                acceptanceSkill.Expansions ==
                    0,
                "Proactive draft unexpectedly invoked a skill.");


            Require(
                action.Preparations ==
                    0 &&
                action.Executions ==
                    0,
                "Proactive draft unexpectedly prepared or executed desktop action.");


            //
            // ---------------------------------------------
            // Stale preference revalidation.
            // ---------------------------------------------
            //

            CompanionSuggestionCandidate?
                second =
                    advisor.Evaluate(
                        provider.Capture(),
                        persistedSettings
                            .Current
                            .Companion,
                        capabilities);


            Require(
                second is not null,
                "Stale-preference fixture could not produce candidate.");


            pendingSuggestions[
                second!.Key] =
                    second;


            Require(
                tray.NotifyCompanion(
                    second.Key,
                    second.Message),
                "Stale-preference fixture could not reach tray.");


            //
            // Category is disabled AFTER notification,
            // BEFORE click.
            //

            Require(
                persistedSettings.Update(
                    persistedSettings.Current with
                    {
                        Companion =
                            persistedSettings
                                .Current
                                .Companion with
                                {
                                    Coding =
                                        false
                                }
                    }),
                "Coding preference could not be disabled during live acceptance.");


            balloonClick.Invoke(
                icon,
                Array.Empty<object>());


            Require(
                cards.Count ==
                    0,
                "Stale proactive notification ignored updated category preference.");


            Require(
                geminiCalls ==
                    1 &&
                acceptanceSkill.Expansions ==
                    0 &&
                action.Preparations ==
                    0 &&
                action.Executions ==
                    0,
                "Stale proactive notification triggered assistant execution.");


            provider.Detach();


            Console.WriteLine();
            Console.WriteLine(
                "Persisted proactive opt-in loaded.");

            Console.WriteLine(
                "Broad CodeEditor context produced local candidate.");

            Console.WriteLine(
                "Dwell gate delayed presentation.");

            Console.WriteLine(
                "Tray click produced suggestion card only.");

            Console.WriteLine(
                "Use prompt populated draft only.");

            Console.WriteLine(
                "Before explicit Send: Gemini 0, skill 0, action 0.");

            Console.WriteLine(
                "Explicit Send produced exactly one normal Gemini request.");

            Console.WriteLine(
                "Stale category preference blocked tray handoff.");

            Console.WriteLine(
                "Tray click and button events are synthetic; physical Windows shell interaction is not verified.");

            Console.WriteLine();
            Console.WriteLine(
                "PASS: proactive companion integration acceptance.");
        }
        finally
        {
            try
            {
                Directory.Delete(
                    directory,
                    recursive:
                        true);
            }
            catch (Exception ex)
                when (ex is
                    IOException or
                    UnauthorizedAccessException)
            {
                Console.WriteLine(
                    $"Warning: companion live cleanup failed: {ex.Message}");
            }
        }
    }
}
