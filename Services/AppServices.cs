using LuKnight.Assistant;

namespace LuKnight.Services;

public sealed class AppServices
{
    public SettingsService Settings { get; }
    public ChatCoordinator Chat { get; }
    public MemoryService Memory { get; }
    public AssistantController Assistant { get; }
    public UpdateService Updates { get; }
    public AppServices(
        SettingsService? settings = null,
        ICredentialService? credentials = null,
        MemoryService? memory = null)
    {
        Settings = settings ?? new();
        Chat = new(credentials ?? new SecureCredentialService(), Settings.Current.Chat);
        Memory = memory ?? new();
        Assistant = new AssistantController(Chat, memory: Memory);
        Updates = new(Settings);
    }
}
