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
    string MovementSpeed);

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

        return basic + $"""

            - Character visible: {context.CharacterVisible}
            - Chat panel open: {context.ChatOpen}
            - Character state: {context.CharacterState}
            - Character mood: {context.CharacterMood}
            - Autonomous behavior: {context.AutonomousBehaviorEnabled}
            - Activity preset: {context.Activity}
            - Movement speed: {context.MovementSpeed}

            This context describes Lu-Knight itself.

            It does NOT mean you can see the user's
            screen, active application, files,
            clipboard, microphone, camera, or location.

            Never claim access to information that is
            not explicitly present in this context.
            """;
    }
}
