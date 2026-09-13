using System.Net.Http;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private static async Task CheckVoiceRoutingAsync()
    {
        int geminiCalls = 0;

        using var handler = new FakeHttp((_, _) =>
        {
            geminiCalls++;
            return Task.FromResult(JsonResponse(new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[] { new { text = "Jawaban Gemini." } }
                        }
                    }
                }
            }));
        });
        using var client = new HttpClient(handler);

        var catalog = new DesktopAppCatalogService(
            () => WindowsDesktopAppDiscovery.BuiltIns().ToList());
        var router = new AssistantIntentRouter(
            new LocalDesktopCommandRouter(catalog));
        var chat = new ChatCoordinator(
            new FakeCredentials { Key = "fake-voice-routing-key" },
            new ChatSettings
            {
                Provider = ChatProvider.Gemini,
                UseDesktopActions = true,
                UseVoiceInput = true
            },
            () => null,
            client);
        var desktop = new FakeDesktopActionExecutor();
        var explorer = new FakeExplorerExecutor();
        var actions = new AssistantActionRouter(new IAssistantAction[]
        {
            new OpenDesktopApplicationAction(
                () => chat.Options.UseDesktopActions,
                desktop,
                catalog),
            new FocusDesktopApplicationAction(
                () => chat.Options.UseDesktopActions,
                desktop,
                catalog),
            new OpenExplorerFolderAction(
                () => chat.Options.UseDesktopActions,
                explorer),
            new SearchExplorerAction(
                () => chat.Options.UseDesktopActions,
                explorer)
        });
        var assistant = new AssistantController(
            chat,
            intentRouter: router,
            actions: actions);

        AssistantReply proposed = await assistant.SendAsync(
            new AssistantRequest("buka notepad", AssistantInputSource.Voice));

        Require(
            proposed.ActionProposal is not null,
            "Voice desktop command did not create an action proposal.");
        Require(
            proposed.Backend == AssistantBackend.Local && geminiCalls == 0,
            "Voice desktop command unexpectedly used Gemini.");

        ConversationTurn voiceTurn = assistant.Conversation.Turns.First(
            x => x.Role == ConversationRole.User);
        Require(
            voiceTurn.Source == AssistantInputSource.Voice,
            "Voice input source was lost before conversation storage.");

        AssistantReply executed = await assistant.ConfirmActionAsync(
            proposed.ActionProposal!.Id);
        Require(
            executed.Backend == AssistantBackend.Local &&
            desktop.OpenCalls == 1 &&
            geminiCalls == 0,
            "Confirmed voice desktop action used Gemini or wrong executor.");

        AssistantReply conversation = await assistant.SendAsync(
            new AssistantRequest(
                "jelaskan apa itu fotosintesis",
                AssistantInputSource.Voice));
        Require(
            conversation.Backend == AssistantBackend.Gemini && geminiCalls == 1,
            "Normal voice conversation did not reach Gemini.");

        ConversationTurn latestUser = assistant.Conversation.Turns.Last(
            x => x.Role == ConversationRole.User);
        Require(
            latestUser.Source == AssistantInputSource.Voice,
            "Voice source was lost on Gemini conversation.");
    }
}
