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
    public LocalDesktopCommandRouter DesktopCommands { get; }
    public AssistantToolRouter Tools { get; }
    public AssistantContextSourceRouter ContextSources { get; }
    public AssistantActionRouter Actions { get; }
    public AssistantSkillRouter Skills { get; }
    public AssistantWorkflowRuntime WorkflowRuntime { get; }
    public LocalScheduleStore ScheduleStore { get; }
    public LocalSchedulerService Scheduler { get; }
    public LocalCompanionAdvisor CompanionAdvisor { get; }
    public AssistantCapabilityRegistry Capabilities { get; }
    public UserSkillStore UserSkills { get; }
    public IReadOnlyList<UserSkillLoadIssue> UserSkillIssues { get; private set; } = [];
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
        IDesktopUiAssistedResolver? uiAssistedResolver = null,
        UserSkillStore? userSkillStore = null,
        AssistantWorkflowRuntime? workflowRuntime = null,
        LocalScheduleStore? scheduleStore = null,
        LocalCompanionAdvisor? companionAdvisor = null)
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
        IDesktopWindowActionExecutor windowActions = windowExecutor ?? new WindowsDesktopWindowActionExecutor();
        IDesktopUiAutomationReader uiAutomationReader = uiAutomation ?? new WindowsDesktopUiAutomationReader();
        IDesktopUiActionExecutor uiActions = uiActionExecutor ?? new WindowsDesktopUiActionExecutor();
        IDesktopMouseActionExecutor mouseActions = mouseActionExecutor ?? new WindowsDesktopMouseActionExecutor();
        IDesktopUiTextActionExecutor uiTextActions = uiTextActionExecutor ?? new WindowsDesktopUiTextActionExecutor();
        IDesktopKeyboardTextActionExecutor keyboardTextActions = keyboardTextActionExecutor ?? new WindowsDesktopKeyboardTextActionExecutor();
        IDesktopUiScreenEvidenceService uiScreenEvidence =
            uiScreenEvidenceService ??
            new WindowsDesktopUiScreenEvidenceService();

        IDesktopUiAssistedResolver uiAssistedResolverInstance =
            uiAssistedResolver ??
            new DesktopUiAssistedResolver(
                uiScreenEvidence);
        DesktopAppIndexWarmup.Start(DesktopApps);
        DesktopCommands = new LocalDesktopCommandRouter(DesktopApps, DesktopWindows);
        IntentRouter = new AssistantIntentRouter(DesktopCommands);
        IExplorerActionExecutor explorerActions = explorerExecutor ?? new WindowsExplorerActionExecutor();
        desktopExecutor ??= new WindowsDesktopActionExecutor();
        Tools = new AssistantToolRouter(new IAssistantTool[]
        {
            new RememberMemoryTool(Memory),
            new ForgetMemoryTool(Memory),
            new ListApplicationsTool(() => Chat.Options.UseApplicationContext),
            new InspectDesktopUiTool(
                () => Chat.Options.UseDesktopActions,
                DesktopWindows,
                uiAutomationReader)
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
            new FocusDesktopWindowAction(() => Chat.Options.UseDesktopActions, DesktopWindows, windowActions),
            new SetDesktopUiTextAction(
                () =>
                    Chat.Options.UseDesktopActions,
                DesktopWindows,
                uiAutomationReader,
                uiTextActions,
                keyboardTextActions),
            new InvokeDesktopUiControlAction(
                () => Chat.Options.UseDesktopActions,
                DesktopWindows,
                uiAutomationReader,
                uiActions,
                mouseActions,
                uiAssistedResolverInstance),
            new OpenExplorerFolderAction(() => Chat.Options.UseDesktopActions, explorerActions),
            new SearchExplorerAction(() => Chat.Options.UseDesktopActions, explorerActions)
        }, () => Chat.Options.DesktopPermission);
        Skills = new AssistantSkillRouter(BuiltInSkillCatalog.Create());
        UserSkills = userSkillStore ?? new UserSkillStore();
        UserSkillLoadResult userSkillResult = UserSkills.Load();
        var skillIssues = new List<UserSkillLoadIssue>(userSkillResult.Issues);
        foreach (IAssistantSkill skill in userSkillResult.Skills)
        {
            try { Skills.Register(skill); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            { skillIssues.Add(new(skill.Id, $"Skill tidak diregistrasikan: {ex.Message}")); }
        }
        UserSkillIssues = skillIssues.AsReadOnly();
        Capabilities = AssistantCapabilityRegistry.Create(Skills, Actions, Tools);
        WorkflowRuntime =
            workflowRuntime ??
            new AssistantWorkflowRuntime();
        Assistant = new AssistantController(
            Chat,
            memory: Memory,
            context: Context,
            intentRouter: IntentRouter,
            tools: Tools,
            contextSources: ContextSources,
            actions: Actions,
            skills: Skills,
            workflowRuntime: WorkflowRuntime);
        ScheduleStore = scheduleStore ?? new LocalScheduleStore();
        Scheduler = new LocalSchedulerService(ScheduleStore);
        Scheduler.Load();
        CompanionAdvisor = companionAdvisor ?? new LocalCompanionAdvisor();
        Updates = new(Settings);
    }
}
