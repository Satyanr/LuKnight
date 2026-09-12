namespace LuKnight.Assistant;

public sealed record AssistantRuntimeContext(
    bool RuntimeAvailable,
    DateTimeOffset LocalTime,
    string TimeZoneId,
    bool CharacterVisible,
    bool ChatOpen,
    string CharacterState,
    string CharacterMood,
    bool AutonomousBehaviorEnabled,
    string Activity,
    string MovementSpeed)
{
    public bool ApplicationContextEnabled { get; init; }
    public string? PrimaryApplication { get; init; }
    public IReadOnlyList<string> VisibleApplications { get; init; } = Array.Empty<string>();
}

public sealed class AssistantContextProvider
{
    private Func<AssistantRuntimeContext>? _reader;

    public void Attach(Func<AssistantRuntimeContext> reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public void Detach() => _reader = null;

    public AssistantRuntimeContext Capture()
    {
        try
        {
            if (_reader is not null)
                return _reader();
        }
        catch
        {
        }

        return new AssistantRuntimeContext(
            false,
            DateTimeOffset.Now,
            TimeZoneInfo.Local.Id,
            false,
            false,
            "Unavailable",
            "Unavailable",
            false,
            "Unavailable",
            "Unavailable");
    }

    public string BuildSystemInstruction()
    {
        AssistantRuntimeContext context = Capture();
        string basic = $"""
            Runtime context supplied directly by
            the Lu-Knight application:

            - Local date/time: {context.LocalTime:yyyy-MM-dd HH:mm:ss zzz}
            - Local time zone: {context.TimeZoneId}
            """;

        if (!context.RuntimeAvailable)
        {
            return basic + """

            Detailed Lu-Knight runtime state
            is currently unavailable.

            Do not infer information that was
            not supplied by the application.
            """;
        }

        string applicationContext;

        if (!context.ApplicationContextEnabled)
        {
            applicationContext = """
            - Application awareness: disabled
            """;
        }
        else if (context.VisibleApplications.Count == 0)
        {
            applicationContext = """
            - Application awareness: enabled
            - External applications: none detected
            """;
        }
        else
        {
            string visible = string.Join(", ", context.VisibleApplications);
            applicationContext = $"""
            - Application awareness: enabled
            - Primary visible external application: {context.PrimaryApplication ?? "Unknown"}
            - Visible external applications: {visible}
            """;
        }

        return basic + $"""

            - Character visible: {context.CharacterVisible}
            - Chat panel open: {context.ChatOpen}
            - Character state: {context.CharacterState}
            - Character mood: {context.CharacterMood}
            - Autonomous behavior: {context.AutonomousBehaviorEnabled}
            - Activity preset: {context.Activity}
            - Movement speed: {context.MovementSpeed}
            {applicationContext}

            This context describes Lu-Knight itself.

            Application awareness only provides process names
            and broad application categories.

            Runtime and application-awareness context do not
            provide screen contents, window titles, file contents,
            clipboard contents, microphone, camera, or location.

            A screen image is available only when the current
            user request contains an explicit user-approved
            screen-image reference.

            Never claim continuous screen visibility or access.

            Never infer information that was not explicitly
            supplied by the application.
            """;
    }
}
