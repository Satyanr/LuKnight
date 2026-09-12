using System.Net;
using System.Net.Http;
using LuKnight.Services;
using LuKnight.Models;

internal static partial class Program
{
    private static async Task CheckGeminiFailoverAsync()
    {
        Require(GeminiModelCatalog.BuildFailoverSequence("gemini-3.6-flash", true)
            .SequenceEqual(GeminiModelCatalog.AssistantModels.Skip(2)), "Fallback upgraded a selected model.");
        Require(GeminiModelCatalog.BuildFailoverSequence("custom", true).Count == 6, "Custom model chain missing.");
        foreach (HttpStatusCode status in new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable, HttpStatusCode.NotFound,
            HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden })
        foreach (bool enabled in new[] { true, false })
        {
            var payloads = new List<string>();
            using var handler = new FakeHttp(async (request, token) =>
            {
                payloads.Add(await request.Content!.ReadAsStringAsync(token));
                Require(request.Headers.GetValues("x-goog-api-key").Single() == "fake", "Fallback changed credentials.");
                if (payloads.Count == 1) return new HttpResponseMessage(status);
                Require(request.RequestUri!.AbsolutePath.Contains("gemini-3.7-flash"), "Wrong fallback model.");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    """{"candidates":[{"content":{"parts":[{"text":"fallback works"}]}}]}""") };
            });
            using var client = new HttpClient(handler);
            var service = new GeminiChatService(() => "fake", new() { AutoModelFallback = enabled }, client);
            bool retry = enabled && status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.NotFound;
            try
            {
                Require(await service.SendMessageAsync("hello") == "fallback works" && retry, "Unexpected success.");
                Require(service.UsedFallbackModel && service.LastModelUsed == "gemini-3.7-flash", "Fallback metadata missing.");
                Require(payloads[0] == payloads[1], "Fallback changed the payload.");
            }
            catch (GeminiApiException ex) { Require(!retry && ex.StatusCode == status, "Incorrect error handling."); }
            Require(handler.Calls == (retry ? 2 : 1), "Wrong retry count.");
            service.Configure(new());
            Require(service.LastModelUsed is null && !service.UsedFallbackModel && service.LastAttemptedModels.Count == 0, "Stale attempt metadata.");
        }
        using var exhaustedHandler = new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        using var exhaustedClient = new HttpClient(exhaustedHandler);
        var chat = new ChatCoordinator(new FakeCredentials { Key = "fake" }, new(), () => null, exhaustedClient);
        await chat.SendMessageAsync("hello");
        Require(!chat.LastReplyWasGemini && exhaustedHandler.Calls == 5 && chat.Status.Contains("Local fallback"), "Exhausted chain did not reach local mode.");
        foreach (string body in new[] { """{"promptFeedback":{"blockReason":"SAFETY"}}""", "broken json",
            """{"candidates":[{"finishReason":"SAFETY"}]}""" })
        {
            using var handler = new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
            using var client = new HttpClient(handler);
            var blocked = new ChatCoordinator(new FakeCredentials { Key = "fake" }, new(), () => null, client);
            await blocked.SendMessageAsync("hello");
            Require(handler.Calls == 1 && !blocked.LastReplyWasGemini, "Safety/invalid response triggered model fallback.");
        }
        using var cancelled = new CancellationTokenSource();
        using var cancelHandler = new FakeHttp((_, _) =>
        {
            cancelled.Cancel();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        });
        using var cancelClient = new HttpClient(cancelHandler);
        try { await new GeminiChatService(() => "fake", new(), cancelClient).SendMessageAsync("hello", cancelled.Token); Require(false, "Cancellation ignored."); }
        catch (OperationCanceledException) { Require(cancelHandler.Calls == 1, "Cancellation retried."); }
    }

    private static void CheckPackagedApps()
    {
        var app = new DesktopAppTarget("store", "Spotify", "shell:AppsFolder\\Spotify.Package_123!App", [], ["spotify"], DesktopAppSource.AppsFolder)
        { AppUserModelId = "Spotify.Package_123!App" };
        Require(DesktopAppPolicy.IsAllowed(app) && WindowsDesktopAppDiscovery.IsLaunchUnchanged(app), "Valid packaged app rejected.");
        foreach (string invalid in new[] { "", "pkg", "pkg!app\n", "pkg!app & calc", "pkg!app/evil", "pkg!app!other", new string('a', 261) + "!App" })
            Require(!DesktopAppPolicy.IsValidAppUserModelId(invalid), "Unsafe AUMID accepted.");
        Require(!DesktopAppPolicy.IsAllowed(app with { AppUserModelId = "Microsoft.WindowsTerminal_123!App" }), "Terminal package accepted.");
        Require(!DesktopAppPolicy.IsAllowed(app with { LaunchTarget = "evil.exe" }), "Inconsistent launch target accepted.");
        Require(app.Fingerprint != (app with { AppUserModelId = "Other.Package!App" }).Fingerprint, "AUMID omitted from confirmation fingerprint.");
        Require(!WindowsDesktopActionExecutor.MatchesProcess(app, "spotify"), "Packaged focus guessed a process.");
        var native = Installed("Spotify", "Spotify");
        var catalog = new DesktopAppCatalogService(() => [native, app]);
        Require(catalog.Applications.Count == 1 && catalog.Resolve("spotify").Match?.Source == DesktopAppSource.AppPaths, "Native/store duplicate remains.");
        using var finished = new ManualResetEventSlim();
        ApartmentState apartment = ApartmentState.Unknown;
        var warm = new DesktopAppCatalogService(() => { apartment = Thread.CurrentThread.GetApartmentState(); finished.Set(); return [app]; });
        DesktopAppIndexWarmup.Start(warm);
        Require(finished.Wait(TimeSpan.FromSeconds(5)) && apartment == ApartmentState.STA, "Warm-up did not run on an STA thread.");
        Require(warm.Resolve("spotify").Found, "Warm-up failed to publish catalog.");
    }
}
