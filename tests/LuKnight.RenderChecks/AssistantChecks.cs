using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;
using LuKnight.Views;

internal static partial class Program
{
    private static void CheckAssistant()
    {
        Task.Run(CheckAssistantAsync).GetAwaiter().GetResult();

        var settings = new SettingsService();
        settings.Update(settings.Current with { Chat = new() { Provider = ChatProvider.Local } });
        var services = new AppServices(settings, new FakeCredentials());
        var host = new LuKnight.MainWindow(services);
        var panel = Get<ChatPanel>(host, "ChatPanelControl");
        try
        {
            typeof(LuKnight.MainWindow).GetMethod("ChatPanel_MessageSubmitted", Private)!.Invoke(host, new object[] { "halo" });
            Require(services.Assistant.Conversation.Count == 2 && !services.Assistant.IsBusy,
                "MainWindow chat bypasses Assistant or remains busy");
            Require(panel.CurrentStatus == ChatStatus.Local && Get<StackPanel>(panel, "MessagesPanel").Children.Count == 3,
                "Assistant reply is missing from chat bubbles or has wrong backend status");
            var window = new SettingsWindow(host);
            window.Model.Product!.ClearCommand.Execute(null);
            Require(services.Assistant.Conversation.Count == 0 && Get<StackPanel>(panel, "MessagesPanel").Children.Count == 0,
                "Settings Clear Conversation does not clear Assistant transcript and UI bubbles");
            window.Close();
        }
        finally
        {
            Get<CharacterView>(host, "CharacterControl").RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            host.Close();
        }
    }

    private static async Task CheckAssistantAsync()
    {
        var conversation = new ConversationManager();
        Require(conversation.Count == 0, "Assistant transcript is not initially empty");
        conversation.AddUser(new("  hello  ", AssistantInputSource.Tray)); conversation.AddAssistant("  reply  ");
        Require(conversation.Turns[0] is { Role: ConversationRole.User, Text: "hello", Source: AssistantInputSource.Tray } &&
            conversation.Turns[1] is { Role: ConversationRole.Assistant, Text: "reply" }, "Conversation loses role, normalization, or source");
        Require(conversation.Turns.All(t => t.CreatedAt.Offset == TimeSpan.Zero && t.CreatedAt <= DateTimeOffset.UtcNow), "Transcript timestamps are not UTC");
        var snapshot = conversation.Snapshot();
        conversation.Clear();
        Require(snapshot.Count == 2 && conversation.Count == 0, "Snapshot changes after transcript is cleared");
        try { ((IList<ConversationTurn>)conversation.Turns).Add(snapshot[0]); throw new Exception("Transcript publicly mutable"); }
        catch (NotSupportedException) { Require(conversation.Count == 0, "Read-only transcript was modified"); }
        foreach (string? invalid in new[] { "", "   ", null })
        {
            try { conversation.AddUser(new(invalid!)); throw new Exception("Empty user input accepted"); }
            catch (ArgumentException) { Require(conversation.Count == 0, "Invalid input polluted transcript"); }
            try { conversation.AddAssistant(invalid!); throw new Exception("Empty reply accepted"); }
            catch (ArgumentException) { Require(conversation.Count == 0, "Invalid reply polluted transcript"); }
        }
        for (int i = 0; i < 55; i++) { conversation.AddUser(new("user " + i)); conversation.AddAssistant("reply " + i); }
        Require(conversation.Count == 100 && conversation.Turns[0].Text == "user 5" && conversation.Turns[^1].Text == "reply 54",
            "Transcript does not retain the latest 100 turns");

        var noKey = new ChatCoordinator(new FakeCredentials(), new(), () => null);
        var local = new AssistantController(noKey);
        var localReply = await local.SendAsync(new("halo", AssistantInputSource.System));
        Require(localReply.Backend == AssistantBackend.Local && localReply.Text.Length > 0 && local.Conversation.Count == 2,
            "Assistant local reply or transcript is incorrect");
        Require(local.Conversation.Turns[0].Source == AssistantInputSource.System && local.DisplayName == noKey.DisplayName,
            "Assistant loses input source or coordinator display name");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await local.SendAsync(new("cancelled"), cancelled.Token); throw new Exception("Cancelled input accepted"); }
        catch (OperationCanceledException) { Require(local.Conversation.Count == 2, "Pre-cancelled input polluted transcript"); }
        try { await local.SendAsync(new("  ")); throw new Exception("Empty input accepted"); }
        catch (ArgumentException) { Require(local.Conversation.Count == 2, "Empty input polluted Assistant transcript"); }
        try { await local.SendAsync(null!); throw new Exception("Null request accepted"); }
        catch (ArgumentNullException) { Require(local.Conversation.Count == 2, "Null request changed transcript"); }

        var payloads = new List<string>();
        using var handler = new FakeHttp(async (request, token) =>
        {
            payloads.Add(await request.Content!.ReadAsStringAsync(token));
            return JsonResponse(new { candidates = new[] { new { content = new { parts = new[] { new { text = "Gemini test reply" } } } } } });
        });
        using var client = new HttpClient(handler);
        var coordinator = new ChatCoordinator(new FakeCredentials { Key = "fake-assistant-test-key" }, new(), () => null, client);
        var assistant = new AssistantController(coordinator);
        var onlineReply = await assistant.SendAsync(new("online", AssistantInputSource.Voice));
        Require(onlineReply.Backend == AssistantBackend.Gemini && onlineReply.Text == "Gemini test reply" && assistant.Conversation.Count == 2,
            "Assistant mislabels a successful Gemini reply");
        Require(onlineReply.CreatedAt >= assistant.Conversation.Turns[^1].CreatedAt, "Reply timestamp predates its transcript entry");
        await assistant.SendAsync(new("second"));
        using (var history = JsonDocument.Parse(payloads[^1]))
            Require(history.RootElement.GetProperty("contents").GetArrayLength() == 3, "Assistant bypasses existing Gemini history");
        assistant.ClearConversation();
        Require(assistant.Conversation.Count == 0, "Assistant clear leaves transcript entries");
        await assistant.SendAsync(new("fresh"));
        using (var history = JsonDocument.Parse(payloads[^1]))
            Require(history.RootElement.GetProperty("contents").GetArrayLength() == 1, "Assistant clear leaves Gemini context entries");
        coordinator.Configure(coordinator.Options with { RememberConversation = false });
        await assistant.SendAsync(new("without context"));
        Require(assistant.Conversation.Count == 4, "Remember OFF incorrectly erases Assistant transcript before phase 7C");
        using (var history = JsonDocument.Parse(payloads[^1]))
            Require(history.RootElement.GetProperty("contents").GetArrayLength() == 1, "Assistant changes RememberConversation behavior");
        coordinator.Configure(coordinator.Options with { Provider = ChatProvider.Local });
        Require((await assistant.SendAsync(new("local now"))).Backend == AssistantBackend.Local, "Assistant fails to follow provider changes on shared ChatCoordinator");

        using var failureClient = new HttpClient(new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var fallback = new AssistantController(new ChatCoordinator(new FakeCredentials { Key = "fake-key" }, new(), () => null, failureClient));
        Require((await fallback.SendAsync(new("offline"))).Backend == AssistantBackend.Local && fallback.Conversation.Count == 2 && !fallback.IsBusy,
            "Provider error breaks fallback transcript or leaves Assistant busy");

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pendingClient = new HttpClient(new FakeHttp(async (_, token) =>
        {
            started.TrySetResult(); await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var pendingChat = new ChatCoordinator(new FakeCredentials { Key = "fake-key" }, new(), () => null, pendingClient);
        var pending = new AssistantController(pendingChat);
        using var cancellation = new CancellationTokenSource();
        var send = pending.SendAsync(new("pending"), cancellation.Token); await started.Task;
        Require(pending.IsBusy && pending.Conversation.Count == 1, "Assistant does not expose pending chat state");
        try { await pending.SendAsync(new("duplicate")); throw new Exception("Busy send accepted"); }
        catch (InvalidOperationException) { Require(pending.Conversation.Count == 1, "Rejected busy send polluted transcript"); }
        try { pending.ClearConversation(); throw new Exception("Busy clear accepted"); }
        catch (InvalidOperationException) { Require(pending.Conversation.Count == 1, "Failed clear erased accepted user input"); }
        cancellation.Cancel();
        try { await send; throw new Exception("Cancellation swallowed"); }
        catch (OperationCanceledException) { Require(!pending.IsBusy && pending.Conversation.Count == 1, "Cancellation creates a phantom reply or leaves Assistant busy"); }
        pendingChat.Configure(new() { Provider = ChatProvider.Local });
        Require((await pending.SendAsync(new("retry"))).Backend == AssistantBackend.Local, "Assistant cannot send after cancellation");
        pending.ClearConversation(); Require(pending.Conversation.Count == 0, "Clear after cancellation failed");
        Console.WriteLine("Assistant checks use fake Gemini responses; transcript remains session-only and no live API calls are made.");
    }
}
