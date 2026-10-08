using System.Reflection;
using LuKnight.Assistant;
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


        string physicsPerformance = Read("Physics/CharacterPhysicsController.cs");
        Require(physicsPerformance.Contains("UpdateRenderingSubscription", StringComparison.Ordinal) &&
                physicsPerformance.Contains("_renderingSubscribed", StringComparison.Ordinal),
            "Physics has no demand-driven render subscription.");
        string behaviorPerformance = Read("Behaviors/BehaviorController.cs");
        Require(behaviorPerformance.Contains("StopLoopTimers", StringComparison.Ordinal) &&
                behaviorPerformance.Contains("StartLoopTimers", StringComparison.Ordinal),
            "Behavior timers remain permanently active.");

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

        MethodInfo? routerExecute =
            typeof(AssistantActionRouter)
                .GetMethod(
                    "ExecuteAsync",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);


        Require(
            routerExecute is not null &&
            !routerExecute.IsPublic &&
            routerExecute.IsAssembly,
            "AssistantActionRouter execution endpoint is externally callable.");

        Require(
            !typeof(AppServices)
                .GetProperties(
                    BindingFlags.Instance |
                    BindingFlags.Public)
                .Any(
                    property =>
                        property.PropertyType ==
                            typeof(
                                AssistantActionRouter)),
            "AppServices publicly exposes AssistantActionRouter.");

        Require(
            !typeof(AssistantController)
                .GetProperties(
                    BindingFlags.Instance |
                    BindingFlags.Public)
                .Any(
                    property =>
                        property.PropertyType ==
                            typeof(
                                AssistantActionRouter)),
            "AssistantController publicly exposes its action router.");

        string[] productionSources =
            Directory
                .GetFiles(
                    root,
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(
                    path =>
                        !path.Contains(
                            $"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase) &&
                        !path.Contains(
                            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase) &&
                        !path.Contains(
                            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

        foreach (string sourcePath
                 in productionSources)
        {
            if (Path.GetFileName(
                    sourcePath)
                .Equals(
                    "AssistantController.cs",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }


            string source =
                File.ReadAllText(
                    sourcePath);


            Require(
                !source.Contains(
                    "_actions.ExecuteAsync(",
                    StringComparison.Ordinal) &&
                !source.Contains(
                    "Actions.ExecuteAsync(",
                    StringComparison.Ordinal),
                $"Production source bypasses AssistantController execution boundary: {sourcePath}");
        }

        string controller =
            Read(
                "Assistant/AssistantController.cs");


        Require(
            controller.Contains(
                "_actions.PrepareAsync(",
                StringComparison.Ordinal),
            "AssistantController no longer owns action preparation boundary.");


        Require(
            controller.Contains(
                "_actions.ExecuteAsync(",
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


        Require(
            !typeof(AppServices)
                .GetProperties()
                .Any(
                    property =>
                        property.PropertyType ==
                            typeof(
                                LocalScheduleStore)),
            "AppServices exposes LocalScheduleStore directly.");

        Require(
            services.Contains(
                "LocalScheduleStore? scheduleStore",
                StringComparison.Ordinal),
            "Scheduler store dependency is no longer injectable.");

        string notifications =
            Read(
                "Services/AssistantNotificationCoordinator.cs");


        Require(
            notifications.Contains(
                "HasUnacknowledgedDue(",
                StringComparison.Ordinal),
            "Proactive coordinator does not respect unacknowledged reminder priority.");

        Require(
            !notifications.Contains(
                "_reminderNotifiedThisSession",
                StringComparison.Ordinal),
            "Coordinator still suppresses successfully persisted reminders for the whole session.");

        string appServices =
            Read(
                "Services/AppServices.cs");


        Require(
            !appServices.Contains(
                "public AssistantActionRouter Actions",
                StringComparison.Ordinal),
            "AppServices restored public action execution surface.");

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

        string controllerSource =
            Read(
                "Assistant/AssistantController.cs");


        Require(
            controllerSource.Contains(
                "retainInConversationContext",
                StringComparison.Ordinal),
            "Explicit request-scoped context boundary is missing.");

        string desktopRouter =
            Read(
                "Assistant/LocalDesktopCommandRouter.cs");


        Require(
            desktopRouter.Contains(
                "IncludeInContext: false",
                StringComparison.Ordinal),
            "Local desktop router does not mark actions private.");

        string appSource =
            Read(
                "App.xaml.cs");

        string mainWindowSource =
            Read(
                "MainWindow.xaml.cs");

        string Compact(string source) => System.Text.RegularExpressions.Regex.Replace(source, @"\s+", "");
        string controllerLifecycle = Compact(controllerSource);
        Require(controllerLifecycle.Contains("publicvoidBeginShutdown(", StringComparison.Ordinal) &&
            controllerLifecycle.Contains("_shutdownCts", StringComparison.Ordinal),
            "AssistantController has no shutdown execution barrier.");
        Require(controllerLifecycle.Contains("cancellationToken.ThrowIfCancellationRequested();", StringComparison.Ordinal),
            "Native execution path lacks shutdown cancellation recheck.");
        Require(Compact(mainWindowSource).Contains("publicvoidBeginShutdown(", StringComparison.Ordinal) &&
            Compact(mainWindowSource).Contains("Services.Assistant.BeginShutdown(", StringComparison.Ordinal),
            "MainWindow does not propagate application shutdown.");
        Require(Compact(appSource).Contains("BeginApplicationShutdown(", StringComparison.Ordinal) &&
            Compact(appSource).Contains("_character?.BeginShutdown(", StringComparison.Ordinal),
            "Application exit does not cross MainWindow shutdown barrier.");

        string catalogSource = Read("Services/DesktopAppCatalog.cs");
        string warmupSource = Read("Services/DesktopAppIndexWarmup.cs");
        Require(catalogSource.Contains("LastRefreshSucceeded", StringComparison.Ordinal) &&
            catalogSource.Contains("_applications =", StringComparison.Ordinal),
            "Desktop app catalog lacks last-known-good refresh state.");
        Require(warmupSource.Contains("Action? degraded", StringComparison.Ordinal),
            "Desktop app warm-up cannot report isolated failure.");
        Require(typeof(AppServices).GetProperty("StartupIssues") is not null,
            "AppServices has no local degraded-startup diagnostics.");
        Require(Compact(appSource).Contains("PollAssistantNotifications()", StringComparison.Ordinal) &&
            appSource.Contains("Automatic update check", StringComparison.Ordinal),
            "Optional notification polling or updater lacks a startup isolation boundary.");

        string conversationSource = Read("Assistant/ConversationManager.cs");
        string memorySource = Read("Services/MemoryService.cs");
        string committedRecovery = Read("Services/CommittedStateRecovery.cs");
        Require(Compact(conversationSource).Contains("MaxSessionTurns=100", StringComparison.Ordinal) &&
            Compact(conversationSource).Contains("MaxContextTurns=20", StringComparison.Ordinal),
            "Conversation history bounds changed unexpectedly.");
        Require(Compact(memorySource).Contains("MaxEntries=200", StringComparison.Ordinal),
            "Long-term memory bound disappeared.");
        Require(Compact(committedRecovery).Contains("MaxInvalidSnapshots=3", StringComparison.Ordinal) &&
            committedRecovery.Contains("PruneInvalidSnapshots", StringComparison.Ordinal),
            "Corrupt-state forensic snapshots are unbounded.");

        string scheduleStoreSource =
            Read(
                "Services/LocalScheduleStore.cs");

        string skillStoreSource =
            Read(
                "Services/UserSkillStore.cs");


        foreach ((string name, string source)
                 in new[]
                 {
                     ("App.xaml.cs", appSource),
                     ("MainWindow.xaml.cs", mainWindowSource),
                     ("ProductSettingsViewModel.cs", Read("ViewModels/ProductSettingsViewModel.cs")),
                     ("SettingsViewModel.cs", Read("ViewModels/SettingsViewModel.cs")),
                     ("LocalScheduleStore.cs", scheduleStoreSource),
                     ("UserSkillStore.cs", skillStoreSource)
                 })
        {
            Require(
                !source.Contains(
                    "ex.Message",
                    StringComparison.Ordinal),
                $"{name} exposes raw exception messages.");
        }

        foreach (string viewModel in new[] { "ViewModels/ProductSettingsViewModel.cs", "ViewModels/SettingsViewModel.cs" })
            Require(!Read(viewModel).Contains("\" + ex", StringComparison.Ordinal),
                "Settings diagnostics expose full exception data.");

        Require(
            !appSource.Contains(
                "Trace.WriteLine($\"[Lu-Knight] Tray unavailable: {ex}\"",
                StringComparison.Ordinal),
            "Tray diagnostics log full exception.");

        Require(
            !mainWindowSource.Contains(
                "\" + ex)",
                StringComparison.Ordinal),
            "Voice diagnostics log full exception.");

        Require(
            !typeof(AppServices)
                .GetProperties(
                    BindingFlags.Instance |
                    BindingFlags.Public)
                .Any(
                    property =>
                        property.PropertyType ==
                            typeof(
                                AssistantToolRouter)),
            "AppServices exposes live assistant tools.");


        Require(
            !typeof(AppServices)
                .GetProperties(
                    BindingFlags.Instance |
                    BindingFlags.Public)
                .Any(
                    property =>
                        property.PropertyType ==
                            typeof(
                                AssistantContextSourceRouter)),
            "AppServices exposes live context capture router.");

        Require(
            !typeof(AssistantController)
                .GetProperties(
                    BindingFlags.Instance |
                    BindingFlags.Public)
                .Any(
                    property =>
                        property.PropertyType ==
                            typeof(
                                AssistantToolRouter)),
            "AssistantController exposes tool execution router.");


        Require(
            !typeof(AssistantController)
                .GetProperties(
                    BindingFlags.Instance |
                    BindingFlags.Public)
                .Any(
                    property =>
                        property.PropertyType ==
                            typeof(
                                AssistantContextSourceRouter)),
            "AssistantController exposes context capture router.");

        string controllerSecurity =
            Read(
                "Assistant/AssistantController.cs");


        Require(
            controllerSecurity.Contains(
                "_tools.ExecuteAsync(",
                StringComparison.Ordinal),
            "AssistantController no longer owns tool execution.");


        Require(
            controllerSecurity.Contains(
                "_contextSources.CaptureAsync(",
                StringComparison.Ordinal),
            "AssistantController no longer owns explicit context capture.");

        string servicesSecurity =
            Read(
                "Services/AppServices.cs");


        Require(
            !servicesSecurity.Contains(
                "ex.Message",
                StringComparison.Ordinal),
            "AppServices exposes raw exception diagnostics.");

        string settingsRecovery =
            Read(
                "Services/SettingsService.cs");

        string memoryRecovery =
            Read(
                "Services/MemoryService.cs");

        string scheduleRecovery =
            Read(
                "Services/LocalScheduleStore.cs");


        foreach ((string name, string source)
                 in new[]
                 {
                     ("SettingsService", settingsRecovery),
                     ("MemoryService", memoryRecovery),
                     ("LocalScheduleStore", scheduleRecovery)
                 })
        {
            Require(
                source.Contains(
                    "CommittedStateRecovery",
                    StringComparison.Ordinal),
                $"{name} does not participate in committed-state recovery.");
        }

        Require(
            !settingsRecovery.Contains(
                "ReadAllText(_path + \".tmp\")",
                StringComparison.Ordinal) &&
            !memoryRecovery.Contains(
                "ReadAllText(_path + \".tmp\")",
                StringComparison.Ordinal) &&
            !scheduleRecovery.Contains(
                "ReadAllText(_path + \".tmp\")",
                StringComparison.Ordinal),
            "Uncommitted temp state became a recovery source.");
    }
}
