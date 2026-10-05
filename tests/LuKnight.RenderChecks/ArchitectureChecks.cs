using System.IO;
using LuKnight.Services;

internal static partial class Program
{
    private static void
        CheckArchitectureBoundaries()
    {
        string root =
            AppContext.BaseDirectory;

        while (!File.Exists(
                   Path.Combine(
                       root,
                       "LuKnight.csproj")))
        {
            root =
                Directory
                    .GetParent(
                        root)?
                    .FullName
                ?? throw new
                    InvalidOperationException(
                        "Repository root tidak ditemukan.");
        }


        string Read(
            string relative)
        {
            string path =
                Path.Combine(
                    root,
                    relative.Replace(
                        '/',
                        Path.DirectorySeparatorChar));

            Require(
                File.Exists(
                    path),
                $"Architecture source missing: {relative}");

            return File.ReadAllText(
                path);
        }


        void Reject(
            string relative,
            params string[] forbidden)
        {
            string source =
                Read(
                    relative);

            foreach (string token
                     in forbidden)
            {
                Require(
                    !source.Contains(
                        token,
                        StringComparison.Ordinal),
                    $"{relative} crossed architecture boundary via '{token}'.");
            }
        }


        //
        // -----------------------------------------
        // Workflow runtime is a pure state machine.
        // -----------------------------------------
        //

        Reject(
            "Assistant/AssistantWorkflowRuntime.cs",
            "AssistantIntentRouter",
            "AssistantActionRouter",
            "IAssistantAction",
            "PreparedAssistantAction",
            "WindowsDesktop",
            "ChatCoordinator",
            "HttpClient",
            "Gemini");


        //
        // -----------------------------------------
        // Skills are definitions/expansion only.
        // -----------------------------------------
        //

        string skillInterface =
            Read(
                "Assistant/AssistantSkill.cs");

        Require(
            !skillInterface.Contains(
                "ExecuteAsync",
                StringComparison.Ordinal),
            "Skill abstraction became an executor.");


        //
        // -----------------------------------------
        // Scheduler is data/time state only.
        // -----------------------------------------
        //

        Reject(
            "Services/LocalSchedulerService.cs",
            "AssistantActionRouter",
            "IAssistantAction",
            "PreparedAssistantAction",
            "AssistantController",
            "WindowsDesktop",
            "ChatCoordinator",
            "HttpClient",
            "Gemini",
            ".ExecuteAsync(");


        Reject(
            "Services/LocalScheduleStore.cs",
            "AssistantActionRouter",
            "IAssistantAction",
            "WindowsDesktop",
            "ChatCoordinator",
            "HttpClient",
            "Gemini");


        //
        // -----------------------------------------
        // Proactive companion is advisory only.
        // -----------------------------------------
        //

        Reject(
            "Assistant/LocalCompanionAdvisor.cs",
            "AssistantActionRouter",
            "IAssistantAction",
            "PreparedAssistantAction",
            "AssistantController",
            "ChatCoordinator",
            "HttpClient",
            "WindowsDesktop",
            ".ExecuteAsync(");


        Reject(
            "Assistant/CompanionSuggestionGate.cs",
            "AssistantActionRouter",
            "IAssistantAction",
            "PreparedAssistantAction",
            "AssistantController",
            "ChatCoordinator",
            "HttpClient",
            "WindowsDesktop",
            ".ExecuteAsync(");


        Reject(
            "Services/AssistantNotificationCoordinator.cs",
            "AssistantController",
            "AssistantActionRouter",
            "IAssistantAction",
            "PreparedAssistantAction",
            "WindowsDesktop",
            "ChatCoordinator",
            ".PrepareAsync(",
            ".ExecuteAsync(",
            ".SendAsync(",
            "StartScheduledSkillAsync(");

        string app =
            Read(
                "App.xaml.cs");


        foreach (string movedConcern
                 in new[]
                 {
                     "CompanionSuggestionGate",
                     "_scheduleNotifiedThisSession",
                     "_companionSuggestions",
                     "GetReminderCandidates(",
                     "CompanionAdvisor.Evaluate(",
                     "Scheduler.MarkPresented("
                 })
        {
            Require(
                !app.Contains(
                    movedConcern,
                    StringComparison.Ordinal),
                $"App still owns assistant notification orchestration: {movedConcern}");
        }

        Require(
            app.Contains(
                "Notifications.Poll(",
                StringComparison.Ordinal) &&
            app.Contains(
                "TryConsumeCompanion(",
                StringComparison.Ordinal),
            "App is not delegating notification orchestration.");

        //
        // -----------------------------------------
        // Capability registry is metadata only.
        // -----------------------------------------
        //

        Reject(
            "Assistant/AssistantCapabilityRegistry.cs",
            "PreparedAssistantAction",
            "WorkflowRuntimeSession",
            "WindowsDesktop",
            ".PrepareAsync(",
            ".ExecuteAsync(");


        //
        // -----------------------------------------
        // App/MainWindow must not execute raw
        // desktop operations.
        // -----------------------------------------
        //

        Reject(
            "App.xaml.cs",
            ".Actions.ExecuteAsync",
            ".UiActions.",
            ".MouseActions.",
            ".UiTextActions.",
            ".KeyboardTextActions.",
            "WindowsDesktopUiActionExecutor",
            "WindowsDesktopMouseActionExecutor",
            "WindowsDesktopUiTextActionExecutor",
            "WindowsDesktopKeyboardTextActionExecutor");


        Reject(
            "MainWindow.xaml.cs",
            ".Actions.ExecuteAsync",
            ".UiActions.",
            ".MouseActions.",
            ".UiTextActions.",
            ".KeyboardTextActions.",
            "WindowsDesktopUiActionExecutor",
            "WindowsDesktopMouseActionExecutor",
            "WindowsDesktopUiTextActionExecutor",
            "WindowsDesktopKeyboardTextActionExecutor");


        //
        // -----------------------------------------
        // AssistantController remains coordinator.
        // -----------------------------------------
        //

        string controller =
            Read(
                "Assistant/AssistantController.cs");


        Require(
            controller.Contains(
                "Actions.PrepareAsync(",
                StringComparison.Ordinal),
            "AssistantController no longer owns action preparation boundary.");


        Require(
            controller.Contains(
                "Actions.ExecuteAsync(",
                StringComparison.Ordinal),
            "AssistantController no longer owns action execution boundary.");


        Require(
            controller.Contains(
                "WorkflowRuntime.ResolveCurrent(",
                StringComparison.Ordinal),
            "AssistantController bypassed reusable workflow runtime.");


        //
        // Scheduler may hand a skill to Assistant,
        // but never directly to an action executor.
        //

        Require(
            controller.Contains(
                "StartScheduledSkillAsync(",
                StringComparison.Ordinal),
            "Scheduled workflow handoff boundary disappeared.");


        //
        // Capability registry must be produced only
        // after skill/action/tool registration.
        //

        string services =
            Read(
                "Services/AppServices.cs");


        foreach (string forbiddenPublicSurface
                 in new[]
                 {
                     "public IDesktopActionExecutor ",
                     "public IDesktopWindowActionExecutor ",
                     "public IDesktopUiActionExecutor ",
                     "public IDesktopMouseActionExecutor ",
                     "public IDesktopUiTextActionExecutor ",
                     "public IDesktopKeyboardTextActionExecutor ",
                     "public IExplorerActionExecutor ",
                     "public IDesktopUiAutomationReader ",
                     "public IDesktopUiScreenEvidenceService ",
                     "public IDesktopUiAssistedResolver "
                 })
        {
            Require(
                !services.Contains(
                    forbiddenPublicSurface,
                    StringComparison.Ordinal),
                $"AppServices exposes raw desktop primitive: {forbiddenPublicSurface.Trim()}");
        }

        foreach (string requiredInjection
                 in new[]
                 {
                     "IDesktopActionExecutor? desktopExecutor",
                     "IExplorerActionExecutor? explorerExecutor",
                     "IDesktopWindowActionExecutor? windowExecutor",
                     "IDesktopUiAutomationReader? uiAutomation",
                     "IDesktopUiActionExecutor? uiActionExecutor",
                     "IDesktopMouseActionExecutor? mouseActionExecutor",
                     "IDesktopUiTextActionExecutor? uiTextActionExecutor",
                     "IDesktopKeyboardTextActionExecutor? keyboardTextActionExecutor",
                     "IDesktopUiScreenEvidenceService? uiScreenEvidenceService",
                     "IDesktopUiAssistedResolver? uiAssistedResolver"
                 })
        {
            Require(
                services.Contains(
                    requiredInjection,
                    StringComparison.Ordinal),
                $"AppServices lost injectable desktop dependency: {requiredInjection}");
        }

        Require(
            services.Contains(
                "new WindowsDesktopUiActionExecutor()",
                StringComparison.Ordinal) &&
            services.Contains(
                "new WindowsDesktopMouseActionExecutor()",
                StringComparison.Ordinal),
            "Native desktop executors are no longer composed at AppServices boundary.");

        // Verify the compiled public API as well, independent of source formatting.
        foreach (Type primitive in new[]
        {
            typeof(IDesktopActionExecutor), typeof(IDesktopWindowActionExecutor),
            typeof(IDesktopUiActionExecutor), typeof(IDesktopMouseActionExecutor),
            typeof(IDesktopUiTextActionExecutor), typeof(IDesktopKeyboardTextActionExecutor),
            typeof(IExplorerActionExecutor), typeof(IDesktopUiAutomationReader),
            typeof(IDesktopUiScreenEvidenceService), typeof(IDesktopUiAssistedResolver)
        })
        {
            Require(!typeof(AppServices).GetProperties().Any(property => primitive.IsAssignableFrom(property.PropertyType)) &&
                !typeof(AppServices).GetFields().Any(field => primitive.IsAssignableFrom(field.FieldType)),
                $"AppServices public API exposes raw desktop primitive: {primitive.Name}");
        }

        int skillRegistration =
            services.IndexOf(
                "Skills.Register(",
                StringComparison.Ordinal);

        int capabilityCreation =
            services.IndexOf(
                "AssistantCapabilityRegistry.Create(",
                StringComparison.Ordinal);


        Require(
            skillRegistration >= 0 &&
            capabilityCreation >
                skillRegistration,
            "Capability registry is created before user skill registration.");
    }
}
