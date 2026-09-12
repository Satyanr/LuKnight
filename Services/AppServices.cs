using LuKnight.Assistant;

namespace LuKnight.Services;

public sealed class AppServices
{
    public SettingsService Settings { get; }
    public ChatCoordinator Chat { get; }
    public MemoryService Memory { get; }
    public AssistantContextProvider Context { get; }
    public AssistantIntentRouter IntentRouter { get; }
    public IDesktopAppCatalog DesktopApps { get; }
    public LocalDesktopCommandRouter DesktopCommands { get; }
    public IExplorerActionExecutor ExplorerActions { get; }
    public AssistantToolRouter Tools { get; }
    public AssistantContextSourceRouter ContextSources { get; }
    public AssistantActionRouter Actions { get; }
    public AssistantController Assistant { get; }
    public IVoiceCaptureService VoiceCapture { get; }
    public UpdateService Updates { get; }
    public AppServices(
        SettingsService? settings = null,
        ICredentialService? credentials = null,
        MemoryService? memory = null,
        AssistantContextProvider? context = null,
        IVoiceCaptureService? voiceCapture = null,
        IDesktopAppCatalog? desktopApps = null,
        IDesktopActionExecutor? desktopExecutor = null,
        IExplorerActionExecutor? explorerExecutor = null)
    {
        Settings = settings ?? new();
        Chat = new(credentials ?? new SecureCredentialService(), Settings.Current.Chat);
        VoiceCapture = voiceCapture ?? new VoiceCaptureService();
        Memory = memory ?? new();
        Context = context ?? new();
        DesktopApps = desktopApps ?? DesktopAppCatalogService.Shared;
        DesktopCommands = new LocalDesktopCommandRouter(DesktopApps);
        IntentRouter = new AssistantIntentRouter(DesktopCommands);
        ExplorerActions = explorerExecutor ?? new WindowsExplorerActionExecutor();
        desktopExecutor ??= new WindowsDesktopActionExecutor();
        Tools = new AssistantToolRouter(new IAssistantTool[]
        {
            new RememberMemoryTool(Memory),
            new ForgetMemoryTool(Memory),
            new ListApplicationsTool(() => Chat.Options.UseApplicationContext)
        });
        ContextSources = new AssistantContextSourceRouter(new IAssistantContextSource[]
        {
            new LocalTextFileContextSource(() => Chat.Options.UseFileContext),
            new ClipboardTextContextSource(() => Chat.Options.UseClipboardContext),
            new SystemStatusContextSource(() => Chat.Options.UseSystemContext),
            new ScreenImageContextSource(() => Chat.Options.UseScreenContext, () => Chat.UsesGemini)
        });
        Actions = new AssistantActionRouter(new IAssistantAction[]
        {
            new OpenDesktopApplicationAction(() => Chat.Options.UseDesktopActions, desktopExecutor, DesktopApps),
            new FocusDesktopApplicationAction(() => Chat.Options.UseDesktopActions, desktopExecutor, DesktopApps),
            new OpenExplorerFolderAction(() => Chat.Options.UseDesktopActions, ExplorerActions),
            new SearchExplorerAction(() => Chat.Options.UseDesktopActions, ExplorerActions)
        });
        Assistant = new AssistantController(
            Chat,
            memory: Memory,
            context: Context,
            intentRouter: IntentRouter,
            tools: Tools,
            contextSources: ContextSources,
            actions: Actions);
        Updates = new(Settings);
    }
}
