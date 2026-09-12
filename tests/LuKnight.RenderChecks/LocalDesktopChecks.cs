using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    // Explicit opt-in only: opens real installed apps and Explorer, never part of the default suite.
    private static async Task CheckDesktopCommandsLiveAsync()
    {
        using var handler = new FakeHttp((_, _) => throw new InvalidOperationException("Live desktop command attempted Gemini."));
        using var client = new HttpClient(handler);
        var catalog = new DesktopAppCatalogService();
        var timer = Stopwatch.StartNew();
        catalog.Refresh();
        Console.WriteLine($"Discovered {catalog.Applications.Count} application targets in {timer.ElapsedMilliseconds} ms.");
        var router = new AssistantIntentRouter(new LocalDesktopCommandRouter(catalog));
        var chat = new ChatCoordinator(new FakeCredentials { Key = "unused-local-smoke-key" },
            new() { UseDesktopActions = true }, () => null, client);
        var assistant = new AssistantController(chat, intentRouter: router);
        foreach (string request in new[] { "tolong bukain photoshop dong", "bukain spotify", "coba buka discord",
                     "tolong buka downloads", "carikan LuKnight di documents", "cari invoice september" })
        {
            var reply = await assistant.SendAsync(new(request));
            if (reply.ActionProposal is { } proposal) reply = await assistant.ConfirmActionAsync(proposal.Id);
            Require(reply.Backend == AssistantBackend.Local && handler.Calls == 0, "Live desktop request used Gemini.");
            Console.WriteLine($"{request}: {reply.Text}");
        }
        Console.WriteLine($"Native launch smoke completed; Gemini requests: {handler.Calls}. Inspect the opened applications/Explorer windows for visual results.");
    }

    private sealed class FakeExplorerExecutor : IExplorerActionExecutor
    {
        public int Opens, Searches;
        public string? Query, Path;
        public DesktopActionResult OpenFolder(string path) { Opens++; Path = path; return new(true, "Folder dibuka."); }
        public DesktopActionResult Search(string query, string? path) { Searches++; Query = query; Path = path; return new(true, "Pencarian dibuka."); }
    }

    private static DesktopAppTarget Installed(string display, string executable, params string[] extraAliases)
    {
        string path = @"C:\RegisteredApps\" + executable + ".exe";
        return new(DesktopAppCatalogService.CreateId(display), display, path, [executable],
            DesktopNameNormalizer.BuildAliases(display, [executable]).Concat(extraAliases).ToArray(), DesktopAppSource.AppPaths);
    }

    private static async Task CheckLocalDesktopCommandsAsync()
    {
        var photoshop = Installed("Adobe Photoshop 2026", "Photoshop");
        var spotify = Installed("Spotify", "Spotify");
        var discord = Installed("Discord", "Update", "discord");
        var fixtures = WindowsDesktopAppDiscovery.BuiltIns().Concat([photoshop, spotify, discord]).ToList();
        int discoveries = 0;
        var now = DateTimeOffset.UtcNow;
        var catalog = new DesktopAppCatalogService(() => { discoveries++; return fixtures; }, () => now);
        var router = new AssistantIntentRouter(new LocalDesktopCommandRouter(catalog));
        string[] requests =
        [
            "tolong bukain chrome dong", "coba buka vscode", "bisa buka excel?", "bukakan spotify ya", "jalankan photoshop",
            "fokuskan ke chrome", "tolong pindah ke vscode", "balik ke excel", "buka downloads", "tolong buka folder dokumen",
            "bukain desktop dong", "cari invoice september", "carikan logo di pictures", "cari proposal di documents",
            "buka explorer dan cari LuKnight", "eh tolong bukain photoshop dong", "bantu saya buka notepad", "buka file explorer"
        ];
        foreach (string request in requests)
            Require(router.Route(request).Kind == AssistantIntentKind.Action, "Natural command failed: " + request);

        foreach (string request in new[] { "tolong buka powershell", "buka cmd", "jalankan regedit", @"open C:\Temp\evil.exe",
                     "buka aplikasi powershell", "open app https://example.org", "buka chrome & calc", "buka pwsh", "buka Windows Terminal",
                     "buka wscript", "buka rundll32", "bukain aplikasi xyz dong", "buka folder xyz", "buka", "cari x", "cari invoice di tempat-rahasia" })
            Require(router.Route(request).Kind == AssistantIntentKind.LocalResponse, "Unsafe/unknown command escaped local handling: " + request);

        foreach (string request in new[] { "tolong jelaskan cara memperbaiki warna foto ini", "apa itu Photoshop?", "jangan buka chrome", "how do I open Excel?" })
            Require(router.Route(request).Kind == AssistantIntentKind.Conversation, "Conversation was mistaken for a desktop action: " + request);
        Require(router.Route("ringkas file: C:\\Temp\\notes.txt").Kind == AssistantIntentKind.Context, "File context was hijacked by desktop routing.");
        Require(catalog.Resolve("photosop").Match?.Id == photoshop.Id, "Fuzzy typo did not resolve Photoshop.");
        Require(catalog.Resolve("vscode").Match?.Id == "vscode", "Canonical VS Code alias disappeared.");
        Require(catalog.Resolve("a").Match is null, "One-letter fuzzy app selected an arbitrary app.");
        Require(WindowsDesktopActionExecutor.MatchesProcess(discord, "Discord"), "Dynamic application process fallback failed.");
        Require(!WindowsDesktopActionExecutor.MatchesProcess(discord, "cmd"), "Focus matched a restricted process.");

        for (int i = 0; i < 4; i++) catalog.Resolve("zzzz-unknown");
        Require(discoveries == 1, "Unknown commands repeatedly scanned installed apps.");
        now = now.AddSeconds(31);
        var blender = Installed("Blender", "blender"); fixtures.Add(blender);
        Require(catalog.Resolve("blender").Match?.Id == blender.Id && discoveries == 2, "Cache miss did not discover newly installed app.");

        var duplicate = photoshop with { Id = "duplicate", Source = DesktopAppSource.StartMenu, LaunchTarget = @"C:\Menu\Photoshop.lnk", ResolvedExecutable = photoshop.LaunchTarget };
        var dedup = new DesktopAppCatalogService(() => [photoshop, duplicate]);
        Require(dedup.Applications.Count == 1 && dedup.Resolve("photoshop").Found, "Start Menu/App Paths duplicate caused ambiguity.");
        var packaged = WindowsDesktopAppDiscovery.FromAppPath("Spotify.exe", @"C:\RegisteredApps\spotify_cli.exe");
        Require(packaged.DisplayName == "Spotify" && packaged.ProcessNames.Contains("Spotify") &&
            new DesktopAppCatalogService(() => [packaged]).Resolve("spotify").Found,
            "App Paths registration name was lost behind a packaged activation helper.");
        var updater = discord with { Source = DesktopAppSource.StartMenu, LaunchTarget = @"C:\Menu\Discord.lnk",
            ResolvedExecutable = @"C:\Discord\Update.exe", Arguments = "--processStart Discord.exe", RegistrationIdentity = @"C:\Discord\Discord.exe" };
        var directDiscord = discord with { Id = "direct-discord", LaunchTarget = @"C:\Discord\app-1.0.9257\Discord.exe",
            ProcessNames = ["Discord"], RegistrationIdentity = updater.RegistrationIdentity };
        var squirrelCatalog = new DesktopAppCatalogService(() => [directDiscord, updater]);
        Require(squirrelCatalog.Applications.Count == 1 && squirrelCatalog.Resolve("discord").Match?.Arguments == updater.Arguments,
            "Direct and updater Discord registrations were not merged into the stable launcher.");
        Require(WindowsDesktopAppDiscovery.SquirrelIdentity(updater.ResolvedExecutable!, updater.Arguments, out var startedProcess) == updater.RegistrationIdentity && startedProcess == "Discord",
            "Updater process identity was not read correctly.");
        var oldPhotoshop = Installed("Adobe Photoshop 2025", "Photoshop2025", "photoshop");
        fixtures.Add(oldPhotoshop); catalog.Refresh();
        Require(catalog.Resolve("photoshop").Ambiguous, "Multiple app versions were silently chosen.");
        Require(catalog.Resolve("Adobe Photoshop 2026").Match?.Id == photoshop.Id, "Specific version did not resolve an ambiguity.");
        Require(router.Route("buka photoshop").Kind == AssistantIntentKind.LocalResponse, "Ambiguity fell through to Gemini.");

        foreach (string exe in new[] { "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "rundll32", "regsvr32", "regedit", "diskpart", "wt" })
        {
            var disguised = Installed("Friendly Notes", exe);
            var restrictedCatalog = new DesktopAppCatalogService(() => [disguised]);
            Require(restrictedCatalog.Applications.Count == 0, "Restricted executable indexed under an innocent display name: " + exe);
        }
        Require(!DesktopAppPolicy.IsAllowed(Installed("Mystery", "safe") with { LaunchTarget = @"C:\Menu\Mystery.lnk", Source = DesktopAppSource.StartMenu }),
            "Unresolved shortcut was allowed.");
        Require(!DesktopAppPolicy.IsAllowed(Installed("Friendly Notes", "Update") with { Arguments = "--processStart powershell.exe" }),
            "Launcher arguments hid a restricted executable.");

        int geminiCalls = 0;
        using var handler = new FakeHttp((_, _) =>
        {
            geminiCalls++;
            return Task.FromResult(JsonResponse(new { candidates = new[] { new { content = new { parts = new[] { new { text = "Jawaban online." } } } } } }));
        });
        using var client = new HttpClient(handler);
        var chat = new ChatCoordinator(new FakeCredentials { Key = "fake-desktop-test-key" }, new() { UseDesktopActions = true }, () => null, client);
        var desktop = new FakeDesktopActionExecutor();
        var explorer = new FakeExplorerExecutor();
        var actions = new AssistantActionRouter(new IAssistantAction[]
        {
            new OpenDesktopApplicationAction(() => chat.Options.UseDesktopActions, desktop, catalog),
            new FocusDesktopApplicationAction(() => chat.Options.UseDesktopActions, desktop, catalog),
            new OpenExplorerFolderAction(() => chat.Options.UseDesktopActions, explorer),
            new SearchExplorerAction(() => chat.Options.UseDesktopActions, explorer)
        });
        var assistant = new AssistantController(chat, intentRouter: router, actions: actions);
        foreach (string request in new[] { "tolong bukain notepad dong", "fokuskan ke chrome", "tolong buka downloads", "carikan LuKnight di documents", "cari invoice september" })
        {
            var proposed = await assistant.SendAsync(new(request));
            Require(proposed.ActionProposal is not null && proposed.Backend == AssistantBackend.Local && geminiCalls == 0,
                "Desktop preparation unexpectedly used Gemini: " + request);
            var executed = await assistant.ConfirmActionAsync(proposed.ActionProposal!.Id);
            Require(executed.Backend == AssistantBackend.Local && executed.Emotion == AssistantEmotion.Happy && geminiCalls == 0,
                "Confirmed desktop action unexpectedly used Gemini: " + request);
        }
        Require(desktop.OpenCalls == 1 && desktop.FocusCalls == 1 && explorer.Opens == 1 && explorer.Searches == 2,
            "Local commands reached the wrong executor.");
        foreach (string request in new[] { "bukain aplikasi xyz dong", "buka photoshop", "buka powershell", "buka folder unknown" })
        {
            var local = await assistant.SendAsync(new(request));
            Require(local.Backend == AssistantBackend.Local && local.ActionProposal is null && geminiCalls == 0,
                "Unknown/restricted/ambiguous command invoked Gemini.");
        }
        var cancelled = await assistant.SendAsync(new("buka chrome"));
        assistant.CancelAction(cancelled.ActionProposal!.Id);
        Require(desktop.OpenCalls == 1 && geminiCalls == 0, "Cancellation executed or used Gemini.");
        var revoked = await assistant.SendAsync(new("cari invoice september"));
        chat.Configure(chat.Options with { UseDesktopActions = false });
        await assistant.ConfirmActionAsync(revoked.ActionProposal!.Id);
        Require(explorer.Searches == 2 && geminiCalls == 0, "Explorer ignored setting revocation.");
        Require((await assistant.SendAsync(new("buka chrome"))).ActionProposal is null && geminiCalls == 0,
            "Disabled action used Gemini or proposed execution.");
        chat.Configure(chat.Options with { UseDesktopActions = true });
        var changing = await assistant.SendAsync(new("buka spotify"));
        fixtures[fixtures.IndexOf(spotify)] = spotify with { Arguments = "--changed-after-confirmation" }; catalog.Refresh();
        var changedReply = await assistant.ConfirmActionAsync(changing.ActionProposal!.Id);
        Require(changedReply.Emotion == AssistantEmotion.Confused && desktop.OpenCalls == 1 && geminiCalls == 0,
            "Changed application registration executed an unconfirmed target.");
        await assistant.SendAsync(new("tolong jelaskan cara memperbaiki warna foto ini"));
        Require(geminiCalls == 1, "Normal reasoning did not reach Gemini (zero-request test lacks a positive control).");

        string fileQuery = "Laporan Q3-2026.pdf & crumb=location:C:\\Other";
        var parsedSearch = router.Route("cari " + fileQuery + " di documents");
        Require(parsedSearch.Action?.Arguments["query"] == fileQuery, "Search changed file punctuation or letter case.");
        ProcessStartInfo? launch = null;
        var windowsExplorer = new WindowsExplorerActionExecutor(info => launch = info);
        Require(windowsExplorer.Search(fileQuery, null).Success && launch?.FileName == "search-ms:query=" + Uri.EscapeDataString(fileQuery),
            "Search URI allowed query data to inject extra URI parameters.");
        launch = null;
        Require(!windowsExplorer.Search("invoice", @"C:\Missing-" + Guid.NewGuid()).Success && launch is null,
            "Missing explicit search scope silently broadened the search.");
        await CheckShortcutDiscoveryAsync();
        Console.WriteLine("Local Desktop Command Engine: parser, discovery fixtures, ambiguity, security, Explorer and zero-Gemini checks passed.");
    }

    private static Task CheckShortcutDiscoveryAsync()
    {
        // Own disposable shortcuts only; these are never launched.
        string root = Path.Combine(Path.GetTempPath(), "LuKnight-shortcuts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            void WriteLink(string path, string target)
            {
                object link = ((dynamic)shell!).CreateShortcut(path);
                try { ((dynamic)link).TargetPath = target; ((dynamic)link).Save(); }
                finally { Marshal.FinalReleaseComObject(link); }
            }
            string shortcut = Path.Combine(root, "Friendly Notes.lnk");
            WriteLink(shortcut, Path.Combine(Environment.SystemDirectory, "notepad.exe"));
            var safe = WindowsDesktopAppDiscovery.ReadShortcut(shortcut);
            Require(safe is not null && safe.ProcessNames.Contains("notepad"), "Real shortcut metadata could not be discovered.");
            WriteLink(shortcut, Path.Combine(Environment.SystemDirectory, "cmd.exe"));
            Require(WindowsDesktopAppDiscovery.ReadShortcut(shortcut) is null, "Disguised shell shortcut was discovered as a normal app.");
            Require(!WindowsDesktopAppDiscovery.IsLaunchUnchanged(safe!), "Replaced shortcut passed launch revalidation.");
            WriteLink(shortcut, Path.Combine(root, "missing.exe"));
            Require(WindowsDesktopAppDiscovery.ReadShortcut(shortcut) is null, "Broken shortcut was indexed.");
        }
        finally
        {
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
            Directory.Delete(root, true);
        }
        return Task.CompletedTask;
    }
}
