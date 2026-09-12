namespace LuKnight.Assistant;

public sealed class AssistantIntentRouter
{
    public AssistantIntent Route(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return AssistantIntent.Conversation();

        MemoryCommand? memoryCommand = MemoryCommandParser.Parse(input);
        if (memoryCommand is null)
            return AssistantIntent.Conversation();

        string toolName = memoryCommand.Kind == MemoryCommandKind.Remember
            ? BuiltInToolNames.MemoryRemember
            : BuiltInToolNames.MemoryForget;

        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"] = memoryCommand.Text
        };

        return AssistantIntent.UseTool(new ToolInvocation(toolName, arguments));
    }
}
