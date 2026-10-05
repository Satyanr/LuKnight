using System.Net.Http;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private sealed class
        PrivacyContextSource :
            IAssistantContextSource
    {
        public const string
            Secret =
                "PRIVATE_CLIPBOARD_SENTINEL_8921";


        public string Name { get; init; } = BuiltInContextNames.ClipboardText;


        public bool Fail
        {
            get;
            set;
        }


        public Task<ContextCaptureResult>
            CaptureAsync(
                ContextInvocation invocation,
                CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();


            if (Fail)
            {
                return Task.FromResult(
                    new ContextCaptureResult(
                        false,
                        "Clipboard test gagal."));
            }


            return Task.FromResult(
                new ContextCaptureResult(
                    true,
                    "Clipboard test dimuat.",
                    new ChatReferenceBlock(
                        Kind:
                            "clipboard-text",

                        Name:
                            "Clipboard",

                        Content:
                            Secret,

                        Truncated:
                            false)));
        }
    }
    private static async Task CheckPrivacyIsolationAsync()
    {
        var bodies = new List<string>();
        bool privateReply = false;
        const string derivedReply = "DERIVED_PRIVATE_REPLY_441";
        using var handler = new FakeHttp(async (request, token) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(token));
            string text = privateReply ? derivedReply : "NORMAL_REPLY_CONTROL_77";
            privateReply = false;
            return JsonResponse(new { candidates = new[] { new { content = new { parts = new[] { new { text } } } } } });
        });
        using var client = new HttpClient(handler);
        var chat = new ChatCoordinator(new FakeCredentials { Key = "privacy-test-key" },
            new ChatSettings { Provider = ChatProvider.Gemini, RememberConversation = true, UseDesktopActions = true },
            () => null, client);

        // All explicit source types share the same one-shot controller boundary.
        foreach (var (name, command) in new[]
        {
            (BuiltInContextNames.ClipboardText, "ringkas clipboard"),
            (BuiltInContextNames.LocalTextFile, @"ringkas file: C:\Clients\PRIVATE_PATH_SENTINEL_315\merger-plan.txt"),
            (BuiltInContextNames.ScreenImage, "lihat layar saya"),
            (BuiltInContextNames.SystemStatus, "status sistem")
        })
        {
            var source = new PrivacyContextSource { Name = name };
            var assistant = new AssistantController(chat, contextSources:
                new AssistantContextSourceRouter(new IAssistantContextSource[] { source }));
            int before = bodies.Count;
            privateReply = true;
            AssistantReply first = await assistant.SendAsync(new AssistantRequest(command));
            Require(first.Backend == AssistantBackend.Gemini && first.Text == derivedReply,
                $"Explicit privacy context did not reach Gemini: {name}");
            Require(bodies.Count == before + 1 && bodies[^1].Contains(PrivacyContextSource.Secret, StringComparison.Ordinal),
                $"One-shot reference was absent from its explicit request: {name}");
            Require(assistant.Conversation.Turns.Count == 2 && assistant.Conversation.Turns[0].Text == command &&
                assistant.Conversation.Turns[1].Text == derivedReply, "Explicit context disappeared from session transcript.");
            Require(assistant.Conversation.GetRecentContext().Count == 0, "Explicit context entered future provider history.");
            await assistant.SendAsync(new AssistantRequest("Halo, jawab singkat."));
            Require(bodies.Count == before + 2, "Normal follow-up did not produce a Gemini request.");
            string followUp = bodies[^1];
            Require(!followUp.Contains(PrivacyContextSource.Secret, StringComparison.Ordinal), "Reference leaked to follow-up.");
            Require(!followUp.Contains(derivedReply, StringComparison.Ordinal), "Reference-derived reply leaked to follow-up.");
            Require(!followUp.Contains(command, StringComparison.OrdinalIgnoreCase) &&
                !followUp.Contains("PRIVATE_PATH_SENTINEL_315", StringComparison.Ordinal), "Private request or path leaked to follow-up.");
            Require(assistant.Conversation.GetRecentContext().Count == 2, "Normal conversation lost history retention.");
            await assistant.SendAsync(new AssistantRequest("Lanjut percakapan biasa."));
            Require(bodies[^1].Contains("Halo, jawab singkat.", StringComparison.Ordinal) &&
                bodies[^1].Contains("NORMAL_REPLY_CONTROL_77", StringComparison.Ordinal), "Normal provider history was not retained.");

            assistant.ClearConversation();
            source.Fail = true;
            int callsBeforeFailure = handler.Calls;
            AssistantReply failed = await assistant.SendAsync(new AssistantRequest(command));
            Require(failed.Backend == AssistantBackend.Local && handler.Calls == callsBeforeFailure,
                "Failed explicit context unexpectedly reached Gemini.");
            Require(assistant.Conversation.Count == 2 && assistant.Conversation.GetRecentContext().Count == 0,
                "Failed context was lost from transcript or entered future context.");
            await assistant.SendAsync(new AssistantRequest("Pesan aman setelah capture gagal."));
            Require(!bodies[^1].Contains("PRIVATE_PATH_SENTINEL_315", StringComparison.Ordinal) &&
                !bodies[^1].Contains("Clipboard test gagal.", StringComparison.Ordinal) &&
                !bodies[^1].Contains(command, StringComparison.OrdinalIgnoreCase), "Failed capture request/response leaked to follow-up.");
            assistant.ClearConversation();
        }

        var fakeApps = new DesktopAppCatalogService(() => new[]
        {
            Installed("PrivateWorkApp", "PrivateWorkApp"),
            Installed("PrivateWorkApp Two", "PrivateWorkAppTwo", "ambiguous-private"),
            Installed("PrivateWorkApp Three", "PrivateWorkAppThree", "ambiguous-private")
        });
        var fakeWindows = new FakeWindowCatalog();
        var router = new LocalDesktopCommandRouter(fakeApps, fakeWindows);
        foreach (string command in new[] { "buka PrivateWorkApp", "fokus PrivateWorkApp", "buka downloads",
                     "cari confidential-merger-plan di documents" })
            Require(router.TryRoute(command)?.Action is { IncludeInContext: false }, "Local desktop action entered context: " + command);
        foreach (string command in new[] { "buka", "buka UnknownPrivateApp", "buka ambiguous-private", "buka powershell",
                     "buka folder UnknownPrivateFolder", "cari x", "cari private-query di UnknownPrivateLocation", "fokus window PrivateWindow" })
            Require(router.TryRoute(command) is { Kind: AssistantIntentKind.LocalResponse, IncludeLocalResponseInContext: false },
                "Local desktop error entered context: " + command);
        var intents = new AssistantIntentRouter(router);
        Require(intents.Route("aplikasi apa yang sedang terbuka").Tool is { IncludeInContext: false },
            "Application-awareness query entered provider context.");

        var desktop = new FakeDesktopActionExecutor();
        var explorer = new FakeExplorerExecutor();
        var actions = new AssistantActionRouter(new IAssistantAction[]
        {
            new OpenDesktopApplicationAction(() => true, desktop, fakeApps),
            new FocusDesktopApplicationAction(() => true, desktop, fakeApps),
            new OpenExplorerFolderAction(() => true, explorer),
            new SearchExplorerAction(() => true, explorer)
        });
        var visibleApp = new DesktopApplicationContext((nint)123, 456, "PRIVATE_RUNNING_APP_629", DesktopApplicationKind.CodeEditor);
        var tools = new AssistantToolRouter(new IAssistantTool[]
        {
            new ListApplicationsTool(() => true, () => new DesktopApplicationSnapshot(visibleApp, new[] { visibleApp }))
        });
        var localAssistant = new AssistantController(chat, intentRouter: intents, actions: actions, tools: tools);
        int callsBeforeDesktop = handler.Calls;
        foreach (string command in new[] { "buka PrivateWorkApp", "fokus PrivateWorkApp", "buka downloads",
                     "cari confidential-merger-plan di documents" })
        {
            AssistantReply proposal = await localAssistant.SendAsync(new AssistantRequest(command));
            Require(proposal.ActionProposal is not null && proposal.Backend == AssistantBackend.Local,
                "Private desktop command did not create local proposal.");
            Require(localAssistant.Conversation.GetRecentContext().Count == 0, "Prepared desktop action promoted invocation privacy.");
            await localAssistant.ConfirmActionAsync(proposal.ActionProposal!.Id);
            Require(localAssistant.Conversation.GetRecentContext().Count == 0, "Confirmed desktop response entered provider context.");
        }
        foreach (string command in new[] { "buka UnknownPrivateApp", "buka ambiguous-private", "buka powershell",
                     "buka folder UnknownPrivateFolder", "cari x", "cari private-query di UnknownPrivateLocation" })
            await localAssistant.SendAsync(new AssistantRequest(command));
        AssistantReply apps = await localAssistant.SendAsync(new AssistantRequest("aplikasi apa yang sedang terbuka"));
        Require(apps.Text.Contains("PRIVATE_RUNNING_APP_629", StringComparison.Ordinal), "Awareness result disappeared from local output.");
        Require(handler.Calls == callsBeforeDesktop && localAssistant.Conversation.GetRecentContext().Count == 0,
            "Local desktop/tool transcript reached Gemini or future history.");
        Require(desktop.OpenCalls == 1 && desktop.FocusCalls == 1 && explorer.Opens == 1 && explorer.Searches == 1,
            "Privacy isolation changed local execution behavior.");
        await localAssistant.SendAsync(new AssistantRequest("Jelaskan satu fakta umum."));
        foreach (string secret in new[] { "PrivateWorkApp", "UnknownPrivateApp", "confidential-merger-plan", "PRIVATE_RUNNING_APP_629",
                     "UnknownPrivateFolder", "UnknownPrivateLocation", "ambiguous-private", "appId", "fingerprint" })
            Require(!bodies[^1].Contains(secret, StringComparison.OrdinalIgnoreCase), "Local automation metadata leaked to follow-up: " + secret);
    }

}
