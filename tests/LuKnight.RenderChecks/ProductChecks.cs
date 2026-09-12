using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LuKnight.Behaviors;
using LuKnight.Models;
using LuKnight.Services;
using LuKnight.ViewModels;

internal static partial class Program
{
    private sealed class FakeCredentials : ICredentialService
    {
        public string? Key;
        public string? Read() => Key;
        public void Write(string key) => Key = key;
        public void Remove() => Key = null;
    }
    private sealed class FakeHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return respond(request, token); }
    }
    private static HttpResponseMessage JsonResponse(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    // Service checks must not capture the WPF dispatcher installed by earlier render checks.
    private static void CheckProducts() => Task.Run(CheckProductsAsync).GetAwaiter().GetResult();
    private static async Task CheckProductsAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "LuKnight-product-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "settings.json"); var settings = new SettingsService(file); settings.Load();
            Require(settings.Current == new AppSettings(), "Fresh configuration is not default");
            var customized = settings.Current with
            {
                General = new(true, false), Behavior = new() { Activity = ActivityLevel.Active, Speed = MovementSpeed.Fast, LookAtCursor = false },
                Chat = new() { Provider = ChatProvider.Local, Language = ChatLanguage.English, ResponseLength = ResponseLength.Detailed, RememberConversation = false },
                SettingsWindow = new(-20, 30, 900, 680), Mascot = new(250, 0, 0)
            };
            Require(settings.Update(customized), "Configuration save failed");
            var reloaded = new SettingsService(file); reloaded.Load(); Require(reloaded.Current == customized, "Configuration does not survive a new service instance");
            string json = File.ReadAllText(file);
            Require(!json.Contains("speedFactor") && !json.Contains("canSleep") && !json.Contains("apiKey", StringComparison.OrdinalIgnoreCase), "Config contains derived state or secret fields");
            settings.Update(customized with { General = new(false, true) });
            Require(File.Exists(file + ".bak") && !File.Exists(file + ".tmp"), "Atomic replacement does not preserve the previous file");
            Require(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file + ".bak"), SettingsService.JsonOptions) == customized, "Backup is not the last valid configuration");
            File.WriteAllText(file, "{broken"); var corrupt = new SettingsService(file); corrupt.Load();
            Require(corrupt.Current == new AppSettings() && Directory.GetFiles(directory, "settings.json.invalid-*.bak").Length == 1, "Broken config fails recovery or backup");
            Require(corrupt.Save(), "Recovered defaults cannot be saved");
            File.WriteAllText(file, "{\"schemaVersion\":999,\"future\":true}"); var future = new SettingsService(file); future.Load();
            Require(!future.Save() && File.ReadAllText(file).Contains("999"), "Older app overwrites newer configuration");
            File.WriteAllText(file, "{\"general\":{\"alwaysOnTop\":false}}"); var old = new SettingsService(file); old.Load();
            Require(old.Current.SchemaVersion == 1 && !old.Current.General.AlwaysOnTop, "Schema zero migration loses settings");
            File.WriteAllText(file, "{\"schemaVersion\":1,\"chat\":null}"); var missing = new SettingsService(file); missing.Load();
            Require(missing.Current.Chat is not null, "Null section crashes configuration load");
            var invalidPlacement = SettingsService.Validate(new() { SettingsWindow = new(double.NaN, 0, 900, 600), Mascot = new(double.PositiveInfinity, 0, 0) });
            Require(invalidPlacement.SettingsWindow is null && invalidPlacement.Mascot is null, "Invalid coordinates reach window placement");
            string parentFile = Path.Combine(directory, "not-a-directory"); File.WriteAllText(parentFile, "sentinel");
            var denied = new SettingsService(Path.Combine(parentFile, "settings.json"));
            Require(!denied.Update(customized) && denied.Current == customized && File.ReadAllText(parentFile) == "sentinel", "Save failure loses session settings or modifies unrelated file");
            settings.Reset(); Require(settings.Current == new AppSettings(), "Configuration reset is incomplete");

            var credentials = new FakeCredentials { Key = "test-key-not-a-real-secret" };
            var payloads = new List<string>();
            using var handler = new FakeHttp(async (request, token) =>
            {
                Require(request.Headers.Contains("x-goog-api-key") && !request.RequestUri!.Query.Contains("key"), "Credential sent in URL instead of header");
                payloads.Add(await request.Content!.ReadAsStringAsync(token));
                return JsonResponse(new { candidates = new[] { new { content = new { parts = new[] { new { text = "Test answer" } } } } } });
            });
            using var client = new HttpClient(handler);
            var chat = new ChatCoordinator(credentials, new(), () => null, client);
            Require(chat.UsesGemini && chat.CredentialStatus.Contains("••••") && !chat.CredentialStatus.Contains(credentials.Key!), "Credential status exposes a key");
            await chat.SendMessageAsync("first"); await chat.SendMessageAsync("second");
            Require(JsonDocument.Parse(payloads[1]).RootElement.GetProperty("contents").GetArrayLength() == 1, "Coordinator unexpectedly owns session history");
            chat.ClearConversation(); await chat.SendMessageAsync("fresh");
            Require(JsonDocument.Parse(payloads[2]).RootElement.GetProperty("contents").GetArrayLength() == 1, "Clear conversation retains old prompts");
            chat.Configure(new() { RememberConversation = false, Language = ChatLanguage.English, ResponseLength = ResponseLength.Short, Style = ResponseStyle.Professional });
            await chat.SendMessageAsync("a"); await chat.SendMessageAsync("b");
            Require(JsonDocument.Parse(payloads[^1]).RootElement.GetProperty("contents").GetArrayLength() == 1, "Remember OFF retains history");
            Require(payloads[^1].Contains("Reply in English") && payloads[^1].Contains("dua kalimat pendek") && payloads[^1].Contains("gaya profesional"), "Preferences do not reach Gemini instruction");
            Require(await chat.TestConnection() && chat.Status.Contains("connected"), "Successful test connection is not reported");
            chat.RemoveKey(); Require(!chat.HasKey && credentials.Key is null, "Removing key leaves it active");
            chat.UpdateKey("another-fake-key"); Require(chat.HasKey, "Updating credential does not activate Gemini");
            var envChat = new ChatCoordinator(new FakeCredentials(), new(), () => "environment-test-key", client);
            Require(envChat.HasKey && envChat.CredentialStatus.Contains("environment"), "Environment fallback unavailable");
            envChat.RemoveKey(); Require(envChat.HasKey, "Removing saved credential erases independent environment fallback");
            using var failure = new HttpClient(new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("secret-test-key") })));
            var offline = new ChatCoordinator(credentials, new(), () => null, failure);
            string fallback = await offline.SendMessageAsync("tolong bantu");
            Require(!offline.LastReplyWasGemini && offline.Status.Contains("Local fallback") && fallback.Contains("mode lokal") && !offline.Status.Contains("secret-test-key"), "AI failure does not safely fall back");
            Require(!await offline.TestConnection() && offline.Status.Contains("unavailable"), "Failed test reports connected");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await offline.SendMessageAsync("cancel", cancelled.Token); throw new Exception("Cancellation swallowed"); }
            catch (OperationCanceledException) { Require(!offline.IsBusy, "Cancelled request leaves busy state"); }
            var pending = new TaskCompletionSource<HttpResponseMessage>();
            using var slow = new HttpClient(new FakeHttp((_, _) => pending.Task));
            var busyChat = new ChatCoordinator(credentials, new(), () => null, slow); var sending = busyChat.SendMessageAsync("waiting");
            try { busyChat.ClearConversation(); throw new Exception("Busy clear accepted"); }
            catch (InvalidOperationException) { Require(busyChat.IsBusy, "Busy guard loses request state"); }
            pending.SetResult(JsonResponse(new { candidates = new[] { new { content = new { parts = new[] { new { text = "done" } } } } } })); await sending;

            byte[] installer = Encoding.UTF8.GetBytes("Fake installer used only with an injected launcher.");
            string hash = Convert.ToHexString(SHA256.HashData(installer));
            var manifest = new UpdateManifest("9.0.0", "https://github.com/Satyanr/LuKnight/releases/download/v9.0.0/LuKnightSetup.exe", hash, installer.Length);
            bool corruptDownload = false; string? sourceOverride = null;
            using var updateHandler = new FakeHttp((request, _) =>
            {
                if (request.RequestUri!.Host == "api.github.com")
                    return Task.FromResult(JsonResponse(new { tag_name = "v9.0.0", draft = false, prerelease = false, assets = new[] { new { name = "update.json", browser_download_url = "https://github.com/Satyanr/LuKnight/releases/download/v9.0.0/update.json" } } }));
                if (request.RequestUri.AbsolutePath.EndsWith("update.json"))
                    return Task.FromResult(JsonResponse(new { version = manifest.Version, url = sourceOverride ?? manifest.Url, sha256 = manifest.Sha256, size = manifest.Size }));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(corruptDownload ? new byte[installer.Length] : installer) });
            });
            using var updateClient = new HttpClient(updateHandler); int launches = 0, exits = 0;
            var updates = new UpdateService(new SettingsService(), updateClient, Path.Combine(directory, "updates"), start =>
            { launches++; Require(start.ArgumentList.Contains("/UPDATE") && start.ArgumentList.Any(a => a.StartsWith("/TARGETPID=")), "Installer handoff omits process wait"); return true; });
            Require(await updates.CheckForUpdate() == manifest, "Release manifest not discovered");
            int calls = updateHandler.Calls; await updates.CheckForUpdate(true);
            Require(updateHandler.Calls == calls, "Automatic checks ignore 24-hour limit");
            Require(await updates.DownloadUpdate() && updates.VerifiedInstaller is not null, "Valid installer fails checksum verification");
            Require(!await updates.InstallUpdate(() => false, () => true, () => exits++) && launches == 0, "Installer starts during active drag/chat/physics");
            Require(!await updates.InstallUpdate(() => true, () => false, () => exits++) && launches == 0, "Installer starts despite failed save");
            File.WriteAllText(updates.VerifiedInstaller!, "tampered");
            Require(!await updates.InstallUpdate(() => true, () => true, () => exits++) && launches == 0, "Modified installer executed after verification");
            Require(await updates.DownloadUpdate() && await updates.InstallUpdate(() => true, () => true, () => exits++) && launches == 1 && exits == 1, "Verified idle install does not launch and hand off shutdown");
            corruptDownload = true;
            Require(!await updates.DownloadUpdate() && updates.VerifiedInstaller is null && Directory.GetFiles(Path.Combine(directory, "updates"), "*.part").Length == 0, "Checksum mismatch leaves an executable update enabled");
            sourceOverride = "https://attacker.invalid/setup.exe";
            Require(await updates.CheckForUpdate() is null, "Update accepts external installer source");
            foreach (string invalid in new[] { "../1.0.0", "1.0", "1.0.0-beta", "01.0.0" })
            {
                try { UpdateService.ParseVersion(invalid); throw new Exception("Invalid version accepted"); }
                catch (InvalidDataException) { Require(true, "Version validation"); }
            }
            using var unavailable = new HttpClient(new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
            var noRelease = new UpdateService(new SettingsService(), unavailable, directory);
            Require(await noRelease.CheckForUpdate() is null && noRelease.Status.Contains("Belum ada"), "Missing public release is reported as an available update");
            using var malformedClient = new HttpClient(new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("not json") })));
            var malformed = new UpdateService(new SettingsService(), malformedClient, directory);
            Require(await malformed.CheckForUpdate() is null && !malformed.IsBusy, "Malformed release response crashes or retains busy state");
            using var timeoutClient = new HttpClient(new FakeHttp((_, _) => Task.FromException<HttpResponseMessage>(new OperationCanceledException())));
            var timedOut = new UpdateService(new SettingsService(), timeoutClient, directory);
            Require(await timedOut.CheckForUpdate() is null && !timedOut.IsBusy && timedOut.Status.Contains("habis"), "Timeout leaves a checking status or escapes into UI");
            sourceOverride = null; corruptDownload = false;
            var launchFailure = new UpdateService(new SettingsService(), updateClient, Path.Combine(directory, "failed-launch"), _ => throw new System.ComponentModel.Win32Exception());
            await launchFailure.CheckForUpdate(); await launchFailure.DownloadUpdate();
            Require(!await launchFailure.InstallUpdate(() => true, () => true, () => exits++) && exits == 1, "Failed installer launch shuts down the app");
            using var stoppedDownload = new CancellationTokenSource(); stoppedDownload.Cancel();
            try { await launchFailure.DownloadUpdate(stoppedDownload.Token); throw new Exception("Download cancellation ignored"); }
            catch (OperationCanceledException)
            { Require(!launchFailure.IsBusy && launchFailure.VerifiedInstaller is null, "Cancelled download leaves stale install permission"); }
            Console.WriteLine("Product checks use fake credentials, HTTP, and installer launch; no real keys, releases, or installations changed.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
