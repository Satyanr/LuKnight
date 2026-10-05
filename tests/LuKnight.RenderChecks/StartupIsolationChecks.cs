using System.IO;
using System.Text.Json;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private sealed class ThrowingWarmupCatalog : IDesktopAppCatalog
    {
        public TaskCompletionSource<bool> Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<DesktopAppTarget> Applications => [];
        public DesktopAppResolution Resolve(string query) => new(null, [], false);
        public bool TryResolveById(string id, out DesktopAppTarget target) { target = default!; return false; }
        public void Refresh()
        {
            Attempted.TrySetResult(true);
            throw new IOException("Synthetic discovery failure containing private-startup-probe.");
        }
    }

    private static async Task CheckStartupIsolationAsync()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int attempts = 0;
        var catalog = new DesktopAppCatalogService(() =>
        {
            attempts++;
            throw new IOException("private-startup-probe");
        }, () => now);
        catalog.Refresh();
        Require(!catalog.LastRefreshSucceeded, "Failed app discovery was reported as healthy.");
        Require(catalog.Applications.Count > 0 && catalog.Applications.All(DesktopAppPolicy.IsAllowed) &&
            catalog.TryResolveById("notepad", out _), "First-run discovery failure did not preserve safe built-ins.");
        Require(catalog.Resolve("notepad").Found && !catalog.Resolve("powershell").Found,
            "Fallback resolution lost built-ins or exposed restricted applications.");
        catalog.Resolve("zzzzunregisteredzzzz");
        Require(attempts == 1, "Fallback reads/misses immediately retried failed discovery.");
        Require(!catalog.RefreshStatus.Contains("private-startup-probe", StringComparison.Ordinal),
            "Catalog health exposed raw discovery diagnostics.");
        now = now.AddSeconds(31);
        catalog.Resolve("zzzzunregisteredzzzz");
        catalog.Resolve("zzzzunregisteredzzzz");
        Require(attempts == 2, "Failed miss refresh was not rate-limited.");
        now = now.AddMinutes(10);
        _ = catalog.Applications;
        Require(attempts == 3, "Fallback never retried discovery after expiry.");

        bool fail = false;
        IEnumerable<DesktopAppTarget> Discover()
        {
            yield return Installed(fail ? "PartialProbe" : "CommittedProbe", fail ? "PartialProbe" : "CommittedProbe");
            if (fail) throw new System.Runtime.InteropServices.COMException("private-startup-probe");
        }
        var lastGood = new DesktopAppCatalogService(Discover);
        lastGood.Refresh();
        IReadOnlyList<DesktopAppTarget> committed = lastGood.Applications;
        Require(lastGood.LastRefreshSucceeded && committed.Count == 1, "Committed snapshot fixture failed.");
        fail = true;
        lastGood.Refresh();
        Require(!lastGood.LastRefreshSucceeded && ReferenceEquals(committed, lastGood.Applications) &&
            lastGood.Resolve("CommittedProbe").Found && !lastGood.Resolve("PartialProbe").Found,
            "Failed enumeration published partial discovery or lost last-known-good state.");
        fail = false;
        lastGood.Refresh();
        Require(lastGood.LastRefreshSucceeded && !ReferenceEquals(committed, lastGood.Applications),
            "Successful retry did not clear degraded health and commit a new snapshot.");

        foreach (Exception failure in new Exception[] { new UnauthorizedAccessException(), new System.Security.SecurityException(),
            new System.ComponentModel.Win32Exception(), new PlatformNotSupportedException(), new InvalidOperationException(),
            new ArgumentException(), new Microsoft.CSharp.RuntimeBinder.RuntimeBinderException() })
        {
            var failed = new DesktopAppCatalogService(() => throw failure);
            failed.Refresh();
            Require(!failed.LastRefreshSucceeded && failed.TryResolveById("notepad", out _),
                "Expected discovery failure did not degrade safely: " + failure.GetType().Name);
        }
        bool programmingErrorSurfaced = false;
        try { new DesktopAppCatalogService(() => throw new NullReferenceException()).Refresh(); }
        catch (NullReferenceException) { programmingErrorSurfaced = true; }
        Require(programmingErrorSurfaced, "Unexpected core error was silently converted to fallback.");

        var reported = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DesktopAppIndexWarmup.Start(catalog, () => reported.TrySetResult(true));
        await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Require(!catalog.LastRefreshSucceeded, "Warm-up did not report degraded catalog health.");
        var callbackAttempted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DesktopAppIndexWarmup.Start(new ThrowingWarmupCatalog(), () =>
        {
            callbackAttempted.TrySetResult(true);
            throw new InvalidOperationException("Synthetic diagnostic callback failure.");
        });
        await callbackAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Require(true, "Warm-up diagnostic callback was not invoked.");

        string directory = Path.Combine(Path.GetTempPath(), "LuKnight-startup-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var servicesToClose = new List<AppServices>();
        try
        {
            var settings = new SettingsService();
            settings.Update(settings.Current with { Chat = settings.Current.Chat with { Provider = ChatProvider.Local } });
            AppServices Create(IDesktopAppCatalog apps, string skills, string schedules)
            {
                var services = new AppServices(settings: settings, credentials: new FakeCredentials(), desktopApps: apps,
                    desktopWindows: new FakeWindowCatalog(), voiceCapture: new ShutdownVoiceCapture(),
                    textToSpeech: new ShutdownTextToSpeech(), userSkillStore: new UserSkillStore(skills),
                    scheduleStore: new LocalScheduleStore(schedules));
                servicesToClose.Add(services);
                return services;
            }
            var throwing = new ThrowingWarmupCatalog();
            AppServices services = Create(throwing, Path.Combine(directory, "skills"), Path.Combine(directory, "schedules.json"));
            await throwing.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(SpinWait.SpinUntil(() => services.StartupIssues.Any(issue => issue.Module == AppStartupModule.DesktopAppIndex),
                TimeSpan.FromSeconds(5)), "Warm-up failure was not isolated/reported.");
            AssistantReply local = await services.Assistant.SendAsync(new AssistantRequest("halo"));
            Require(local.Backend == AssistantBackend.Local && !services.Assistant.IsBusy,
                "Optional desktop-index failure broke core assistant.");
            IReadOnlyList<AppStartupIssue> snapshot = services.StartupIssues;
            var record = typeof(AppServices).GetMethod("RecordStartupIssue", Private)!;
            Parallel.For(0, 20, _ => record.Invoke(services, [AppStartupModule.DesktopAppIndex, "Duplicate probe"]));
            Require(snapshot.Count == 1 && services.StartupIssues.Count == 1,
                "Startup diagnostics retained duplicates or exposed mutable shared state.");

            string skills = Path.Combine(directory, "corrupt-skills");
            Directory.CreateDirectory(skills);
            File.WriteAllText(Path.Combine(skills, "private-startup-probe.json"), "{ broken");
            File.WriteAllText(Path.Combine(skills, "valid.json"), """
                { "schemaVersion": 1, "enabled": true, "id": "startup-probe", "displayName": "Startup Probe",
                  "description": "Local startup fixture.", "aliases": ["startup-probe"],
                  "steps": ["buka documents"] }
                """);
            string schedules = Path.Combine(directory, "corrupt-schedules.json");
            File.WriteAllText(schedules, "{ broken");
            AppServices degraded = Create(new MutablePlanAppCatalog(), skills, schedules);
            Require(degraded.UserSkillIssues.Count > 0 && degraded.StartupIssues.Any(issue => issue.Module == AppStartupModule.UserSkills) &&
                degraded.Skills.Catalog.Any(skill => skill.Id == "startup-probe"), "Corrupt user skill broke valid skill registration.");
            Require(degraded.Scheduler.LoadIssues.Count > 0 && degraded.StartupIssues.Any(issue => issue.Module == AppStartupModule.Scheduler),
                "Corrupt scheduler state was not isolated.");
            Require((await degraded.Assistant.SendAsync(new AssistantRequest("halo"))).Backend == AssistantBackend.Local,
                "Degraded optional modules prevented core assistant startup.");
            Require(degraded.StartupIssues.All(issue => !issue.Message.Contains("private-startup-probe", StringComparison.Ordinal)),
                "Aggregate startup diagnostics exposed filenames or raw exceptions.");
            Require(!degraded.Context.BuildSystemInstruction().Contains("skill valid tetap tersedia", StringComparison.Ordinal) &&
                !degraded.Context.BuildSystemInstruction().Contains("jadwal valid tetap tersedia", StringComparison.Ordinal) && degraded.Assistant.Memory.Items.Count == 0 &&
                degraded.Assistant.Conversation.Turns.All(turn => !turn.Text.Contains("jadwal lokal", StringComparison.Ordinal)),
                "Startup diagnostics entered assistant context, conversation or long-term memory.");

            var validSchedule = new ScheduledSkill
            {
                Id = Guid.NewGuid(), DisplayName = "Startup schedule", Enabled = true,
                CreatedAtUtc = now.AddHours(-2), DueAtUtc = now.AddHours(-1),
                Invocation = new ScheduledSkillInvocation { SkillId = "search-downloads", Argument = "invoice" }
            };
            string mixedPath = Path.Combine(directory, "mixed-schedules.json");
            File.WriteAllText(mixedPath, JsonSerializer.Serialize(new LocalScheduleFile { Schedules =
                [validSchedule, validSchedule with { Id = Guid.NewGuid(), Invocation = new ScheduledSkillInvocation { SkillId = "bad.exe" } }] },
                SettingsService.JsonOptions));
            AppServices mixed = Create(new MutablePlanAppCatalog(), Path.Combine(directory, "mixed-skills"), mixedPath);
            Require(mixed.Scheduler.Schedules.Count == 1 && mixed.Scheduler.GetDue(now).Count == 1 &&
                mixed.StartupIssues.Any(issue => issue.Module == AppStartupModule.Scheduler),
                "Invalid schedule entry blocked valid due schedule.");
            Require(!mixed.Assistant.HasPendingAction && !mixed.Assistant.HasPendingPlan,
                "Degraded startup restored execution authorization.");
        }
        finally
        {
            foreach (AppServices services in servicesToClose)
            {
                services.Assistant.BeginShutdown();
                services.VoiceCapture.Dispose();
                services.TextToSpeech.Dispose();
            }
            Directory.Delete(directory, recursive: true);
        }
    }
}
