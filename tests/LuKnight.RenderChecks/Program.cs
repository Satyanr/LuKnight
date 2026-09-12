using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LuKnight.Physics;
using LuKnight.Behaviors;
using LuKnight.Services;
using LuKnight.Views;
using LuKnight.Visuals;
using LuKnight.ViewModels;

internal static partial class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo AdvanceMethod = typeof(SpriteAnimationPlayer).GetMethod("Advance", Private)!;
    private static int _checks;

    [STAThread]
    private static void Main(string[] args)
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "LuKnight.csproj")))
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Repository not found");
        string output = Path.Combine(root, "output", "sprites");
        Directory.CreateDirectory(output);
        if (args.Contains("--assistant")) { CheckAssistant(); Console.WriteLine($"PASS: {_checks} assistant checks."); return; }
        if (args.Contains("--behavior-settings")) { CheckBehaviorSettings(); Console.WriteLine($"PASS: {_checks} behavior settings checks."); return; }
        if (args.Contains("--product")) { CheckProducts(); Console.WriteLine($"PASS: {_checks} product checks."); return; }
        if (args.Contains("--startup")) { CheckStartup(); Console.WriteLine($"PASS: {_checks} startup checks."); return; }
        if (args.Contains("--settings")) { CheckSettings(output); Console.WriteLine($"PASS: {_checks} settings checks."); return; }
        if (args.Contains("--physics")) { CheckThrowAndGrounding(output); Console.WriteLine($"PASS: {_checks} throw/input/grounding checks."); return; }
        if (args.Contains("--tray")) { CheckTray(); Console.WriteLine($"PASS: {_checks} tray checks."); return; }
        if (args.Contains("--attention")) { CheckAttentionAndIdle(output); Console.WriteLine($"PASS: {_checks} attention checks."); return; }
        CheckRigAndRendering(output);
        CheckLifecycle();
        if (args.Contains("--desktop"))
        {
            CheckGrabAndTargetReset();
            CheckWalkingCadence();
        }
        else Console.WriteLine("SKIP: native cursor/window checks (run with --desktop in an interactive Windows session).");
        CheckCadence();
        CheckHeadRegistration();
        CheckPuppet(output);
        CheckAttentionAndIdle(output);
        CheckTray();
        CheckThrowAndGrounding(output);
        CheckStartup();
        CheckBehaviorSettings();
        CheckProducts();
        CheckAssistant();
        CheckSettings(output);
        Console.WriteLine($"PASS: {_checks} checks (sprite scale, transparent bounds, state/mood transitions, cadence, lifecycle).");
    }

    private static T Get<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private | BindingFlags.Public)!.GetValue(obj)!;

    private sealed class MemoryStartupStore : IStartupStore
    {
        public string? Command;
        public bool Hidden, DenyWrites, DenyReads;
        public int Writes;
        public string? ReadCommand() { if (DenyReads) throw new UnauthorizedAccessException("Test read denied"); return Command; }
        public void WriteCommand(string command) { CheckWrite(); Command = command; }
        public void DeleteCommand() { CheckWrite(); Command = null; }
        public bool ReadStartHidden() => Hidden;
        public void WriteStartHidden(bool hidden) { CheckWrite(); Hidden = hidden; }
        private void CheckWrite() { if (DenyWrites) throw new UnauthorizedAccessException("Test write denied"); Writes++; }
    }

    private static void CheckBehaviorSettings()
    {
        static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private)!.SetValue(owner, value);
        static object? Call(object owner, string method, params object?[] arguments) => owner.GetType().GetMethod(method, Private)!.Invoke(owner, arguments);
        var defaults = new BehaviorOptions();
        var preferences = new BehaviorSettings(); int changes = 0; preferences.Changed += _ => changes++;
        preferences.Apply(defaults); Require(changes == 0, "Reading/applying defaults emits changes");
        preferences.Apply(defaults with { Enabled = false }); preferences.Reset();
        Require(changes == 2 && preferences.Current == defaults, "Reset behavior does not restore every preference");
        try { preferences.Apply(defaults with { Speed = (MovementSpeed)99 }); throw new Exception("Invalid speed accepted"); }
        catch (ArgumentOutOfRangeException) { Require(preferences.Current == defaults, "Invalid options changed runtime"); }
        var view = new CharacterView(); Render(view);
        var window = new Window { Width = 190, Height = 240 };
        using (var controller = new BehaviorController(window, view))
        {
            Call(controller, "StartWalking"); Require(view.CurrentState == CharacterState.Walk, "Default walking disabled");
            controller.ApplySettings(defaults with { Enabled = false });
            Require(view.CurrentState == CharacterState.Idle && !Get<bool>(controller, "_walking"), "Disabling autonomy does not stop current walk");
            for (int i = 0; i < 10; i++) Call(controller, "ChooseNextBehavior");
            Require(view.CurrentState == CharacterState.Idle && !controller.IsPaused, "Autonomy switch hijacks pause flags or starts decisions");
            controller.ReactToClick(); Require(view.CurrentMood == CharacterMood.Surprised, "Autonomy OFF blocks click reaction");
            controller.SetThinking(true); Require(view.CurrentMood == CharacterMood.Thinking, "Autonomy OFF blocks chat thinking"); controller.SetThinking(false);
            controller.ObservePointer(new Point(180, 100));
            Settle(Step(Get<SpriteAnimationPlayer>(view, "_spritePlayer")), .5);
            Require(Math.Abs(Get<SpriteAnimationPlayer>(view, "_spritePlayer").Face!.LookX) > .2, "Autonomy OFF blocks cursor tracking");
            foreach (var state in new[] { CharacterState.Grabbed, CharacterState.Falling })
            {
                view.SetState(state); controller.Pause(BehaviorPauseReason.Physics);
                controller.ApplySettings(defaults with { Enabled = false, Speed = MovementSpeed.Fast });
                Require(view.CurrentState == state && controller.IsPaused, "Settings interrupted physics/drag: " + state);
            }
            // Remove synthetic pause without invoking native grounding.
            Set(controller, "_pauseReasons", BehaviorPauseReason.None); view.SetState(CharacterState.Idle);
            controller.ApplySettings(defaults with { ReactToCursor = false });
            Call(controller, "StartWalking"); controller.ObservePointer(new Point(180, 100));
            Require(view.CurrentState == CharacterState.Walk && !Get<bool>(controller, "_cursorNearby"), "Look-only cursor mode stops walking/reacts");
            controller.ApplySettings(defaults with { LookAtCursor = false, ReactToCursor = true });
            controller.ObservePointer(new Point(180, 100));
            Require(view.CurrentState == CharacterState.Idle && Get<bool>(controller, "_cursorNearby"), "React-only cursor mode is disabled with gaze");
            controller.ApplySettings(defaults with { LookAtCursor = false, ReactToCursor = false });
            Settle(Step(Get<SpriteAnimationPlayer>(view, "_spritePlayer")), 1.5);
            controller.ObservePointer(new Point(180, 100));
            Settle(Step(Get<SpriteAnimationPlayer>(view, "_spritePlayer")), .5);
            Require(Math.Abs(Get<SpriteAnimationPlayer>(view, "_spritePlayer").Face!.LookX) < .02, "Disabled gaze keeps following the pointer");
            foreach (var delay in Enum.GetValues<SleepDelay>())
            {
                controller.ApplySettings(defaults with { SleepAfter = delay, WakeAtCursor = false });
                Require(Get<TimeSpan>(controller, "_sleepAfter") == (defaults with { SleepAfter = delay }).SleepThreshold, "Sleep threshold not applied: " + delay);
                Call(controller, "EnterSleep");
                Require(Get<bool>(controller, "_sleeping") == (delay != SleepDelay.Never), "Sleep Never is ignored");
                if (delay != SleepDelay.Never)
                {
                    controller.ObservePointer(new Point(view.ActualWidth / 2, view.ActualHeight * .6));
                    Require(Get<bool>(controller, "_sleeping"), "Disabled cursor wake still wakes");
                    controller.ApplySettings(controller.Options with { WakeAtCursor = true, ReactToCursor = false, LookAtCursor = false });
                    controller.ObservePointer(new Point(view.ActualWidth / 2, view.ActualHeight * .6));
                    Require(!Get<bool>(controller, "_sleeping"), "Cursor wake incorrectly depends on gaze/reaction");
                }
            }
            foreach (var nap in Enum.GetValues<NapDuration>())
            {
                controller.ApplySettings(defaults with { Nap = nap }); Call(controller, "EnterSleep");
                double seconds = (Get<DateTime>(controller, "_sleepUntil") - DateTime.UtcNow).TotalSeconds;
                double factor = (defaults with { Nap = nap }).NapFactor;
                Require(seconds >= 7 * factor - .2 && seconds <= 15 * factor, "Nap duration not applied: " + nap);
                controller.ApplySettings(controller.Options with { AllowSleep = false });
                Require(!Get<bool>(controller, "_sleeping") && view.CurrentState == CharacterState.Idle, "Disabling sleep does not wake the pet");
            }
            foreach (var speed in Enum.GetValues<MovementSpeed>())
            {
                controller.ApplySettings(defaults with { Speed = speed });
                var actual = (double)typeof(BehaviorController).GetProperty("CurrentWalkSpeed", Private)!.GetValue(controller)!;
                Require(Math.Abs(actual - 70 * (defaults with { Speed = speed }).SpeedFactor) < .001, "Movement speed is not connected: " + speed);
            }
            Require((defaults with { Activity = ActivityLevel.Calm }).ActivityDelay > defaults.ActivityDelay &&
                (defaults with { Activity = ActivityLevel.Active }).ActivityDelay < defaults.ActivityDelay, "Activity pacing order is reversed");
            var surface = Get<SurfaceBehaviorController>(controller, "_surfaceController");
            var actionType = surface.GetType().GetField("_surfaceAction", Private)!.FieldType;
            void Action(string action) => Set(surface, "_surfaceAction", Enum.Parse(actionType, action));
            int falls = 0; controller.SupportLost += () => falls++;
            foreach (string action in new[] { "Hanging", "SideClimbingDown", "SideHolding", "SideClimbingUp", "ClimbingUp", "JumpPreparing" })
            {
                controller.ApplySettings(defaults); Set(surface, "_supportWindowHandle", new IntPtr(123)); Action(action);
                controller.ApplySettings(defaults with { Enabled = false });
                Require(!surface.IsBusy && !surface.HasSupport, "Cancelled attachment is stuck: " + action);
            }
            Require(falls == 6, "Cancelled side attachments do not hand off to physics");
            controller.ApplySettings(defaults with { ExploreWindows = false });
            Call(surface, "BeginEdgePause", 1);
            Require(!surface.IsBusy, "Explore OFF still begins an edge adventure");
            Require(!(bool)Call(surface, "BeginTargetJump", DateTime.UtcNow, false)!, "Explore OFF permits a jump");
            controller.ApplySettings(defaults with { JumpBetweenWindows = false });
            Require(!(bool)Call(surface, "BeginTargetJump", DateTime.UtcNow, false)!, "Jump OFF permits a jump");
            controller.ApplySettings(defaults with { HangingClimbing = false }); Call(surface, "BeginHanging", DateTime.UtcNow);
            Require(!surface.IsBusy, "Climbing OFF permits hanging");
            Set(surface, "_supportWindowHandle", new IntPtr(123)); Action("JumpPreparing");
            controller.ApplySettings(defaults with { HangingClimbing = false, Speed = MovementSpeed.Fast });
            Require(surface.IsBusy && surface.HasSupport, "Changing speed with climbing OFF cancels an allowed jump");
            controller.ApplySettings(defaults with { JumpBetweenWindows = false });
            Require(!surface.IsBusy && !surface.HasSupport, "Jump OFF leaves a prepared jump pending");
            Set(controller, "_lastInteractionAt", DateTime.UtcNow.AddHours(-1));
            controller.ApplySettings(defaults with { Enabled = false }); controller.ApplySettings(defaults);
            Require((DateTime.UtcNow - Get<DateTime>(controller, "_lastInteractionAt")).TotalSeconds < 1, "Re-enabling autonomy immediately falls asleep from stale inactivity");
        }
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); window.Close();
        var host = new LuKnight.MainWindow(); var hostView = Get<CharacterView>(host, "CharacterControl"); Render(hostView);
        using (var behavior = new BehaviorController(host, hostView))
        using (var physics = new CharacterPhysicsController(host, hostView))
        {
            Set(host, "_behaviorController", behavior); Set(host, "_physicsController", physics);
            physics.StartFall(420, -300, new IntPtr(123)); behavior.Pause(BehaviorPauseReason.Physics);
            host.BehaviorSettings.Apply(defaults with { Enabled = false, Speed = MovementSpeed.Fast, JumpBetweenWindows = false });
            Require(!behavior.Options.Enabled && behavior.Options.Speed == MovementSpeed.Fast, "MainWindow does not apply live preferences to its controller");
            Require(physics.IsActive && Get<double>(physics, "_velocityX") == 420 && Get<double>(physics, "_velocityY") == -300 &&
                Get<nint>(physics, "_targetWindowHandle") == new IntPtr(123) && hostView.CurrentState == CharacterState.Falling,
                "Live behavior settings alter an airborne trajectory");
            host.BehaviorSettings.Reset(); Require(behavior.Options == defaults, "Runtime reset leaves stale behavior options");
        }
        hostView.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); host.Close();
    }

    private static void CheckStartup()
    {
        var store = new MemoryStartupStore();
        const string exe = @"C:\My Apps\LuKnight\LuKnight.exe";
        var startup = new StartupService(store, exe, @"C:\My Apps\LuKnight\LuKnight.dll", _ => true);
        Require(!startup.IsEnabled() && !startup.ReadStatus().StartHidden, "Startup must be opt-in by default");
        Require(!startup.Validate() && store.Writes == 0, "Validation silently enables autostart");
        startup.SetStartHidden(true);
        Require(!startup.IsEnabled() && startup.ReadStatus().StartHidden, "Hidden preference unexpectedly enables login startup");
        startup.Enable();
        Require(store.Command == "\"" + exe + "\" --startup", "Run command is not absolute/quoted or lacks --startup");
        int writes = store.Writes;
        Require(!startup.Validate() && writes == store.Writes, "Validation rewrites a correct entry");
        store.Command = "\"C:\\Old Version\\LuKnight.exe\" --startup";
        Require(startup.Validate() && store.Command!.Contains(exe) && store.Hidden, "Moved executable path was not repaired");
        var reloaded = new StartupService(store, exe, "unused.dll", _ => true);
        Require(reloaded.IsEnabled() && reloaded.ReadStatus().StartHidden, "Startup preferences do not survive service recreation");
        startup.Disable();
        Require(store.Command is null && store.Hidden, "Disable removes the hidden preference or retains startup registration");
        Require(!startup.Validate() && !startup.IsEnabled(), "Validation re-enables a user-disabled startup entry");

        foreach (var args in new[] { Array.Empty<string>(), new[] { "--start" }, new[] { "--startup=false" } })
            Require(!StartupService.ShouldStartHidden(args, true, true), "Manual/unrecognized launch is hidden");
        Require(StartupService.ShouldStartHidden(new[] { "--STARTUP" }, true, true), "Login launch ignores hidden preference");
        Require(!StartupService.ShouldStartHidden(new[] { "--startup" }, false, true), "Visible startup preference is ignored");
        Require(!StartupService.ShouldStartHidden(new[] { "--startup" }, true, false), "Failed tray creates an inaccessible hidden application");
        Require(StartupService.BuildCommand(@"C:\Program Files\dotnet\dotnet.exe", @"C:\My Apps\LuKnight.dll") ==
            "\"C:\\Program Files\\dotnet\\dotnet.exe\" \"C:\\My Apps\\LuKnight.dll\" --startup", "Hosted launch omits/incorrectly quotes the assembly");
        foreach (string path in new[] { "LuKnight.exe", "C:\\bad\"path\\LuKnight.exe", "C:\\" + new string('a', 260) + "\\LuKnight.exe" })
        {
            bool rejected = false; try { StartupService.BuildCommand(path, "unused"); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Invalid/overlong startup command was accepted");
        }
        var missing = new StartupService(store, exe, "unused", _ => false);
        bool missingRejected = false; try { missing.Enable(); } catch (InvalidOperationException) { missingRejected = true; }
        Require(missingRejected && store.Command is null, "Missing executable gets registered for login");
        var missingAssembly = new StartupService(store, @"C:\dotnet\dotnet.exe", @"C:\LuKnight.dll", path => path.EndsWith(".exe"));
        missingRejected = false; try { missingAssembly.Enable(); } catch (InvalidOperationException) { missingRejected = true; }
        Require(missingRejected, "Hosted startup accepts a missing application DLL");

        var runtime = new SettingsRuntime(true, true, "Sprite", "Idle", false, "", ChatStatus.Local, false, true);
        var model = new SettingsViewModel(() => runtime, _ => { }, _ => { }, () => { }, startup);
        Require(model.StartupAvailable && !model.StartWithWindows && model.StartHidden, "Settings does not read persisted startup preferences");
        store.DenyWrites = true; model.StartWithWindows = true;
        Require(!model.StartWithWindows && model.StartupMessage.Contains("gagal"), "Failed registry write leaves a checked startup toggle");
        model.StartHidden = false;
        Require(model.StartHidden, "Failed hidden preference write is shown as successful");
        store.DenyWrites = false; model.StartWithWindows = true;
        Require(model.StartWithWindows && !model.StartupMessage.Contains("gagal"), "Startup cannot recover from a denied write");
        startup.Disable(); model.Refresh();
        Require(!model.StartWithWindows, "External removal of Run entry is not reflected in Settings");
        store.DenyReads = true; model.Refresh();
        Require(!model.StartupAvailable && !startup.ReadStatus().Available, "Registry read failure crashes or exposes usable controls");
        store.DenyReads = false; model.Refresh();
        Require(model.StartupAvailable, "Startup controls remain disabled after access recovers");
        Console.WriteLine("Startup checks use an in-memory store; no Windows Run entries or user preferences were changed.");
    }

    private static void CheckSettings(string output)
    {
        var runtime = new SettingsRuntime(true, false, "Sprite", "Idle", false, "", ChatStatus.Local, false, true);
        int topChanges = 0, visibilityChanges = 0, resets = 0;
        var startup = new StartupService(new MemoryStartupStore(), @"C:\LuKnight\LuKnight.exe", "unused", _ => true);
        var model = new SettingsViewModel(() => runtime,
            value => { topChanges++; runtime = runtime with { AlwaysOnTop = value }; },
            value => { visibilityChanges++; runtime = runtime with { Visible = value }; },
            () => { resets++; runtime = runtime with { Visible = true, State = "Idle" }; }, startup);
        var uiCredentials = new FakeCredentials();
        var uiServices = new AppServices(new SettingsService(), uiCredentials);
        model.Product = new ProductSettingsViewModel(uiServices, uiServices.Chat.ClearConversation, () => true, () => true, () => { });
        Require(model.Provider == "Local fallback" && model.ApiStatus.Contains("belum dikonfigurasi"), "Settings misreports unconfigured AI as connected");
        Require(model.ApplicationStatus.Contains("Hidden"), "Settings does not report hidden mascot");
        var settings = new SettingsWindow(model);
        var root = Get<Grid>(settings, "SettingsRoot");
        void Layout(double width = 860, double height = 640)
        {
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        }
        Layout();
        Get<CheckBox>(settings, "AlwaysOnTopCheck").IsChecked = false;
        Require(!runtime.AlwaysOnTop && topChanges == 1, "Always-on-top checkbox is not connected to runtime");
        Get<CheckBox>(settings, "ShowCharacterCheck").IsChecked = true;
        Require(runtime.Visible && visibilityChanges == 1, "Show checkbox does not restore the mascot");
        runtime = runtime with { AlwaysOnTop = true, Visible = false, Renderer = "Vector" }; model.Refresh(); Layout();
        Require(Get<CheckBox>(settings, "AlwaysOnTopCheck").IsChecked == true && Get<CheckBox>(settings, "ShowCharacterCheck").IsChecked == false,
            "Settings controls do not reflect changes made outside the window");
        Require(topChanges == 1 && visibilityChanges == 1, "Refreshing status triggers setting commands recursively");
        Require(Get<TextBlock>(settings, "RendererText").Text == "Vector", "Renderer status is hard-coded instead of showing fallback");
        model.ResetPositionCommand.Execute(null);
        Require(resets == 1 && runtime.Visible, "Reset position command is not wired");
        runtime = runtime with { CanReset = false }; model.Refresh(); model.ResetPositionCommand.Execute(null);
        Require(resets == 1 && !model.ResetPositionCommand.CanExecute(null), "Reset runs before mascot controllers are ready");
        runtime = runtime with { UsesGemini = true, Model = "test-model", ChatStatus = ChatStatus.Ready, CanReset = true }; model.Refresh();
        Require(model.Model == "test-model" && model.ApiStatus.Contains("belum terverifikasi"), "Configured key is falsely treated as a verified API connection");
        runtime = runtime with { Sending = true }; model.Refresh();
        Require(model.AiStatus.Contains("memproses"), "AI busy state is stale");
        runtime = runtime with { Sending = false, ChatStatus = ChatStatus.Error }; model.Refresh();
        Require(model.AiStatus.Contains("gagal") && model.Provider == "Gemini", "AI error claims that unimplemented fallback is active");
        runtime = runtime with { ChatStatus = ChatStatus.Connected }; model.Refresh();
        Require(model.ApiStatus.Contains("terakhir berhasil"), "Settings does not reflect a successful API response");
        Require(Get<CheckBox>(settings, "StartupCheck").IsEnabled && Get<CheckBox>(settings, "StartHiddenCheck").IsEnabled &&
            Get<Button>(settings, "CheckUpdatesButton").IsEnabled, "Startup/update controls are disabled");
        Get<CheckBox>(settings, "StartupCheck").IsChecked = true;
        Get<CheckBox>(settings, "StartHiddenCheck").IsChecked = true;
        Require(startup.IsEnabled() && startup.ReadStatus().StartHidden, "Startup checkbox bindings are not connected to the service");
        Get<ComboBox>(settings, "ProviderCombo").SelectedItem = LuKnight.Models.ChatProvider.Local;
        Require(uiServices.Chat.Options.Provider == LuKnight.Models.ChatProvider.Local && uiServices.Settings.Current.Chat.Provider == LuKnight.Models.ChatProvider.Local,
            "Provider binding does not apply or persist");
        Get<PasswordBox>(settings, "ApiKeyInput").Password = "fake-ui-key-for-test";
        typeof(SettingsWindow).GetMethod("UpdateKey_Click", Private)!.Invoke(settings, new object[] { settings, new RoutedEventArgs() });
        Require(uiCredentials.Key == "fake-ui-key-for-test" && Get<PasswordBox>(settings, "ApiKeyInput").Password == "" && !model.Product.CredentialStatus.Contains(uiCredentials.Key),
            "Key update failed, retained input, or exposes stored value");
        model.Product.RemoveKeyCommand.Execute(null);
        Require(uiCredentials.Key is null, "Remove Key UI command does not remove saved credential");
        Get<CheckBox>(settings, "AutonomousCheck").IsChecked = false;
        Require(!model.AutonomousEnabled && Get<CheckBox>(settings, "LookCursorCheck").IsEnabled, "Autonomy binding disables manual cursor preferences");
        Get<CheckBox>(settings, "AutonomousCheck").IsChecked = true;
        Get<ComboBox>(settings, "ActivityCombo").SelectedItem = ActivityLevel.Active;
        Get<ComboBox>(settings, "SpeedCombo").SelectedItem = MovementSpeed.Fast;
        Get<ComboBox>(settings, "SleepAfterCombo").SelectedValue = SleepDelay.FiveMinutes;
        Get<ComboBox>(settings, "NapCombo").SelectedItem = NapDuration.Long;
        Require(model.Activity == ActivityLevel.Active && model.Speed == MovementSpeed.Fast && model.SleepAfter == SleepDelay.FiveMinutes && model.Nap == NapDuration.Long,
            "Behavior dropdowns are not bound");
        Get<CheckBox>(settings, "ExploreWindowsCheck").IsChecked = false;
        Require(!model.ExploreWindows && !Get<CheckBox>(settings, "JumpWindowsCheck").IsEnabled, "Adventure dependencies are stale");
        model.ResetBehaviorCommand.Execute(null);
        Require(model.AutonomousEnabled && model.ExploreWindows && model.Activity == ActivityLevel.Balanced && model.Speed == MovementSpeed.Normal && model.SleepAfter == SleepDelay.TwoMinutes,
            "Reset behavior does not refresh UI");
        Require(!string.IsNullOrWhiteSpace(model.Version) && !string.IsNullOrWhiteSpace(model.Build) &&
            model.RepositoryUrl == "https://github.com/Satyanr/LuKnight", "About metadata is missing");

        runtime = runtime with { UsesGemini = false, ChatStatus = ChatStatus.Local, Model = "", Renderer = "Sprite" }; model.Refresh();
        string[] sections = { "General", "Behavior", "AI & Chat", "About" };
        string[] panels = { "GeneralPanel", "BehaviorPanel", "AiPanel", "AboutPanel" };
        var navigation = Get<ListBox>(settings, "Navigation");
        for (int i = 0; i < sections.Length; i++)
        {
            navigation.SelectedValue = sections[i]; Layout();
            Require(model.SelectedSection == sections[i] && panels.Count(p => Get<StackPanel>(settings, p).Visibility == Visibility.Visible) == 1,
                "Settings navigation shows incorrect/multiple pages: " + sections[i]);
            Require(Get<StackPanel>(settings, panels[i]).Visibility == Visibility.Visible, "Selected page is not visible");
            var bitmap = new RenderTargetBitmap(860, 640, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            Save(bitmap, Path.Combine(output, "settings-" + i + ".png"));
        }
        navigation.SelectedValue = "AI & Chat"; Layout(760, 520);
        var aiScroll = Get<ScrollViewer>(settings, "PageScroll"); aiScroll.ScrollToEnd(); Layout(760, 520);
        Require(aiScroll.ScrollableHeight > 0 && aiScroll.VerticalOffset > 0, "AI settings cannot scroll to conversation controls");
        var aiBottom = new RenderTargetBitmap(760, 520, 96, 96, PixelFormats.Pbgra32); aiBottom.Render(root);
        Save(aiBottom, Path.Combine(output, "settings-ai-bottom.png"));
        navigation.SelectedValue = "Behavior"; Layout(760, 520);
        var behaviorScroll = Get<ScrollViewer>(settings, "PageScroll"); behaviorScroll.ScrollToEnd(); Layout(760, 520);
        Require(behaviorScroll.VerticalOffset > 0 && Get<Button>(settings, "ResetBehaviorButton").TransformToAncestor(root).Transform(new Point()).Y < 480,
            "Behavior reset is inaccessible at minimum window size");
        var behaviorBottom = new RenderTargetBitmap(760, 520, 96, 96, PixelFormats.Pbgra32); behaviorBottom.Render(root);
        Save(behaviorBottom, Path.Combine(output, "settings-behavior-bottom.png"));
        navigation.SelectedValue = "General"; Layout(760, 520);
        var scroll = Get<ScrollViewer>(settings, "PageScroll"); scroll.ScrollToEnd(); Layout(760, 520);
        Require(scroll.ScrollableHeight > 0 && scroll.VerticalOffset > 0, "General page becomes inaccessible at minimum window size");
        var timer = Get<System.Windows.Threading.DispatcherTimer>(settings, "_refreshTimer");
        timer.Start(); settings.Close();
        Require(!timer.IsEnabled, "Closing Settings leaks its refresh timer");

        var app = new LuKnight.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var mascot = new LuKnight.MainWindow(); bool mascotClosed = false; mascot.Closed += (_, _) => mascotClosed = true;
        typeof(LuKnight.App).GetField("_character", Private)!.SetValue(app, mascot); app.MainWindow = mascot;
        var create = typeof(LuKnight.App).GetMethod("GetOrCreateSettingsWindow", Private)!;
        var first = (SettingsWindow)create.Invoke(app, null)!;
        var second = (SettingsWindow)create.Invoke(app, null)!;
        Require(ReferenceEquals(first, second), "Opening Settings creates duplicate windows");
        Require(first.Owner is null && !mascot.IsVisible, "Settings is owned by/shows the hidden mascot");
        first.Model.AutonomousEnabled = false;
        first.Model.Speed = MovementSpeed.Slow;
        Require(!mascot.BehaviorSettings.Current.Enabled && mascot.BehaviorSettings.Current.Speed == MovementSpeed.Slow && !mascot.IsVisible,
            "Changing hidden mascot behavior opens it or loses preferences");
        first.Close();
        Require(!mascotClosed && Get<SettingsWindow?>(app, "_settingsWindow") is null, "Closing Settings closes the mascot or leaves a stale singleton");
        var reopened = (SettingsWindow)create.Invoke(app, null)!;
        Require(!ReferenceEquals(first, reopened), "Closed Settings cannot be reopened");
        Require(!reopened.Model.AutonomousEnabled && reopened.Model.Speed == MovementSpeed.Slow, "Reopening Settings loses behavior preferences");
        reopened.Close();
        Get<CharacterView>(mascot, "CharacterControl").RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); mascot.Close();

        var view = new CharacterView();
        using (var physics = new CharacterPhysicsController(new Window(), view))
        {
            typeof(CharacterPhysicsController).GetField("_isFalling", Private)!.SetValue(physics, true);
            typeof(CharacterPhysicsController).GetField("_velocityY", Private)!.SetValue(physics, 900.0);
            typeof(CharacterPhysicsController).GetField("_targetWindowHandle", Private)!.SetValue(physics, new IntPtr(123));
            physics.ResetMotion();
            Require(!physics.IsActive && Get<double>(physics, "_velocityY") == 0 && Get<nint>(physics, "_targetWindowHandle") == nint.Zero,
                "Position reset leaves an active airborne trajectory");
        }
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        app.Shutdown();
    }

    private static void CheckThrowAndGrounding(string output)
    {
        foreach (int hz in new[] { 30, 60, 120, 144, 1000 })
        foreach (int direction in new[] { -1, 1 })
        {
            var tracker = new PointerVelocityTracker(); tracker.Reset(new Point(), 0);
            for (int i = 1; i <= hz; i++) tracker.Add(new Point(direction * 900.0 * i / hz, -500.0 * i / hz), (double)i / hz);
            tracker.Add(new Point(direction * 900, -500), 1.002);
            Require(tracker.Velocity.X * direction > 850 && tracker.Velocity.Y < -450, $"Final stationary sample erased throw at {hz} Hz");
            tracker.Add(new Point(direction * 900, -500), 1.2);
            Require(tracker.Velocity.Length < .01, "Holding still before release retains stale throw momentum");
        }
        var view = new CharacterView(); Render(view);
        var window = new Window { Left = 0, Top = 0, Width = 190, Height = 240 };
        Point cursor = new(100, 100); double clock = 0;
        using (var physics = new CharacterPhysicsController(window, view, () => cursor, () => clock))
        {
            view.SetState(CharacterState.Walk);
            physics.PrepareGrab(cursor, clock);
            clock = .006; cursor = new Point(108, 96); physics.SamplePointer(cursor);
            Require(physics.BeginGrab(new Point(100, 100), 0), "Walking mascot cannot start a grab");
            Require(view.CurrentState == CharacterState.Grabbed, "Grab does not own the physical state");
            clock = .012; cursor = new Point(120, 88); physics.EndGrab();
            Require(physics.IsFalling && !physics.IsGrabbed && view.CurrentState == CharacterState.Falling, "Release does not enter ballistic motion");
            Require(Get<double>(physics, "_velocityX") > 900 && Get<double>(physics, "_velocityY") < -500, "A flick shorter than one render frame cannot throw");
            clock = 1; cursor = new Point(100, 100); physics.PrepareGrab(cursor, clock);
            Require(physics.BeginGrab(cursor, clock), "Second grab cannot start");
            for (int i = 1; i <= 12; i++) { clock = 1 + i / 60.0; cursor = new Point(100 - 900 * i / 60.0, 100 - 500 * i / 60.0); physics.UpdateGrab(); }
            clock += .002; physics.EndGrab();
            Require(Get<double>(physics, "_velocityX") < -850 && Get<double>(physics, "_velocityY") < -450, "Left/up throw lost its direction or speed at release");
            double airborneVelocity = Get<double>(physics, "_velocityY");
            physics.PrepareGrab(cursor, clock); clock += .02; physics.SamplePointer(cursor); physics.CancelPreparedGrab();
            Require(Get<double>(physics, "_velocityY") == airborneVelocity && !Get<bool>(physics, "_trackingPointer"), "Click without drag changes airborne momentum or leaves pending capture");
        }

        foreach (double dpi in new[] { 1, 1.25, 1.5, 2 })
        {
            double foot = CharacterGrounding.FootOffsetInPixels(10, 220, dpi);
            double surface = 1000;
            Require(Math.Abs((surface - foot) + (10 + 638.0 / 3) * dpi - surface) < .001, "Feet do not meet the desktop/window top at DPI " + dpi);
            Require(240 * dpi - foot > 15 * dpi, "Transparent window padding was not removed from the floor anchor");
        }
        var samples = new List<(string Name, BitmapSource Image)>();
        var rig = new SpritePuppet(SpritePuppetMotion.Walk);
        for (int i = 0; i < 24; i++)
        {
            rig.Advance(i * .8 / 24);
            var bitmap = Render(new Image { Source = rig.Image });
            byte[] pixels = new byte[510 * 660 * 4]; bitmap.CopyPixels(pixels, 510 * 4, 0);
            int bottom = -1;
            for (int y = 0; y < 660; y++) for (int x = 0; x < 510; x++) if (pixels[(y * 510 + x) * 4 + 3] > 200) bottom = y;
            Require(bottom >= 636 && bottom <= 639, $"Walking sole floats/sinks at phase {i}: y={bottom}");
            if (i % 6 == 0)
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(bitmap, new Rect(0, 0, 510, 660));
                    dc.DrawLine(new Pen(Brushes.Turquoise, 2), new Point(20, 639), new Point(490, 639));
                }
                var grounded = new RenderTargetBitmap(510, 660, 96, 96, PixelFormats.Pbgra32); grounded.Render(visual);
                samples.Add(($"Walk ground {i / 6}", grounded));
            }
        }
        foreach (var state in new[] { CharacterState.Walk, CharacterState.Idle, CharacterState.Grabbed })
        {
            view.SetState(state);
            Step(Get<SpriteAnimationPlayer>(view, "_spritePlayer"))(.025);
            var bitmap = Render(view); byte[] pixels = new byte[510 * 660 * 4]; bitmap.CopyPixels(pixels, 510 * 4, 0);
            Require(Enumerable.Range(0, 510 * 660).All(p => pixels[p * 4 + 3] > 0), "Layered-window input falls through transparent pixels during " + state);
        }
        SaveContactSheet(samples, Path.Combine(output, "ground-contact.png"));
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); window.Close();
    }

    private static void CheckTray()
    {
        int toggled = 0, shown = 0, chat = 0, settings = 0, restarted = 0, exited = 0;
        using var tray = new TrayIconService(() => toggled++, () => shown++, () => chat++, () => settings++, () => restarted++, () => exited++);
        var items = tray.Menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().ToArray();
        Require(items.Select(i => i.Text).SequenceEqual(new[] { "Hide Lu-Knight", "Open Chat", "Settings", "Restart Lu-Knight", "Exit" }), "Tray commands are missing or out of order");
        foreach (var item in items) item.PerformClick();
        Require(toggled == 1 && chat == 1 && settings == 1 && restarted == 1 && exited == 1, "Tray menu does not dispatch each command exactly once");
        tray.SetCharacterVisible(false);
        Require(items[0].Text == "Show Lu-Knight", "Hidden mascot cannot be restored from menu");
        tray.SetCharacterVisible(true);
        Require(items[0].Text == "Hide Lu-Knight", "Tray visibility label is stale");
        var icon = Get<System.Windows.Forms.NotifyIcon>(tray, "_icon");
        Require(icon.Icon is not null && icon.Text == "Lu-Knight", "Shell icon/tooltip is missing");
        var doubleClick = typeof(System.Windows.Forms.NotifyIcon).GetMethod("OnMouseDoubleClick", Private)!;
        doubleClick.Invoke(icon, new object[] { new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 2, 0, 0, 0) });
        doubleClick.Invoke(icon, new object[] { new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Right, 2, 0, 0, 0) });
        Require(shown == 1 && toggled == 1, "Double click must restore the mascot, not hide it or react to right clicks");
        tray.Dispose(); tray.Dispose();
        Require(tray.Menu.IsDisposed && !icon.Visible, "Tray shutdown leaks menu/icon");

        var native = ApplicationRestart.CreateStartInfo(@"C:\My App\LuKnight.exe", @"C:\My App\LuKnight.dll", new[] { "--example", "value with spaces" });
        Require(native.ArgumentList.SequenceEqual(new[] { "--example", "value with spaces" }), "Restart loses/incorrectly quotes application arguments");
        var hosted = ApplicationRestart.CreateStartInfo(@"C:\dotnet\dotnet.exe", @"C:\My App\LuKnight.dll", Array.Empty<string>());
        Require(hosted.ArgumentList.SequenceEqual(new[] { @"C:\My App\LuKnight.dll" }), "Framework-dependent restart launches dotnet without the application");
        Require(!hosted.UseShellExecute && hosted.CreateNoWindow && hosted.WindowStyle == ProcessWindowStyle.Hidden, "Restart exposes a console window");

        var view = new CharacterView(); Render(view);
        var window = new Window();
        using (var behavior = new BehaviorController(window, view))
        using (var physics = new CharacterPhysicsController(window, view))
        {
            view.SetState(CharacterState.Walk);
            behavior.Resume(BehaviorPauseReason.Hidden);
            Require(view.CurrentState == CharacterState.Walk, "Show on an already visible mascot resets its behavior");
            behavior.Pause(BehaviorPauseReason.Hidden);
            behavior.Pause(BehaviorPauseReason.Chat);
            behavior.Resume(BehaviorPauseReason.Chat);
            Require(Get<BehaviorPauseReason>(behavior, "_pauseReasons") == BehaviorPauseReason.Hidden, "Closing chat resumes a hidden mascot");
            view.SetState(CharacterState.Falling); view.SetMood(CharacterMood.Surprised);
            typeof(BehaviorController).GetMethod("OnTick", Private)!.Invoke(behavior, new object?[] { null, EventArgs.Empty });
            Require(view.CurrentState == CharacterState.Falling && view.CurrentMood == CharacterMood.Surprised, "Hidden behavior still reacts to cursor/moods");
            behavior.Resume(BehaviorPauseReason.Hidden);
            Require(!behavior.IsPaused && view.CurrentState == CharacterState.Falling, "Restore replaces the physical pose/repositions the mascot");
            physics.SetSuspended(true);
            typeof(CharacterPhysicsController).GetField("_isFalling", Private)!.SetValue(physics, true);
            typeof(CharacterPhysicsController).GetField("_velocityY", Private)!.SetValue(physics, 300.0);
            typeof(CharacterPhysicsController).GetMethod("OnRendering", Private)!.Invoke(physics, new object?[] { null, RenderAt(20) });
            Require(Get<double>(physics, "_velocityY") == 300 && Get<TimeSpan?>(physics, "_lastRenderTime") is null, "Hidden physics continues falling");
            physics.SetSuspended(false);
            Require(!Get<bool>(physics, "_suspended"), "Physics cannot resume after showing the mascot");
        }
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); window.Close();
        var host = new LuKnight.MainWindow();
        var hostView = Get<CharacterView>(host, "CharacterControl"); Render(hostView);
        var hostBehavior = new BehaviorController(host, hostView);
        var hostPhysics = new CharacterPhysicsController(host, hostView);
        typeof(LuKnight.MainWindow).GetField("_behaviorController", Private)!.SetValue(host, hostBehavior);
        typeof(LuKnight.MainWindow).GetField("_physicsController", Private)!.SetValue(host, hostPhysics);
        host.HideToTray();
        Require(Get<BehaviorPauseReason>(hostBehavior, "_pauseReasons") == BehaviorPauseReason.Hidden && Get<bool>(hostPhysics, "_suspended"), "HideToTray does not suspend both controllers");
        host.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Require(ReferenceEquals(hostBehavior, Get<BehaviorController>(host, "_behaviorController")) &&
            ReferenceEquals(hostPhysics, Get<CharacterPhysicsController>(host, "_physicsController")), "Reload duplicates controllers/render subscriptions");
        hostView.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); host.Close();
        Console.WriteLine("Tray menu/event/lifecycle checks use synthetic events; shell display and native clicks are not verified.");
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++;
    }
    private static Action<double> Step(SpriteAnimationPlayer player) => AdvanceMethod.CreateDelegate<Action<double>>(player);
    private static void Settle(Action<double> step, double seconds = 1) { for (int i = 0; i < seconds * 120; i++) step(1.0 / 120); }
    private static void CheckRigAndRendering(string output)
    {
        var view = new CharacterView();
        Require(view.RenderMode == CharacterRenderMode.Sprite, "Default must be Sprite");
        var images = new List<(string Name, BitmapSource Image)>();
        foreach (var state in Enum.GetValues<CharacterState>())
        {
            view.SetState(state);
            var player = Get<SpriteAnimationPlayer>(view, "_spritePlayer");
            Settle(Step(player));
            Require(view.RenderMode == CharacterRenderMode.Sprite, "State fell back to vector: " + state);
            var source = Get<Image>(view, "SpriteImage").Source;
            foreach (var mood in Enum.GetValues<CharacterMood>())
            {
                view.SetMood(mood); Settle(Step(player));
                if (state != CharacterState.Idle)
                    Require(ReferenceEquals(source, Get<Image>(view, "SpriteImage").Source) || state == CharacterState.Walk,
                        "Mood replaced a physical pose");
            }
            view.SetMood(CharacterMood.Neutral); Settle(Step(player));
            foreach (int facing in new[] { -1, 1 })
            {
                view.SetFacingDirection(facing);
                var bitmap = Render(view);
                CheckAlphaBounds(Render(Get<Grid>(view, "SpriteImpactRoot")), state + "/" + facing);
                if (facing == 1) images.Add((state.ToString(), bitmap));
            }
            double elapsed = Get<double>(player, "_elapsed");
            view.SetState(state);
            Require(Get<double>(player, "_elapsed") == elapsed, "Repeated state restarted playback");
        }
        SaveContactSheet(images, Path.Combine(output, "preview.png"));
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    private static void CheckLifecycle()
    {
        var view = new CharacterView();
        var player = Get<SpriteAnimationPlayer>(view, "_spritePlayer");
        view.SetRenderMode(CharacterRenderMode.Vector);
        Require(!player.IsPlaying && !Get<bool>(player, "_subscribed"), "Inactive sprite clock must stop");
        view.SetRenderMode(CharacterRenderMode.Sprite);
        Require(player.IsPlaying, "Sprite mode should resume");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Require(!Get<bool>(player, "_subscribed"), "Unload leaks render subscription");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Require(Get<SpriteAnimationPlayer>(view, "_spritePlayer").IsPlaying, "Reload must recreate playback");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    private static void CheckGrabAndTargetReset()
    {
        var view = new CharacterView();
        view.SetRenderMode(CharacterRenderMode.Sprite);
        var window = new Window { Width = 190, Height = 240, Content = view };
        new WindowInteropHelper(window).EnsureHandle(); // Hidden test window; no cursor input is synthesized.
        using (var physics = new CharacterPhysicsController(window, view))
        {
            typeof(CharacterPhysicsController).GetField("_targetWindowHandle", Private)!.SetValue(physics, new IntPtr(123));
            Require(physics.BeginGrab(), "Grab should start with a cursor available");
            Require(Get<nint>(physics, "_targetWindowHandle") == nint.Zero && !physics.IsFalling, "Grab must clear autonomous trajectory");
            var player = Get<SpriteAnimationPlayer>(view, "_spritePlayer");
            Step(player)(.016);
            var before = Get<double>(player, "_elapsed");
            physics.UpdateGrab(); physics.UpdateGrab();
            Require(view.CurrentState == CharacterState.Grabbed && Get<double>(player, "_elapsed") == before, "Pointer samples must not restart the animation");
            physics.EndGrab();
            Require(physics.IsFalling && view.CurrentState == CharacterState.Falling, "Release must hand back to falling physics");
        }
        window.Close();
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    private static RenderingEventArgs RenderAt(double seconds) => (RenderingEventArgs)Activator.CreateInstance(typeof(RenderingEventArgs), Private, null, new object[] { TimeSpan.FromSeconds(seconds) }, null)!;

    private static void CheckWalkingCadence()
    {
        foreach (int hz in new[] { 30, 60, 120, 144 })
        {
            var view = new CharacterView();
        view.SetRenderMode(CharacterRenderMode.Sprite);
            var window = new Window { Width = 190, Height = 240, Content = view };
            new WindowInteropHelper(window).EnsureHandle();
            using (var behavior = new BehaviorController(window, view))
            {
                var workArea = DesktopMonitorService.GetMonitorForWindow(window).WorkArea;
                DesktopMonitorService.SetWindowPosition(window, workArea.Left + 200, workArea.Bottom - 240);
                view.SetState(CharacterState.Walk);
                typeof(BehaviorController).GetField("_walking", Private)!.SetValue(behavior, true);
                typeof(BehaviorController).GetField("_canWalkOnRender", Private)!.SetValue(behavior, true);
                typeof(BehaviorController).GetField("_direction", Private)!.SetValue(behavior, 1);
                var update = typeof(BehaviorController).GetMethod("OnMovementRendering", Private)!;
                update.Invoke(behavior, new object?[] { null, RenderAt(10) });
                var start = DesktopMonitorService.GetWindowBounds(window);
                for (int i = 1; i <= hz; i++)
                    update.Invoke(behavior, new object?[] { null, RenderAt(10 + (double)i / hz) });
                var end = DesktopMonitorService.GetWindowBounds(window);
                double speed = (double)typeof(BehaviorController).GetField("WalkSpeed", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
                Require(Math.Abs(end.Left - start.Left - speed) <= 1.01, $"Walking loses fractional distance at {hz} Hz");
                update.Invoke(behavior, new object?[] { null, RenderAt(11) });
                Require(DesktopMonitorService.GetWindowBounds(window) == end, "Duplicate render callback moves the window twice");
                behavior.Pause(BehaviorPauseReason.UserDrag);
                update.Invoke(behavior, new object?[] { null, RenderAt(11.016) });
                Require(DesktopMonitorService.GetWindowBounds(window) == end, "Autonomous movement must pause during drag");
            }
            window.Close();
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
    }

    private static void CheckCadence()
    {
        var indices = new List<int>();
        var clip = SpriteClipFactory.FromFolder("walk", "Assets/Characters/LuKnight/Walk", 12)!;
        foreach (int hz in new[] { 30, 60, 120, 144 })
        {
            using var player = new SpriteAnimationPlayer(new Image(), new Image());
            player.Play(clip);
            var step = Step(player);
            for (int i = 0; i < hz * 3; i++) step(1.0 / hz);
            indices.Add(Get<int>(player, "_frameIndex"));
        }
        Require(indices.Distinct().Count() == 1, "Frame cadence differs across refresh rates");
        var boundaryImage = new Image();
        using (var boundaryPlayer = new SpriteAnimationPlayer(boundaryImage))
        {
            boundaryPlayer.Play(clip);
            for (int i = 1; i <= 24; i++)
            {
                Step(boundaryPlayer)(1.0 / 12);
                var expected = Get<BitmapSource[]>(boundaryPlayer, "_frames")[i % clip.Frames.Count];
                byte[] actualPixels = new byte[510 * 660 * 4];
                ((BitmapSource)boundaryImage.Source).CopyPixels(actualPixels, 510 * 4, 0);
                Require(actualPixels.SequenceEqual(Get<Dictionary<BitmapSource, byte[]>>(boundaryPlayer, "_pixels")[expected]),
                    "Floating-point phase boundary skipped a drawing");
            }
        }
        using var clockPlayer = new SpriteAnimationPlayer(new Image(), new Image());
        clockPlayer.Play(clip);
        var rendering = typeof(SpriteAnimationPlayer).GetMethod("OnRendering", Private)!;
        rendering.Invoke(clockPlayer, new object?[] { null, RenderAt(1) });
        rendering.Invoke(clockPlayer, new object?[] { null, RenderAt(1.016) });
        double time = Get<double>(clockPlayer, "_elapsed");
        rendering.Invoke(clockPlayer, new object?[] { null, RenderAt(1.016) });
        Require(time == Get<double>(clockPlayer, "_elapsed"), "Duplicate callback advanced clock");
        rendering.Invoke(clockPlayer, new object?[] { null, RenderAt(8) });
        Require(Get<double>(clockPlayer, "_elapsed") - time <= .050001, "Stall must not skip a motion cycle");
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 120; i++) Step(clockPlayer)(1.0 / 60);
        watch.Stop();
        Console.WriteLine($"Walk interpolation CPU: {watch.Elapsed.TotalMilliseconds / 120:0.00} ms/update (offscreen).");
        var front = new Image(); var back = new Image();
        using var blendPlayer = new SpriteAnimationPlayer(front, back);
        blendPlayer.Play(clip); Step(blendPlayer)(.2);
        blendPlayer.Play(SpriteClipFactory.FromFolder("sleep", "Assets/Characters/LuKnight/Sleep", 1)!);
        Require(back.Source is not null && front.Opacity == 0, "Transition lost outgoing pose");
        Step(blendPlayer)(.05);
        Require(Math.Abs(front.Opacity - .5) < .001 && Math.Abs(back.Opacity - .5) < .001, "Transition should blend smoothly");
        Step(blendPlayer)(.05);
        Require(front.Opacity == 1 && back.Source is null, "Transition leaves a ghost image");
        bool failed = false;
        blendPlayer.FrameLoadFailed += () => failed = true;
        blendPlayer.Play(new SpriteAnimationClip("missing", new[] { "missing.png" }, 1, true));
        Require(failed && !blendPlayer.IsPlaying && front.Source is null, "Missing frame must stop safely");
    }

    private static void CheckHeadRegistration()
    {
        var widths = new List<int>(); var centers = new List<double>();
        foreach (string folder in new[] { "Idle", "Expressions" })
        foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Assets/Characters/LuKnight", folder), "*.png"))
        {
            var source = new BitmapImage(new Uri(path));
            var frame = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            byte[] pixels = new byte[510 * 660 * 4]; frame.CopyPixels(pixels, 510 * 4, 0);
            int widest = 0; double center = 0;
            for (int y = 375; y < 465; y++)
            {
                int left = 510, right = -1;
                for (int x = 0; x < 510; x++) if (pixels[(y * 510 + x) * 4 + 3] > 220) { left = Math.Min(left, x); right = x; }
                if (right - left + 1 > widest) { widest = right - left + 1; center = (left + right) / 2.0; }
            }
            widths.Add(widest); centers.Add(center);
        }
        Console.WriteLine($"Head registration: width {widths.Min()}..{widths.Max()} px, center {centers.Min()}..{centers.Max()} px.");
        Require(widths.Max() - widths.Min() <= 8, "Walk/expression head scale jitters");
        Require(centers.Max() - centers.Min() <= 5, "Walk/expression center jitters");
    }

    private static void CheckPuppet(string output)
    {
        var samples = new List<(string Name, BitmapSource Image)>();
        foreach (var motion in Enum.GetValues<SpritePuppetMotion>())
        {
            var rig = new SpritePuppet(motion);
            double period = motion == SpritePuppetMotion.Walk ? .8 : 1.1;
            rig.Advance(period / 4);
            double firstNear = rig.NearLegAngle, firstFar = rig.FarLegAngle;
            rig.Advance(period * .75);
            Require(Math.Abs(firstNear - rig.NearLegAngle) > 45, "Near leg is not stepping");
            Require(Math.Abs(firstFar - rig.FarLegAngle) > 45, "Far leg is not stepping");
            Require(Math.Sign(firstNear - rig.NearLegAngle) != Math.Sign(firstFar - rig.FarLegAngle), "Legs must alternate opposite phases");
            for (int i = 0; i < 24; i++)
            {
                rig.Advance(period * i / 24);
                Require(rig.Image.Width == 510 && rig.Image.Height == 660, "Joint movement changed the sprite canvas");
                foreach (int direction in new[] { -1, 1 })
                {
                    var image = new Image { Source = rig.Image, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(direction, 1) };
                    var rendered = Render(image); CheckAlphaBounds(rendered, $"{motion}/{direction}/{i}");
                    if (i % 6 == 0) samples.Add(($"{motion} {(direction == 1 ? "right" : "left")} {i / 6}", rendered));
                }
            }
            SavePuppetGif(motion, Path.Combine(output, motion.ToString().ToLowerInvariant() + "-directions.gif"));
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) rig.Advance(i / 60.0);
            watch.Stop();
            Console.WriteLine($"{motion} cutout rig: {watch.Elapsed.TotalMilliseconds / 1000:0.000} ms/update.");
        }
        SaveContactSheet(samples, Path.Combine(output, "directional-motion.png"));
        var view = new CharacterView(); view.SetState(CharacterState.Idle);
        view.Blink();
        Require(Get<SpriteAnimationPlayer>(view, "_spritePlayer").Face?.IsBlinking == true, "Blink must animate the face without changing the clip");
        view.SetState(CharacterState.Walk);
        Require(Get<SpritePuppet>(Get<SpriteAnimationPlayer>(view, "_spritePlayer"), "_puppet") is not null, "Runtime walking must use the two-leg rig");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    private static void CheckAttentionAndIdle(string output)
    {
        // Smooth color must remain smooth even when adjacent source pixels have different alpha.
        // Otherwise gaze resampling leaves alternating stationary/moving pixels around the eyes.
        byte[] gradient = new byte[510 * 660 * 4];
        for (int y = 0; y < 660; y++) for (int x = 0; x < 510; x++)
        {
            int p = (y * 510 + x) * 4;
            byte alpha = (byte)((x + y) % 2 == 0 ? 240 : 255);
            for (int c = 0; c < 3; c++) gradient[p + c] = (byte)Math.Round(x * .45 * alpha / 255);
            gradient[p + 3] = alpha;
        }
        var smoothFace = new SpriteFace(BitmapSource.Create(510, 660, 96, 96, PixelFormats.Pbgra32, null, gradient, 510 * 4));
        smoothFace.Look(.85, 0); smoothFace.Advance(1); smoothFace.Image.CopyPixels(gradient, 510 * 4, 0);
        double maxStep = 0;
        for (int x = 176; x < 216; x++)
        {
            int p = (384 * 510 + x) * 4;
            maxStep = Math.Max(maxStep, Math.Abs(gradient[p] * 255.0 / gradient[p + 3] - gradient[p - 4] * 255.0 / gradient[p - 1]));
        }
        Require(maxStep < 2, "Gaze creates speckled/discontinuous color on partially opaque fur");
        // Gaze and lids must never punch transparency into the head or shift its silhouette.
        foreach (bool profile in new[] { false, true })
        {
            string asset = profile ? "Rig/body.png" : "Idle/idle_000.png";
            var source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets/Characters/LuKnight", asset)));
            var closed = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets/Characters/LuKnight/Idle/idle_003.png")));
            var subject = new SpriteFace(source, profile, profile ? null : closed);
            int stride = source.PixelWidth * 4;
            byte[] baseline = new byte[stride * source.PixelHeight], actual = new byte[baseline.Length];
            subject.Image.CopyPixels(baseline, stride, 0);
            foreach (int x in new[] { -1, 0, 1 }) foreach (int y in new[] { -1, 0, 1 })
            {
                subject.Look(x, y); subject.Advance(1); subject.Blink(); subject.Advance(.1);
                subject.Image.CopyPixels(actual, stride, 0);
                Require(Enumerable.Range(0, baseline.Length / 4).All(i => baseline[i * 4 + 3] == actual[i * 4 + 3]),
                    $"Gaze/blink changes head alpha: profile={profile}, gaze={x},{y}");
            }
        }
        var view = new CharacterView(); Render(view);
        var player = Get<SpriteAnimationPlayer>(view, "_spritePlayer");
        var step = Step(player); step(.3);
        var face = player.Face!;
        byte[] original = new byte[510 * 660 * 4]; face.Image.CopyPixels(original, 510 * 4, 0);
        var pictures = new List<(string Name, BitmapSource Image)>();
        foreach (var target in new[] { new Point(-1, -.6), new Point(1, .6), new Point(0, 0) })
        {
            view.TrackCursor(target.X, target.Y); Settle(step, .5);
            Require(Math.Abs(face.LookX - target.X) < .005, "Idle eyes do not follow cursor");
            pictures.Add(($"Idle look {target.X}", Render(view)));
        }
        view.Blink(); step(.1); pictures.Add(("Idle blink", Render(view)));
        Require(ReferenceEquals(face, player.Face), "Blink replaced the idle body/clip");
        foreach (var mood in new[] { CharacterMood.Happy, CharacterMood.Surprised, CharacterMood.Neutral })
        {
            view.SetMood(mood); view.TwitchEars(); step(.06);
            var pixels = new byte[original.Length]; face.Image.CopyPixels(pixels, 510 * 4, 0);
            Require(original.AsSpan(510 * 4 * 500).SequenceEqual(pixels.AsSpan(510 * 4 * 500)), "Idle interaction changes torso/foot pixels (glitch)");
        }
        view.SetState(CharacterState.Walk); player = Get<SpriteAnimationPlayer>(view, "_spritePlayer"); step = Step(player);
        step(.2); double phase = Get<double>(player, "_elapsed");
        view.TrackCursor(1, -.7); view.Blink(); view.TwitchEars(); step(.1);
        Require(Get<double>(player, "_elapsed") > phase && view.CurrentState == CharacterState.Walk, "Interaction stopped/restarted walking clip");
        Require(player.Face!.LookX > .7 && player.Face.IsBlinking, "Walking face ignores cursor/blink");
        pictures.Add(("Walk blink / look", Render(view)));
        view.SetFacingDirection(-1); view.TrackCursor(1, .5); Settle(step, .5);
        Require(player.Face.LookX < -.9, "Mirrored walking gaze follows wrong screen direction");
        pictures.Add(("Walk left / look right", Render(view)));
        using (var behavior = new BehaviorController(new Window(), view))
        {
            typeof(BehaviorController).GetField("_walking", Private)!.SetValue(behavior, true);
            behavior.ObservePointer(new Point(90, 120));
            Require(view.CurrentState == CharacterState.Idle && !Get<bool>(behavior, "_walking"), "Hover cannot interrupt walking");
            Require(view.CurrentMood == CharacterMood.Curious, "Hover does not trigger curious mood");
            behavior.Pause(BehaviorPauseReason.Chat);
            behavior.ObservePointer(new Point(150, 100)); Settle(Step(Get<SpriteAnimationPlayer>(view, "_spritePlayer")), .3);
            Require(Math.Abs(Get<SpriteAnimationPlayer>(view, "_spritePlayer").Face!.LookX) > .2, "Chat pause freezes attention");
            foreach (var state in new[] { CharacterState.Grabbed, CharacterState.Falling, CharacterState.Hanging, CharacterState.Climbing })
            {
                view.SetState(state); behavior.ObservePointer(new Point(90, 100));
                Require(view.CurrentState == state, "Hover interrupts physical state " + state);
            }
            behavior.PointerLeft();
        }
        view.SetState(CharacterState.Walk); Render(view);
        Require(Get<Grid>(view, "SpriteLayer").IsHitTestVisible, "Sprite layer is excluded from pointer input");
        bool routed = false; view.MouseMove += (_, _) => routed = true;
        Get<Image>(view, "SpriteImage").RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
            { RoutedEvent = System.Windows.Input.Mouse.MouseMoveEvent });
        Require(routed, "Sprite mouse events do not reach CharacterView");
        view.SetState(CharacterState.Idle); view.SetFacingDirection(1); view.LookSide(1); view.RelaxCursorLook();
        player = Get<SpriteAnimationPlayer>(view, "_spritePlayer"); Settle(Step(player), .3);
        Require(player.Face!.LookX > .8, "Ambient LookSide was canceled by far cursor relaxation");
        Settle(Step(player), 1.0);
        Require(Math.Abs(player.Face.LookX) < .02, "Ambient glance never relaxes");
        view.LookDown(); view.RelaxCursorLook(); Settle(Step(player), .3);
        Require(player.Face.LookY > .8, "LookDown gaze was canceled before rendering");
        view.PlayEdgePeek(-1); view.RelaxCursorLook(); Settle(Step(player), .3);
        Require(player.Face.LookY > .8 && player.Face.LookX < -.5, "Edge peek does not look down toward the edge");
        Settle(Step(player), 1);
        foreach (var mood in Enum.GetValues<CharacterMood>().Where(m => m is not CharacterMood.Neutral and not CharacterMood.Curious))
        {
            view.SetMood(mood); Settle(Step(player), .3);
            pictures.Add((mood.ToString(), Render(view)));
        }
        var host = new LuKnight.MainWindow();
        var hostView = Get<CharacterView>(host, "CharacterControl"); Render(hostView);
        using (var behavior = new BehaviorController(host, hostView))
        {
            typeof(LuKnight.MainWindow).GetField("_behaviorController", Private)!.SetValue(host, behavior);
            behavior.Pause(BehaviorPauseReason.Chat); behavior.Pause(BehaviorPauseReason.UserDrag);
            typeof(LuKnight.MainWindow).GetField("_leftMouseDown", Private)!.SetValue(host, true);
            hostView.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                { RoutedEvent = System.Windows.Input.Mouse.LostMouseCaptureEvent });
            Require(!Get<bool>(host, "_leftMouseDown"), "Capture loss leaves a stuck mouse button");
            Require(Get<BehaviorPauseReason>(behavior, "_pauseReasons") == BehaviorPauseReason.Chat,
                "Capture loss must release UserDrag while preserving other pause owners");
            behavior.SetThinking(true); behavior.ReactDizzy();
            typeof(BehaviorController).GetField("_temporaryMoodUntil", Private)!.SetValue(behavior, DateTime.UtcNow.AddSeconds(-1));
            typeof(BehaviorController).GetMethod("OnTick", Private)!.Invoke(behavior, new object?[] { null, EventArgs.Empty });
            Require(hostView.CurrentMood == CharacterMood.Thinking, "Chat pause prevents temporary reaction from returning to thinking");
        }
        hostView.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); host.Close();
        SaveContactSheet(pictures, Path.Combine(output, "attention-check.png"));
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    private static void SavePuppetGif(SpritePuppetMotion motion, string path)
    {
        var rig = new SpritePuppet(motion);
        var encoder = new GifBitmapEncoder();
        double period = motion == SpritePuppetMotion.Walk ? .8 : 1.1;
        int count = motion == SpritePuppetMotion.Walk ? 20 : 22;
        for (int i = 0; i < count; i++)
        {
            rig.Advance(i * period / count);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 41, 54)), null, new Rect(0, 0, 420, 270));
                dc.DrawText(new FormattedText(motion + "  LEFT", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.White, 1), new Point(20, 12));
                dc.DrawText(new FormattedText(motion + "  RIGHT", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.White, 1), new Point(230, 12));
                dc.PushTransform(new ScaleTransform(-1, 1, 105, 145));
                dc.DrawImage(rig.Image, new Rect(20, 35, 170, 220)); dc.Pop();
                dc.DrawImage(rig.Image, new Rect(230, 35, 170, 220));
            }
            var bitmap = new RenderTargetBitmap(420, 270, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/grctlext/Delay", (ushort)Math.Round(period / count * 100));
            metadata.SetQuery("/grctlext/Disposal", (byte)2);
            encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        }
        using var stream = new MemoryStream(); encoder.Save(stream);
        byte[] gif = stream.ToArray();
        int header = 13 + ((gif[10] & 0x80) != 0 ? 3 * (1 << ((gif[10] & 7) + 1)) : 0);
        byte[] loop = { 0x21, 0xff, 0x0b, 78, 69, 84, 83, 67, 65, 80, 69, 50, 46, 48, 3, 1, 0, 0, 0 };
        using var output = File.Create(path); output.Write(gif, 0, header); output.Write(loop); output.Write(gif, header, gif.Length - header);
    }

    private static BitmapSource Render(FrameworkElement view)
    {
        view.Measure(new Size(170, 220)); view.Arrange(new Rect(0, 0, 170, 220)); view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(510, 660, 288, 288, PixelFormats.Pbgra32);
        bitmap.Render(view); bitmap.Freeze();
        return bitmap;
    }
    private static void CheckAlphaBounds(BitmapSource image, string label)
    {
        byte[] pixels = new byte[510 * 660 * 4]; image.CopyPixels(pixels, 510 * 4, 0);
        int visible = 0;
        for (int y = 0; y < 660; y++)
        for (int x = 0; x < 510; x++)
        {
            byte alpha = pixels[(y * 510 + x) * 4 + 3];
            if (alpha > 0) visible++;
            if ((x == 0 || y == 0 || x == 509 || y == 659) && alpha > 0)
                throw new InvalidOperationException($"Clipped sprite at canvas border: {label} ({x},{y})");
        }
        Require(visible > 30000, "Sprite render is empty: " + label);
    }
    private static void Save(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); png.Save(stream);
    }
    private static void SaveContactSheet(List<(string Name, BitmapSource Image)> images, string path)
    {
        int height = (int)Math.Ceiling(images.Count / 4.0) * 290;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 41, 54)), null, new Rect(0, 0, 1020, height));
            for (int i = 0; i < images.Count; i++)
            {
                double x = i % 4 * 255, y = i / 4 * 290;
                dc.DrawText(new FormattedText(images[i].Name, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 15, Brushes.White, 1), new Point(x + 20, y + 8));
                dc.DrawImage(images[i].Image, new Rect(x + 25, y + 25, 204, 264));
            }
        }
        var bitmap = new RenderTargetBitmap(1020, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); Save(bitmap, path);
    }

}
