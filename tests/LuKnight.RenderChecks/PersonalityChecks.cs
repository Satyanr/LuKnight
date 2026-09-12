using System.IO;
using System.Net.Http;
using System.Text.Json;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private static async Task CheckPersonalityAsync()
    {
        var engine = new PersonalityEngine();
        string identity = engine.BuildSystemInstruction();
        Require(engine.Current.Name == "Lu-Knight" && engine.Current.Id == "lu-knight", "Canonical personality changed");
        Require(!string.IsNullOrWhiteSpace(identity) && identity.Contains(engine.Current.Identity.Trim()), "Personality omits its identity");
        Require(engine.Current.Traits.All(identity.Contains) && engine.Current.InteractionRules.All(identity.Contains), "Personality omits traits or interaction rules");
        var payloads = new List<string>();
        using var client = new HttpClient(new FakeHttp(async (request, token) =>
        {
            payloads.Add(await request.Content!.ReadAsStringAsync(token));
            return JsonResponse(new { candidates = new[] { new { content = new { parts = new[] { new { text = "Hello from the test provider." } } } } } });
        }));
        var chat = new ChatCoordinator(new FakeCredentials { Key = "fake-personality-key" }, new(), () => null, client);
        var assistant = new AssistantController(chat);
        foreach (var style in Enum.GetValues<ResponseStyle>())
        {
            chat.Configure(chat.Options with { Style = style });
            await assistant.SendAsync(new("hello"));
            using var payload = JsonDocument.Parse(payloads[^1]);
            string system = payload.RootElement.GetProperty("system_instruction").GetProperty("parts")[0].GetProperty("text").GetString()!;
            Require(system.StartsWith(identity), "Response style replaces Assistant identity: " + style);
            string expectedStyle = style switch { ResponseStyle.Professional => "gaya profesional", ResponseStyle.Playful => "gaya hangat dan sedikit playful", _ => "gaya ramah dan membantu" };
            Require(system.Contains(expectedStyle) && system.Contains("memberikan hasil tindakan tersebut"), "Style or capability boundary missing: " + style);
            Require(!payload.RootElement.GetProperty("contents").GetRawText().Contains("Kepribadian utama"), "Personality leaked into provider conversation history");
        }
        Require(assistant.Conversation.Count == 6 && assistant.Conversation.Turns.All(t => t.Text is "hello" or "Hello from the test provider."),
            "Personality instruction is stored as a conversation turn");
        assistant.ClearConversation();
        Require(assistant.Conversation.Count == 0 && assistant.Personality.BuildSystemInstruction() == identity, "Clear removes personality");
        string settingsJson = JsonSerializer.Serialize(new AppSettings(), SettingsService.JsonOptions);
        Require(!settingsJson.Contains("personality", StringComparison.OrdinalIgnoreCase), "Personality leaked into settings schema");

        // An injected profile proves the provider receives Assistant-owned identity.
        var custom = new PersonalityEngine(new("test-profile", "Aster", "A test identity.", ["A test trait."], ["A test interaction rule."]));
        var customAssistant = new AssistantController(chat, custom);
        await customAssistant.SendAsync(new("custom request"));
        using (var payload = JsonDocument.Parse(payloads[^1]))
        {
            string system = payload.RootElement.GetProperty("system_instruction").GetProperty("parts")[0].GetProperty("text").GetString()!;
            Require(system.StartsWith(custom.BuildSystemInstruction()) && !system.Contains("Kamu adalah Lu-Knight"), "Gemini overrides the injected Assistant identity");
        }
        IChatService direct = new GeminiChatService(() => "fake-key", new(), client);
        await direct.SendMessageAsync("legacy direct request");
        using (var payload = JsonDocument.Parse(payloads[^1]))
        {
            string system = payload.RootElement.GetProperty("system_instruction").GetProperty("parts")[0].GetProperty("text").GetString()!;
            Require(system.Contains("Kamu adalah Lu-Knight") && system.Contains("memberikan hasil tindakan tersebut") && !system.Contains("Kepribadian utama"),
                "Legacy Gemini call loses its fallback identity or capability boundary");
        }
        await ((IChatService)chat).SendMessageAsync("legacy coordinator request");
        using (var payload = JsonDocument.Parse(payloads[^1]))
        {
            string system = payload.RootElement.GetProperty("system_instruction").GetProperty("parts")[0].GetProperty("text").GetString()!;
            Require(!system.Contains("Aster") && system.Contains("Kamu adalah Lu-Knight"), "Per-request identity leaks into the legacy coordinator overload");
        }
        foreach (string? missing in new[] { null, "", "  " })
            Require(GeminiChatService.BuildInstruction(new(), missing).Contains("Kamu adalah Lu-Knight"), "Blank identity has no fallback");
        var local = new AssistantController(new ChatCoordinator(new FakeCredentials(), new(), () => null));
        var reply = await local.SendAsync(new("hello"));
        Require(reply.Backend == AssistantBackend.Local && reply.Text == ChatCoordinator.LocalReply("hello", new()), "Personality changes local fallback behavior");
    }

    // Explicit opt-in only: three generic prompts, using configured credentials, no config writes.
    private static void CheckAssistantLive() => Task.Run(async () =>
    {
        var settings = new SettingsService(Path.Combine(SettingsService.UserDirectory, "settings.json")); settings.Load();
        var chat = new ChatCoordinator(new SecureCredentialService(), settings.Current.Chat with
        { Provider = ChatProvider.Gemini, RememberConversation = false, Language = ChatLanguage.Indonesia, ResponseLength = ResponseLength.Short });
        if (!chat.HasKey) throw new InvalidOperationException("Live check unavailable: no configured Gemini key.");
        var assistant = new AssistantController(chat);
        foreach (var style in Enum.GetValues<ResponseStyle>())
        {
            chat.Configure(chat.Options with { Style = style });
            var reply = await assistant.SendAsync(new("Siapa namamu dan apa peranmu? Sambut aku yang baru kembali bekerja setelah istirahat. Jawab singkat."));
            if (reply.Backend != AssistantBackend.Gemini) throw new InvalidOperationException("Live Gemini check failed: " + chat.Status);
            Console.WriteLine($"{style}: {reply.Text}");
        }
        Console.WriteLine("Live Assistant check completed for all three response styles; configuration unchanged.");
    }).GetAwaiter().GetResult();
}
