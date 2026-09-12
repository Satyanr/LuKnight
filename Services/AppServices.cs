using LuKnight.Assistant;

namespace LuKnight.Services;

public sealed class AppServices
{
    public SettingsService Settings { get; }
    public ChatCoordinator Chat { get; }
    public MemoryService Memory { get; }
    public AssistantContextProvider Context { get; }
    public AssistantIntentRouter IntentRouter { get; }
    public AssistantToolRouter Tools { get; }
    public AssistantController Assistant { get; }
    public UpdateService Updates { get; }
    public AppServices(
        SettingsService? settings = null,
        ICredentialService? credentials = null,
        MemoryService? memory = null,
        AssistantContextProvider? context = null)
    {
        Settings = settings ?? new();
        Chat = new(credentials ?? new SecureCredentialService(), Settings.Current.Chat);
        Memory = memory ?? new();
        Context = context ?? new();
        IntentRouter = new AssistantIntentRouter();
        Tools = new AssistantToolRouter(new IAssistantTool[]
        {
            new RememberMemoryTool(Memory),
            new ForgetMemoryTool(Memory),
            new ListApplicationsTool(() => Chat.Options.UseApplicationContext)
        });
        Assistant = new AssistantController(
            Chat,
            memory: Memory,
            context: Context,
            intentRouter: IntentRouter,
            tools: Tools);
        Updates = new(Settings);
    }
}
