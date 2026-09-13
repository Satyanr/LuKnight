using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private const string UiFixturePrefix = "LuKnight UIA Fixture";

    private static void RunUiActionFixtureHost(string token)
    {
        string initialTitle = $"{UiFixturePrefix} {token}";
        var window = new Window
        {
            Title = initialTitle,
            Width = 420,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
            Topmost = true
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        var description = new TextBlock
        {
            Text = "LuKnight UI Automation acceptance fixture",
            Margin = new Thickness(0, 0, 0, 16)
        };
        var refresh = new Button
        {
            Content = "Refresh",
            Width = 140,
            Height = 40,
            Margin = new Thickness(0, 0, 0, 12)
        };
        AutomationProperties.SetName(refresh, "Refresh");
        var save = new Button { Content = "Save", Width = 140, Height = 40 };
        AutomationProperties.SetName(save, "Save");
        var mouseTarget = new Button
        {
            Content = "Mouse Target",
            Width = 140,
            Height = 40,
            Margin = new Thickness(0, 12, 0, 0)
        };
        AutomationProperties.SetName(mouseTarget, "Mouse Target");

        refresh.Click += (_, _) => window.Title = $"{initialTitle} — REFRESH INVOKED";
        save.Click += (_, _) => window.Title = $"{initialTitle} — SAVE INVOKED";
        mouseTarget.Click += (_, _) => window.Title = $"{initialTitle} — MOUSE CLICKED";
        panel.Children.Add(description);
        panel.Children.Add(refresh);
        panel.Children.Add(save);
        panel.Children.Add(mouseTarget);
        window.Content = panel;

        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        application.Run(window);
    }

    private static async Task CheckUiButtonInvokeLiveAsync()
    {
        string token = Guid.NewGuid().ToString("N")[..8];
        string initialTitle = $"{UiFixturePrefix} {token}";
        string invokedTitle = $"{initialTitle} — REFRESH INVOKED";
        using Process fixture = StartUiFixtureProcess(token);

        try
        {
            var windows = new DesktopWindowTargetService();
            DesktopWindowTarget target = await WaitForFixtureWindowAsync(
                windows,
                fixture,
                initialTitle);
            Console.WriteLine();
            Console.WriteLine($"Fixture PID: {fixture.Id}");
            Console.WriteLine($"Fixture window: {target.DisplayLabel}");

            var ui = new WindowsDesktopUiAutomationReader();
            DesktopUiSnapshot snapshot = await ui.CaptureAsync(target);
            Require(snapshot.Success, snapshot.Error ?? "Native UIA fixture capture failed.");

            DesktopUiControlResolution refresh =
                DesktopUiControlResolver.Resolve(snapshot, "Refresh", "Button");
            Require(refresh.Match is not null, "Refresh button was not visible through native UIA.");
            DesktopUiControlResolution save =
                DesktopUiControlResolver.Resolve(snapshot, "Save", "Button");
            Require(save.Match is not null, "Save button was not visible through native UIA.");
            Require(
                !DesktopUiActionPolicy.IsTemporarilyBlocked(refresh.Match!, out _),
                "Safe Refresh fixture button was blocked.");
            Require(
                DesktopUiActionPolicy.IsTemporarilyBlocked(save.Match!, out _),
                "Sensitive Save fixture button escaped policy.");

            using var handler = new FakeHttp((_, _) =>
                throw new InvalidOperationException("Live UIA action attempted Gemini."));
            using var client = new HttpClient(handler);
            var chat = new ChatCoordinator(
                new FakeCredentials { Key = "unused-uia-live-key" },
                new ChatSettings
                {
                    Provider = ChatProvider.Gemini,
                    UseDesktopActions = true
                },
                () => null,
                client);
            var desktopRouter = new LocalDesktopCommandRouter(
                new DesktopAppCatalogService(() => Array.Empty<DesktopAppTarget>()),
                windows);
            var router = new AssistantIntentRouter(desktopRouter);
            var executor = new WindowsDesktopUiActionExecutor();
            var action = new InvokeDesktopUiControlAction(() => true, windows, ui, executor);
            var actions = new AssistantActionRouter(new IAssistantAction[] { action });
            var assistant = new AssistantController(chat, intentRouter: router, actions: actions);

            AssistantReply proposal = await assistant.SendAsync(
                new AssistantRequest($"klik tombol Refresh di window {initialTitle}"));
            Require(proposal.Backend == AssistantBackend.Local, "Live UIA preparation was not local.");
            Require(proposal.ActionProposal is not null, "Safe Refresh button did not produce confirmation.");
            Require(handler.Calls == 0, "Live UIA preparation called Gemini.");
            Require(
                assistant.Conversation.GetRecentContext().Count == 0,
                "Live UIA preparation leaked into Gemini context.");
            Require(
                windows.Capture().Any(x => x.ProcessId == fixture.Id && x.Title == initialTitle),
                "Refresh executed before confirmation.");
            Console.WriteLine("Confirmation gate passed; Refresh has not executed yet.");

            AssistantReply confirmed = await assistant.ConfirmActionAsync(proposal.ActionProposal!.Id);
            Require(confirmed.Backend == AssistantBackend.Local, "Confirmed live UIA action was not local.");
            Require(handler.Calls == 0, "Confirmed live UIA action called Gemini.");
            await WaitForFixtureWindowAsync(windows, fixture, invokedTitle);
            Console.WriteLine("Native InvokePattern successfully activated Refresh.");
            Require(
                assistant.Conversation.GetRecentContext().Count == 0,
                "Live UIA result leaked into Gemini context.");

            AssistantReply blocked = await assistant.SendAsync(
                new AssistantRequest($"klik tombol Save di window {invokedTitle}"));
            Require(
                blocked.Backend == AssistantBackend.Local && blocked.ActionProposal is null,
                "Sensitive Save button escaped policy.");
            Require(handler.Calls == 0, "Blocked Save action called Gemini.");
            await Task.Delay(300);
            Require(
                windows.Capture().Any(x => x.ProcessId == fixture.Id && x.Title == invokedTitle),
                "Blocked Save button was invoked.");
            Console.WriteLine("Sensitive Save button was correctly blocked.");
            Console.WriteLine();
            Console.WriteLine("PASS: native cross-process UIA Button Invoke acceptance.");
        }
        finally
        {
            await StopUiFixtureAsync(fixture);
        }
    }

    private static Process StartUiFixtureProcess(string token)
    {
        string processPath = Environment.ProcessPath ??
            throw new InvalidOperationException("Current process path unavailable.");
        var info = new ProcessStartInfo { FileName = processPath, UseShellExecute = false };
        if (string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            string assemblyPath = Assembly.GetEntryAssembly()?.Location ??
                throw new InvalidOperationException("Entry assembly path unavailable.");
            info.ArgumentList.Add(assemblyPath);
        }

        info.ArgumentList.Add("--uia-action-fixture-host");
        info.ArgumentList.Add($"--uia-fixture-token={token}");
        return Process.Start(info) ??
            throw new InvalidOperationException("Failed to start UIA fixture process.");
    }

    private static async Task CheckSafeMouseLiveAsync()
    {
        string token = Guid.NewGuid().ToString("N")[..8];
        string initialTitle = $"{UiFixturePrefix} {token}";
        string clickedTitle = $"{initialTitle} — MOUSE CLICKED";
        using Process fixture = StartUiFixtureProcess(token);

        try
        {
            var windows = new DesktopWindowTargetService();
            DesktopWindowTarget target = await WaitForFixtureWindowAsync(
                windows,
                fixture,
                initialTitle);
            var ui = new WindowsDesktopUiAutomationReader();
            DesktopUiSnapshot snapshot = await ui.CaptureAsync(target);
            Require(snapshot.Success, snapshot.Error ?? "Mouse fixture UIA capture failed.");

            DesktopUiControlResolution control =
                DesktopUiControlResolver.Resolve(snapshot, "Mouse Target", "Button");
            Require(control.Match is not null, "Mouse Target button was not resolved.");
            DesktopUiNodeSnapshot button = control.Match!;
            Require(
                !DesktopUiActionPolicy.IsTemporarilyBlocked(button, out _),
                "Safe mouse fixture button was blocked.");

            var mouse = new WindowsDesktopMouseActionExecutor();
            DesktopActionResult result = await mouse.ClickAsync(
                target,
                button.Path,
                DesktopUiNodeIdentity.Fingerprint(button));
            Require(result.Success, result.Message);
            DesktopWindowTarget clickedWindow = await WaitForFixtureWindowAsync(
                windows,
                fixture,
                clickedTitle);

            DesktopUiSnapshot afterClickSnapshot = await ui.CaptureAsync(clickedWindow);
            Require(
                afterClickSnapshot.Success,
                afterClickSnapshot.Error ?? "Mouse fixture recapture failed.");
            DesktopUiControlResolution save = DesktopUiControlResolver.Resolve(
                afterClickSnapshot,
                "Save",
                "Button");
            Require(save.Match is not null, "Save button disappeared from mouse fixture.");
            DesktopActionResult blockedSave = await mouse.ClickAsync(
                clickedWindow,
                save.Match!.Path,
                DesktopUiNodeIdentity.Fingerprint(save.Match));
            Require(!blockedSave.Success, "Safe mouse bypassed sensitive-button policy.");
            await Task.Delay(250);
            Require(
                windows.Capture().Any(x =>
                    x.ProcessId == fixture.Id && x.Title == clickedTitle),
                "Blocked Save button was clicked by mouse fallback.");

            Console.WriteLine();
            Console.WriteLine("Native bounded mouse click activated Mouse Target.");
            Console.WriteLine("Cursor restoration was attempted only if the user had not moved it.");
            Console.WriteLine("PASS: safe mouse primitive live acceptance.");
        }
        finally
        {
            await StopUiFixtureAsync(fixture);
        }
    }

    private static async Task<DesktopWindowTarget> WaitForFixtureWindowAsync(
        DesktopWindowTargetService windows,
        Process fixture,
        string expectedTitle)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            fixture.Refresh();
            if (fixture.HasExited)
            {
                throw new InvalidOperationException(
                    $"UIA fixture exited unexpectedly with code {fixture.ExitCode}.");
            }

            DesktopWindowTarget? target = windows.Capture().FirstOrDefault(x =>
                x.ProcessId == fixture.Id &&
                string.Equals(x.Title, expectedTitle, StringComparison.Ordinal));
            if (target is not null)
                return target;
            await Task.Delay(100);
        }

        throw new InvalidOperationException($"UIA fixture window \"{expectedTitle}\" was not found.");
    }

    private static async Task StopUiFixtureAsync(Process fixture)
    {
        fixture.Refresh();
        if (fixture.HasExited)
            return;
        fixture.CloseMainWindow();
        try
        {
            await fixture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            if (!fixture.HasExited)
            {
                fixture.Kill(entireProcessTree: true);
                await fixture.WaitForExitAsync();
            }
        }
    }
}
