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
}
