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
    public IDesktopWindowTargetCatalog DesktopWindows { get; }
    public IDesktopWindowActionExecutor WindowActions { get; }
    public IDesktopUiAutomationReader UiAutomation { get; }
    public IDesktopUiActionExecutor UiActions { get; }
    public IDesktopMouseActionExecutor MouseActions { get; }
    public IDesktopUiTextActionExecutor UiTextActions { get; }
    public IDesktopKeyboardTextActionExecutor KeyboardTextActions { get; }
    public IDesktopUiScreenEvidenceService UiScreenEvidence { get; }
    public IDesktopUiAssistedResolver UiAssistedResolver { get; }
    public LocalDesktopCommandRouter DesktopCommands { get; }
    public IExplorerActionExecutor ExplorerActions { get; }
    public AssistantToolRouter Tools { get; }
    public AssistantContextSourceRouter ContextSources { get; }
    public AssistantActionRouter Actions { get; }
    public AssistantSkillRouter Skills { get; }
    public AssistantController Assistant { get; }
    public IVoiceCaptureService VoiceCapture { get; }
    public ISpeechToTextService SpeechToText { get; }
    public ITextToSpeechService TextToSpeech { get; }
    public UpdateService Updates { get; }
    public AppServices(
        SettingsService? settings = null,
        ICredentialService? credentials = null,
        MemoryService? memory = null,
        AssistantContextProvider? context = null,
        IVoiceCaptureService? voiceCapture = null,
        ISpeechToTextService? speechToText = null,
        ITextToSpeechService? textToSpeech = null,
        IDesktopAppCatalog? desktopApps = null,
        IDesktopActionExecutor? desktopExecutor = null,
        IExplorerActionExecutor? explorerExecutor = null,
        IDesktopWindowTargetCatalog? desktopWindows = null,
        IDesktopWindowActionExecutor? windowExecutor = null,
        IDesktopUiAutomationReader? uiAutomation = null,
        IDesktopUiActionExecutor? uiActionExecutor = null,
        IDesktopMouseActionExecutor? mouseActionExecutor = null,
        IDesktopUiTextActionExecutor? uiTextActionExecutor = null,
        IDesktopKeyboardTextActionExecutor? keyboardTextActionExecutor = null,
        IDesktopUiScreenEvidenceService? uiScreenEvidenceService = null,
        IDesktopUiAssistedResolver? uiAssistedResolver = null)
    {
        Settings = settings ?? new();
        Chat = new(credentials ?? new SecureCredentialService(), Settings.Current.Chat);
        VoiceCapture = voiceCapture ?? new VoiceCaptureService();
        SpeechToText = speechToText ?? new LocalWhisperSpeechToTextService();
        TextToSpeech = textToSpeech ?? new WindowsTextToSpeechService();
        Memory = memory ?? new();
        Context = context ?? new();
        DesktopApps = desktopApps ?? DesktopAppCatalogService.Shared;
        DesktopWindows = desktopWindows ?? new DesktopWindowTargetService();
        WindowActions = windowExecutor ?? new WindowsDesktopWindowActionExecutor();
        UiAutomation = uiAutomation ?? new WindowsDesktopUiAutomationReader();
        UiActions = uiActionExecutor ?? new WindowsDesktopUiActionExecutor();
        MouseActions = mouseActionExecutor ?? new WindowsDesktopMouseActionExecutor();
        UiTextActions = uiTextActionExecutor ?? new WindowsDesktopUiTextActionExecutor();
        KeyboardTextActions = keyboardTextActionExecutor ?? new WindowsDesktopKeyboardTextActionExecutor();
        UiScreenEvidence =
            uiScreenEvidenceService ??
            new WindowsDesktopUiScreenEvidenceService();

        UiAssistedResolver =
            uiAssistedResolver ??
            new DesktopUiAssistedResolver(
                UiScreenEvidence);
        DesktopAppIndexWarmup.Start(DesktopApps);
        DesktopCommands = new LocalDesktopCommandRouter(DesktopApps, DesktopWindows);
        IntentRouter = new AssistantIntentRouter(DesktopCommands);
        ExplorerActions = explorerExecutor ?? new WindowsExplorerActionExecutor();
        desktopExecutor ??= new WindowsDesktopActionExecutor();
        Tools = new AssistantToolRouter(new IAssistantTool[]
        {
            new RememberMemoryTool(Memory),
            new ForgetMemoryTool(Memory),
            new ListApplicationsTool(() => Chat.Options.UseApplicationContext),
            new InspectDesktopUiTool(
                () => Chat.Options.UseDesktopActions,
                DesktopWindows,
                UiAutomation)
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
            new FocusDesktopWindowAction(() => Chat.Options.UseDesktopActions, DesktopWindows, WindowActions),
            new SetDesktopUiTextAction(
                () =>
                    Chat.Options.UseDesktopActions,
                DesktopWindows,
                UiAutomation,
                UiTextActions,
                KeyboardTextActions),
            new InvokeDesktopUiControlAction(
                () => Chat.Options.UseDesktopActions,
                DesktopWindows,
                UiAutomation,
                UiActions,
                MouseActions,
                UiAssistedResolver),
            new OpenExplorerFolderAction(() => Chat.Options.UseDesktopActions, ExplorerActions),
            new SearchExplorerAction(() => Chat.Options.UseDesktopActions, ExplorerActions)
        }, () => Chat.Options.DesktopPermission);
        Skills = new AssistantSkillRouter(new IAssistantSkill[]
        {
            new SearchDownloadsSkill()
        });
        Assistant = new AssistantController(
            Chat,
            memory: Memory,
            context: Context,
            intentRouter: IntentRouter,
            tools: Tools,
            contextSources: ContextSources,
            actions: Actions,
            skills: Skills);
        Updates = new(Settings);
    }
}
