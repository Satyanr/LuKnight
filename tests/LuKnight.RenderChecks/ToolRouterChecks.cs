using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private static async Task CheckToolRouterAsync()
    {
        var intentRouter = new AssistantIntentRouter();
        AssistantIntent normal = intentRouter.Route("halo Lu-Knight");
        Require(normal.Kind == AssistantIntentKind.Conversation,
            "Normal conversation was routed as a tool.");

        AssistantIntent remember = intentRouter.Route("ingat bahwa kode saya ORBIT-742");
        Require(remember.Kind == AssistantIntentKind.Tool &&
            remember.Tool?.Name == BuiltInToolNames.MemoryRemember &&
            remember.Tool.Arguments["text"] == "kode saya ORBIT-742",
            "Remember intent routing is incorrect.");

        AssistantIntent forget = intentRouter.Route("lupakan: kode saya ORBIT-742");
        Require(forget.Kind == AssistantIntentKind.Tool &&
            forget.Tool?.Name == BuiltInToolNames.MemoryForget,
            "Forget intent routing is incorrect.");

        AssistantIntent desktopRequest = intentRouter.Route("tolong buka notepad");
        Require(desktopRequest.Kind == AssistantIntentKind.Conversation,
            "Unregistered desktop request was routed as a tool.");

        var memory = new MemoryService();
        var tools = new AssistantToolRouter(new IAssistantTool[]
        {
            new RememberMemoryTool(memory),
            new ForgetMemoryTool(memory)
        });
        Require(tools.RegisteredTools.Count == 2 &&
            tools.RegisteredTools.Contains(BuiltInToolNames.MemoryRemember) &&
            tools.RegisteredTools.Contains(BuiltInToolNames.MemoryForget),
            "Default tool registry is incomplete.");

        ToolExecutionResult stored = await tools.ExecuteAsync(remember.Tool!);
        Require(stored.Success && memory.Count == 1,
            "Remember tool failed.");

        ToolExecutionResult removed = await tools.ExecuteAsync(forget.Tool!);
        Require(removed.Success && memory.Count == 0,
            "Forget tool failed.");

        var unknown = new ToolInvocation(
            "desktop.run-command",
            new Dictionary<string, string>());
        ToolExecutionResult blocked = await tools.ExecuteAsync(unknown);
        Require(!blocked.Success && memory.Count == 0,
            "Unregistered tool was executed.");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await tools.ExecuteAsync(remember.Tool!, cancellation.Token);
            throw new Exception("Cancelled tool execution was accepted.");
        }
        catch (OperationCanceledException)
        {
            Require(memory.Count == 0, "Cancelled tool execution changed memory.");
        }

        var gateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateRouter = new AssistantToolRouter(new IAssistantTool[]
        {
            new SlowRememberTool(gateStarted, gateRelease)
        });
        var gateAssistant = new AssistantController(
            new ChatCoordinator(new FakeCredentials(), new() { Provider = ChatProvider.Local }),
            tools: gateRouter);
        Task<AssistantReply> first = gateAssistant.SendAsync(new("ingat bahwa gate test"));
        await gateStarted.Task;
        try
        {
            await gateAssistant.SendAsync(new("ingat bahwa second request"));
            throw new Exception("Concurrent tool request was accepted.");
        }
        catch (InvalidOperationException) { }
        try
        {
            gateAssistant.ClearConversation();
            throw new Exception("Clear Conversation was accepted during a tool request.");
        }
        catch (InvalidOperationException) { }
        gateRelease.SetResult();
        await first;

        Console.WriteLine("Tool router checks passed.");
    }

    private sealed class SlowRememberTool(
        TaskCompletionSource started,
        TaskCompletionSource release) : IAssistantTool
    {
        public string Name => BuiltInToolNames.MemoryRemember;

        public async Task<ToolExecutionResult> ExecuteAsync(
            ToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new ToolExecutionResult(true, "gate complete");
        }
    }
}
