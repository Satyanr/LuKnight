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

        FileContextCommand? fileCommand = FileContextCommandParser.Parse(input);
        if (fileCommand is not null)
        {
            return AssistantIntent.UseContext(new ContextInvocation(
                BuiltInContextNames.LocalTextFile,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["path"] = fileCommand.Path
                }));
        }

        ClipboardContextCommand? clipboardCommand = ClipboardContextCommandParser.Parse(input);
        if (clipboardCommand is not null)
        {
            return AssistantIntent.UseContext(new ContextInvocation(
                BuiltInContextNames.ClipboardText,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
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
