using System.IO;
using System.Net.Http;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private static async Task CheckPhase8RegressionsAsync()
    {
        foreach (var (input, expected) in new (string, string)[]
        {
            ("**Halo** dan *selamat pagi*", "Halo dan selamat pagi"),
            (@"\*\*Halo\*\* dan \'contoh\'", "Halo dan 'contoh'"),
            ("### Langkah\n* Buka Settings", "Langkah\n• Buka Settings"),
            ("Gunakan `a * b` dan `C:\\Temp\\*.txt`.", "Gunakan a * b dan C:\\Temp\\*.txt."),
            ("```cs\r\nvar s = 'x'; // **literal**\r\n```", "var s = 'x'; // **literal**"),
            (@"C:\Temp\*.txt dan D:\Data\*.csv", @"C:\Temp\*.txt dan D:\Data\*.csv"),
            ("I'm here. 'Kutipan' tetap ada. 2 * 3 = 6.", "I'm here. 'Kutipan' tetap ada. 2 * 3 = 6."),
            ("nama_file dan x_y_z", "nama_file dan x_y_z")
        })
            Require(AssistantTextFormatter.Format(input) == expected, "Plain-text formatting corrupted: " + input);

        using var handler = new FakeHttp((_, _) => Task.FromResult(JsonResponse(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = @"\*\*Halo\*\* `contoh`" } } } } }
        })));
        using var client = new HttpClient(handler);
        var assistant = new AssistantController(new ChatCoordinator(new FakeCredentials { Key = "test" }, new(), () => null, client));
        var reply = await assistant.SendAsync(new("halo"));
        Require(reply.Text == "Halo contoh" && assistant.Conversation.Turns.Last().Text == reply.Text,
            "Model formatting was not cleaned before transcript storage.");

        var router = new AssistantIntentRouter();
        foreach (string query in new[] { "apa isi clipboard?", "baca clipboard!", "read clipboard." })
            Require(router.Route(query).Context?.Name == BuiltInContextNames.ClipboardText, "Clipboard punctuation broke routing.");

        var invalidSystem = new SystemStatusContextSource(() => true, () => throw new Exception("Invalid scope captured system data"));
        Require(!(await invalidSystem.CaptureAsync(new(BuiltInContextNames.SystemStatus,
            new Dictionary<string, string> { ["scope"] = "999" }))).Success, "Undefined numeric system scope was accepted.");

        bool enabled = true;
        var executor = new FakeDesktopActionExecutor();
        var actions = new IAssistantAction[]
        {
            new OpenDesktopApplicationAction(() => enabled, executor),
            new FocusDesktopApplicationAction(() => enabled, executor)
        };
        foreach (var action in actions)
        {
            enabled = true;
            var prepared = action.Prepare(new(action.Name, new Dictionary<string, string> { ["appId"] = "notepad" }));
            enabled = false;
            Require(!(await action.ExecuteAsync(prepared.Action!)).Success, "Action ignored a revoked desktop setting.");
        }
        Require(executor.OpenCalls == 0 && executor.FocusCalls == 0, "Disabled action reached the native executor.");

        enabled = true;
        var actionAssistant = new AssistantController(
            new ChatCoordinator(new FakeCredentials(), new() { Provider = ChatProvider.Local }),
            actions: new AssistantActionRouter(actions));
        var proposal = await actionAssistant.SendAsync(new("buka notepad"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await actionAssistant.ConfirmActionAsync(proposal.ActionProposal!.Id, cancelled.Token);
            throw new Exception("Cancelled confirmation executed.");
        }
        catch (OperationCanceledException) { }
        Require(!actionAssistant.IsBusy && !actionAssistant.HasPendingAction && executor.OpenCalls == 0,
            "Cancelled confirmation left chat permanently blocked.");
        Require((await actionAssistant.SendAsync(new("halo"))).Backend == AssistantBackend.Local,
            "Chat did not recover after cancelled confirmation.");

        string directory = Path.Combine(Path.GetTempPath(), "LuKnight-Phase8-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = new LocalTextFileContextSource(() => true);
            async Task<ContextCaptureResult> Capture(string name) => await source.CaptureAsync(new(
                BuiltInContextNames.LocalTextFile, new Dictionary<string, string> { ["path"] = Path.Combine(directory, name) }));
            await File.WriteAllTextAsync(Path.Combine(directory, "large.txt"), new string('a', 30_000));
            var truncated = await Capture("large.txt");
            Require(truncated.Success && truncated.Reference is { Truncated: true, Content.Length: 24_000 },
                "Large text was not bounded and marked truncated.");
            await File.WriteAllTextAsync(Path.Combine(directory, "exact.txt"), new string('a', 24_000));
            Require((await Capture("exact.txt")).Reference is { Truncated: false, Content.Length: 24_000 },
                "Exact-sized text was incorrectly truncated.");
            await File.WriteAllBytesAsync(Path.Combine(directory, "binary.txt"), [0xff, 0xff, 0xff]);
            Require(!(await Capture("binary.txt")).Success, "Invalid UTF-8 became corrupted context.");
            await File.WriteAllTextAsync(Path.Combine(directory, "utf16.txt"), "Dokumen UTF16", System.Text.Encoding.Unicode);
            Require((await Capture("utf16.txt")).Reference?.Content == "Dokumen UTF16", "BOM text decoding regressed.");
            await File.WriteAllBytesAsync(Path.Combine(directory, "oversize.txt"), new byte[512 * 1024 + 1]);
            Require(!(await Capture("oversize.txt")).Success, "File size limit was bypassed.");
            await File.WriteAllTextAsync(Path.Combine(directory, "credentials.json"), "{}");
            Require(!(await Capture("credentials.json")).Success, "Sensitive file was accepted.");

            string memoryPath = Path.Combine(directory, "memory.json");
            await File.WriteAllTextAsync(memoryPath, "{\"schemaVersion\":1,\"items\":[null]}");
            var memory = new MemoryService(memoryPath);
            memory.Load();
            Require(memory.Count == 0 && memory.Status.Contains("tidak valid"), "Null memory entry crashed startup.");
            const string futureMemory = "{\"schemaVersion\":999,\"items\":[]}";
            await File.WriteAllTextAsync(memoryPath, futureMemory);
            var future = new MemoryService(memoryPath);
            future.Load();
            future.Remember("temporary note");
            Require(await File.ReadAllTextAsync(memoryPath) == futureMemory,
                "Remember overwrote memory belonging to a newer app version.");
            future.Clear();
            Require(await File.ReadAllTextAsync(memoryPath) == futureMemory,
                "Clear overwrote an unsupported memory schema.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        var screenStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var screenRelease = new ManualResetEventSlim();
        var screenSource = new ScreenImageContextSource(() => true, () => true, () =>
        {
            screenStarted.SetResult();
            if (!screenRelease.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return new ScreenCaptureSnapshot("image/jpeg", "AAAA", 1, 1);
        });
        using var screenCancel = new CancellationTokenSource();
        Task<ContextCaptureResult> capture = screenSource.CaptureAsync(new(BuiltInContextNames.ScreenImage, new Dictionary<string, string>()), screenCancel.Token);
        try
        {
            await screenStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(!capture.IsCompleted, "Screen capture blocked the caller instead of running asynchronously.");
            screenCancel.Cancel();
        }
        finally { screenRelease.Set(); }
        try { await capture; throw new Exception("Cancelled screenshot was returned for upload."); }
        catch (OperationCanceledException) { }
    }
}
