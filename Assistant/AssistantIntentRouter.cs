namespace LuKnight.Assistant;

public sealed class AssistantIntentRouter
{
    public AssistantIntent Route(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return AssistantIntent.Conversation();

        MemoryCommand? memoryCommand = MemoryCommandParser.Parse(input);
        if (memoryCommand is not null)
        {
            string toolName = memoryCommand.Kind == MemoryCommandKind.Remember
                ? BuiltInToolNames.MemoryRemember
                : BuiltInToolNames.MemoryForget;

            var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["text"] = memoryCommand.Text
            };

            return AssistantIntent.UseTool(new ToolInvocation(toolName, arguments));
        }

        if (IsApplicationAwarenessQuery(input))
        {
            return AssistantIntent.UseTool(new ToolInvocation(BuiltInToolNames.DesktopListApplications, new Dictionary<string, string>()));
        }

        return AssistantIntent.Conversation();
    }

    private static bool IsApplicationAwarenessQuery(string input)
    {
        string value = input.Trim().ToLowerInvariant();
        string[] phrases =
        [
            "aplikasi apa yang sedang terbuka",
            "aplikasi apa yang terbuka",
            "aplikasi apa yang sedang aktif",
            "aplikasi apa yang saya gunakan",
            "aplikasi apa yang sedang saya gunakan",
            "lihat aplikasi yang terbuka",
            "what apps are open",
            "what applications are open",
            "what app am i using",
            "which apps are open"
        ];

        return phrases.Any(phrase => value.Contains(phrase, StringComparison.Ordinal));
    }
}
