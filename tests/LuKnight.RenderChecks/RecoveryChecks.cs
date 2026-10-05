using System.IO;
using System.Text.Json;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private sealed record RecoveryAdapter(Action Load, Func<string> State, Func<bool> Save);

    private static async Task CheckRecoveryAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "LuKnight-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string Settings(bool onTop) => JsonSerializer.Serialize(new AppSettings
                { General = new GeneralSettings(AlwaysOnTop: onTop) }, SettingsService.JsonOptions);
            string Memory(string text) => JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                items = new[] { new MemoryEntry(Guid.NewGuid(), text, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) }
            }, SettingsService.JsonOptions);
            var schedule = new ScheduledSkill
            {
                Id = Guid.NewGuid(), DisplayName = "primary", Enabled = true,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-2), DueAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                Invocation = new ScheduledSkillInvocation { SkillId = "search-downloads", Argument = "invoice" }
            };
            string Schedule(string name) => JsonSerializer.Serialize(new LocalScheduleFile
                { Schedules = new[] { schedule with { DisplayName = name } } }, SettingsService.JsonOptions);

            RecoveryAdapter SettingsAdapter(string path)
            {
                var service = new SettingsService(path);
                return new(service.Load, () => service.Current.General.AlwaysOnTop ? "primary" : "backup", service.Save);
            }
            RecoveryAdapter MemoryAdapter(string path)
            {
                var service = new MemoryService(path);
                return new(service.Load, () => service.Items.FirstOrDefault()?.Text ?? "empty", () =>
                {
                    service.Remember("post-recovery-write");
                    return service.Status == "Long-term memory tersimpan.";
                });
            }
            RecoveryAdapter ScheduleAdapter(string path)
            {
                var store = new LocalScheduleStore(path);
                LocalScheduleLoadResult loaded = new([], []);
                return new(() => loaded = store.Load(), () => loaded.Schedules.FirstOrDefault()?.DisplayName ?? "empty",
                    () => store.Save(loaded.Schedules.ToArray(), out _));
            }

            foreach (var fixture in new (string Name, string Primary, string Backup, string Future, Func<string, RecoveryAdapter> Create)[]
            {
                ("settings", Settings(true), Settings(false), "{\"SchemaVersion\":999,\"general\":\"future-shape\"}", SettingsAdapter),
                ("memory", Memory("primary"), Memory("backup"), "{\"SchemaVersion\":999,\"items\":\"future-shape\"}", MemoryAdapter),
                ("schedules", Schedule("primary"), Schedule("backup"), "{\"SchemaVersion\":999,\"schedules\":\"future-shape\"}", ScheduleAdapter)
            })
            {
                string PathFor(string scenario) => Path.Combine(directory, fixture.Name + "-" + scenario + ".json");

                // The primary generation wins over backup and all uncommitted data.
                string path = PathFor("valid");
                File.WriteAllText(path, fixture.Primary);
                File.WriteAllText(path + ".bak", fixture.Backup);
                File.WriteAllText(path + ".tmp", fixture.Backup);
                File.WriteAllText(path + ".recover.tmp", fixture.Backup);
                RecoveryAdapter service = fixture.Create(path);
                service.Load();
                Require(service.State() == "primary", fixture.Name + " did not prefer valid primary.");
                Require(!File.Exists(path + ".tmp") && !File.Exists(path + ".recover.tmp"), "Stale temp files survived load.");
                Require(File.ReadAllText(path + ".bak") == fixture.Backup, "Loading primary altered backup.");

                // Corrupt primary is preserved byte-for-byte before validated backup heals it.
                path = PathFor("corrupt");
                File.WriteAllText(path, "{broken-primary");
                File.WriteAllText(path + ".bak", fixture.Backup);
                File.WriteAllText(path + ".tmp", fixture.Primary);
                service = fixture.Create(path);
                service.Load();
                Require(service.State() == "backup", fixture.Name + " did not recover backup.");
                Require(File.ReadAllText(path) == fixture.Backup && File.ReadAllText(path + ".bak") == fixture.Backup,
                    "Recovery failed to heal primary or modified committed backup.");
                string[] invalid = Directory.GetFiles(directory, System.IO.Path.GetFileName(path) + ".invalid-*.bak");
                Require(invalid.Length == 1 && File.ReadAllText(invalid[0]) == "{broken-primary", "Invalid primary was not preserved.");
                Require(!File.Exists(path + ".tmp") && service.Save(), "Recovered store remained read-only or kept stale temp.");

                path = PathFor("missing");
                File.WriteAllText(path + ".bak", fixture.Backup);
                service = fixture.Create(path);
                service.Load();
                Require(service.State() == "backup" && File.ReadAllText(path) == fixture.Backup, "Missing primary did not recover committed backup.");

                path = PathFor("tmp-only");
                File.WriteAllText(path + ".tmp", fixture.Backup);
                File.WriteAllText(path + ".recover.tmp", fixture.Backup);
                service = fixture.Create(path);
                service.Load();
                Require(!File.Exists(path) && !File.Exists(path + ".tmp") && !File.Exists(path + ".recover.tmp"),
                    "Uncommitted-only state was promoted after restart.");
                Require(service.State() == (fixture.Name == "settings" ? "primary" : "empty"), "Uncommitted-only state entered memory.");

                path = PathFor("future");
                File.WriteAllText(path, fixture.Future);
                File.WriteAllText(path + ".bak", fixture.Backup);
                service = fixture.Create(path);
                service.Load();
                Require(service.State() != "backup" && !service.Save(), "Future primary was downgraded to older backup.");
                Require(File.ReadAllText(path) == fixture.Future && File.ReadAllText(path + ".bak") == fixture.Backup,
                    "Future primary or committed backup was overwritten.");

                path = PathFor("future-backup");
                File.WriteAllText(path, "{broken-primary");
                File.WriteAllText(path + ".bak", fixture.Future);
                service = fixture.Create(path);
                service.Load();
                Require(!service.Save() && File.ReadAllText(path) == "{broken-primary" && File.ReadAllText(path + ".bak") == fixture.Future,
                    "Future backup was lost or restored as an older schema.");

                // Recovery can use the backup in memory, but failed healing must block writes.
                path = PathFor("locked");
                File.WriteAllText(path, "{broken-primary");
                File.WriteAllText(path + ".bak", fixture.Backup);
                service = fixture.Create(path);
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    service.Load();
                    Require(service.State() == "backup" && !service.Save(), "Failed primary healing did not enter read-only recovery.");
                    Require(File.ReadAllText(path) == "{broken-primary" && File.ReadAllText(path + ".bak") == fixture.Backup,
                        "Failed healing damaged primary or backup.");
                }
                service.Load();
                Require(service.State() == "backup" && service.Save(), "Unlocking and reloading did not restore writability.");

                path = PathFor("no-valid-backup");
                File.WriteAllText(path, "{broken-primary");
                File.WriteAllText(path + ".bak", "{broken-backup");
                service = fixture.Create(path);
                service.Load();
                Require(service.State() == (fixture.Name == "settings" ? "primary" : "empty"), "Invalid backup was trusted.");
                invalid = Directory.GetFiles(directory, System.IO.Path.GetFileName(path) + ".invalid-*.bak");
                Require(invalid.Length == 1 && File.ReadAllText(invalid[0]) == "{broken-primary", "Primary without valid backup was not preserved.");

                // A failed replace cleans only temporary files, retaining both committed generations.
                path = PathFor("failed-save");
                File.WriteAllText(path, fixture.Primary);
                File.WriteAllText(path + ".bak", fixture.Backup);
                service = fixture.Create(path);
                service.Load();
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Require(!service.Save(), "Write to locked primary succeeded.");
                Require(!File.Exists(path + ".tmp") && File.ReadAllText(path) == fixture.Primary &&
                    File.ReadAllText(path + ".bak") == fixture.Backup, "Failed save retained temp or damaged committed state.");
            }

            // Verify an actual previous memory generation, rather than a synthetic backup copy.
            string memoryPath = Path.Combine(directory, "memory-generations.json");
            var memory = new MemoryService(memoryPath);
            memory.Remember("recovery-memory-one");
            memory.Remember("recovery-memory-two");
            Require(File.Exists(memoryPath + ".bak"), "Memory backup was not created.");
            File.WriteAllText(memoryPath, "{broken");
            var recoveredMemory = new MemoryService(memoryPath);
            recoveredMemory.Load();
            Require(recoveredMemory.Items.Any(item => item.Text == "recovery-memory-one") &&
                !recoveredMemory.Items.Any(item => item.Text == "recovery-memory-two"), "Memory did not recover previous committed generation.");
            Require(recoveredMemory.Status.Contains("dipulihkan", StringComparison.OrdinalIgnoreCase), "Memory recovery status was not reported.");

            // Per-entry isolation is a valid primary, and must not roll back to a different backup.
            string schedulePath = Path.Combine(directory, "isolated-schedules.json");
            File.WriteAllText(schedulePath, JsonSerializer.Serialize(new LocalScheduleFile
                { Schedules = new[] { schedule, schedule with { Id = Guid.NewGuid(), Invocation = new ScheduledSkillInvocation { SkillId = "bad.exe" } } } },
                SettingsService.JsonOptions));
            File.WriteAllText(schedulePath + ".bak", Schedule("backup"));
            var isolatedStore = new LocalScheduleStore(schedulePath);
            LocalScheduleLoadResult isolated = isolatedStore.Load();
            Require(isolated.Schedules.Count == 1 && isolated.Schedules[0].DisplayName == "primary" &&
                isolated.Issues.Count == 1 && !isolatedStore.IsReadOnly, "Invalid schedule entry caused file-level backup fallback.");

            await CheckPendingAuthorizationRecoveryAsync();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task CheckPendingAuthorizationRecoveryAsync()
    {
        var action = new PlanTestAction(BuiltInActionNames.DesktopOpenApplication);
        var router = new AssistantActionRouter(new IAssistantAction[] { action });
        var apps = new MutablePlanAppCatalog();
        apps.Add(Installed("RecoveryApp", "RecoveryApp"));
        var intents = new AssistantIntentRouter(new LocalDesktopCommandRouter(apps, new FakeWindowCatalog()));
        AssistantController Create() => new(new ChatCoordinator(new FakeCredentials(),
            new ChatSettings { Provider = ChatProvider.Local, UseDesktopActions = true }), intentRouter: intents, actions: router);
        foreach (string command in new[] { "buka RecoveryApp", "buka RecoveryApp lalu buka RecoveryApp" })
        {
            AssistantController beforeCrash = Create();
            AssistantReply pending = await beforeCrash.SendAsync(new AssistantRequest(command));
            Require(pending.ActionProposal is not null && beforeCrash.HasPendingAction,
                "Crash-reset fixture did not create pending authorization.");
            Require(beforeCrash.HasPendingPlan == command.Contains("lalu", StringComparison.Ordinal), "Crash-reset workflow fixture failed.");
            AssistantController afterRestart = Create();
            Require(!afterRestart.HasPendingAction && !afterRestart.HasPendingPlan && !afterRestart.IsBusy,
                "Pending authorization/workflow survived controller recreation.");
            bool rejected = false;
            try { await afterRestart.ConfirmActionAsync(pending.ActionProposal!.Id); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected && action.Executions == 0, "Restart replayed pre-crash confirmation or native action.");
        }
    }
}
