using System.IO;
using LuKnight.Assistant;
using LuKnight.Services;

internal static partial class Program
{
    private static void
        CheckSchedulerFoundation()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                "LuKnight-SchedulerTests",
                Guid.NewGuid()
                    .ToString("N"));

        Directory.CreateDirectory(
            directory);

        try
        {
            string path =
                Path.Combine(
                    directory,
                    "schedules.json");

            var store =
                new LocalScheduleStore(
                    path);

            var scheduler =
                new LocalSchedulerService(
                    store);


            DateTimeOffset now =
                new(
                    2026,
                    10,
                    5,
                    10,
                    0,
                    0,
                    TimeSpan.Zero);


            var future =
                new ScheduledSkill
                {
                    Id =
                        Guid.NewGuid(),

                    DisplayName =
                        "Future",

                    CreatedAtUtc =
                        now,

                    DueAtUtc =
                        now.AddMinutes(
                            10),

                    Invocation =
                        new ScheduledSkillInvocation
                        {
                            SkillId =
                                "search-downloads",

                            Argument =
                                "invoice"
                        }
                };


            var due =
                new ScheduledSkill
                {
                    Id =
                        Guid.NewGuid(),

                    DisplayName =
                        "Due",

                    CreatedAtUtc =
                        now.AddMinutes(
                            -10),

                    DueAtUtc =
                        now.AddMinutes(
                            -1),

                    Invocation =
                        new ScheduledSkillInvocation
                        {
                            SkillId =
                                "project-search",

                            Parameters =
                                new Dictionary<string, string>
                                {
                                    ["project"] =
                                        "LuKnight",

                                    ["query"] =
                                        "invoice"
                                }
                        }
                };


            Require(
                scheduler.Upsert(
                    future,
                    out _),
                "Future schedule could not be saved.");

            Require(
                scheduler.Upsert(
                    due,
                    out _),
                "Due schedule could not be saved.");


            scheduler.Load();


            Require(
                scheduler.Schedules.Count ==
                    2,
                "Scheduler persistence round-trip failed.");


            IReadOnlyList<ScheduledSkill>
                dueNow =
                    scheduler.GetDue(
                        now);


            Require(
                dueNow.Count == 1 &&
                dueNow[0].Id ==
                    due.Id,
                "Due calculation was incorrect.");


            Require(
                scheduler.GetDue(
                    now.AddMinutes(
                        11))
                    .Count ==
                    2,
                "Future schedule did not become due.");


            var reloaded =
                new LocalSchedulerService(
                    new LocalScheduleStore(
                        path));

            reloaded.Load();


            Require(
                reloaded.Schedules.Count ==
                    2,
                "Scheduler did not survive service recreation.");


            Require(
                reloaded.Schedules
                    .Single(
                        x =>
                            x.Id ==
                            due.Id)
                    .Invocation
                    .Parameters?["project"] ==
                    "LuKnight",
                "Scheduled named parameters were not preserved.");


            Require(
                reloaded.Remove(
                    due.Id,
                    out _),
                "Schedule removal failed.");


            Require(
                reloaded.Schedules.Count ==
                    1,
                "Removed schedule remained in memory.");


            var finalReload =
                new LocalSchedulerService(
                    new LocalScheduleStore(
                        path));

            finalReload.Load();


            Require(
                finalReload.Schedules.Count ==
                    1 &&
                finalReload.Schedules[0].Id ==
                    future.Id,
                "Removed schedule remained on disk.");

            Require(
                !scheduler.Upsert(
                    future with
                    {
                        Id =
                            Guid.NewGuid(),

                        Invocation =
                            new ScheduledSkillInvocation
                            {
                                SkillId =
                                    "test",

                                Argument =
                                    "legacy",

                                Parameters =
                                    new Dictionary<string, string>
                                    {
                                        ["query"] =
                                            "named"
                                    }
                            }
                    },
                    out _),
                "Schedule accepted legacy and named parameters together.");

            Require(
                !scheduler.Upsert(
                    future with
                    {
                        Id =
                            Guid.NewGuid(),

                        Invocation =
                            new ScheduledSkillInvocation
                            {
                                SkillId =
                                    "powershell.exe"
                            }
                    },
                    out _),
                "Schedule accepted invalid skill identifier.");
            CheckSchedulerEdgeCases(path, future, now);
        }
        finally
        {
            Directory.Delete(
                directory,
                recursive:
                    true);
        }
    }

    private static void CheckSchedulerEdgeCases(string path, ScheduledSkill template, DateTimeOffset now)
    {
        var store = new LocalScheduleStore(path);
        var scheduler = new LocalSchedulerService(store);
        scheduler.Load();
        Guid firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var values = new Dictionary<string, string> { [" query "] = " invoice " };
        var boundary = template with
        {
            Id = secondId, DisplayName = " Boundary ",
            DueAtUtc = now.ToOffset(TimeSpan.FromHours(8)),
            CreatedAtUtc = now.ToOffset(TimeSpan.FromHours(8)),
            LastPresentedAtUtc = now.ToOffset(TimeSpan.FromHours(8)),
            Invocation = new() { SkillId = " project-search ", Parameters = values }
        };
        Require(scheduler.Upsert(boundary, out _), "Exact due boundary could not be saved.");
        values[" query "] = "mutated";
        ScheduledSkill saved = scheduler.Schedules.Single(x => x.Id == secondId);
        Require(saved.DisplayName == "Boundary" && saved.Invocation.SkillId == "project-search" &&
            saved.Invocation.Parameters?["QUERY"] == "invoice", "Schedule normalization or defensive parameter copy failed.");
        Require(saved.DueAtUtc.Offset == TimeSpan.Zero && saved.CreatedAtUtc.Offset == TimeSpan.Zero &&
            saved.LastPresentedAtUtc?.Offset == TimeSpan.Zero, "Schedule timestamps were not normalized to UTC.");
        Require(scheduler.Upsert(boundary with { Id = firstId }, out _), "Equal-time schedule failed.");
        Require(scheduler.GetDue(now.ToOffset(TimeSpan.FromHours(-5))).Select(x => x.Id)
            .SequenceEqual(new[] { firstId, secondId }), "Due boundary, UTC equivalence or deterministic ID ordering failed.");
        Require(scheduler.GetDue(now).Select(x => x.Id).SequenceEqual(scheduler.GetDue(now).Select(x => x.Id)),
            "GetDue unexpectedly consumed due data.");
        Require(scheduler.Upsert(saved with { Enabled = false }, out _), "Disabling a schedule failed.");
        Require(scheduler.GetDue(now).All(x => x.Id != secondId), "Disabled schedule became due.");
        Require(scheduler.Schedules.Count == 3, "Upsert created a duplicate instead of updating.");
        string before = File.ReadAllText(path);
        var invalid = new[]
        {
            template with { Id = Guid.Empty },
            template with { DisplayName = "bad\0name" },
            template with { DisplayName = new string('x', LocalSchedulePolicy.MaxDisplayNameLength + 1) },
            template with { Invocation = new() { SkillId = "test", Argument = "bad\nargument" } },
            template with { Invocation = new() { SkillId = "test", Argument = new string('x', LocalSchedulePolicy.MaxArgumentLength + 1) } },
            template with { Invocation = new() { SkillId = "test", Parameters = new Dictionary<string, string> { ["argument"] = "reserved" } } },
            template with { Invocation = new() { SkillId = "test", Parameters = new Dictionary<string, string> { ["query"] = "one", [" query "] = "two" } } },
            template with { Invocation = new() { SkillId = "test", Parameters = new Dictionary<string, string> { ["query"] = new string('x', LocalSchedulePolicy.MaxParameterValueLength + 1) } } },
            template with { Invocation = new() { SkillId = "test", Parameters = Enumerable.Range(0, LocalSchedulePolicy.MaxParameters + 1).ToDictionary(i => "p" + i, _ => "x") } },
            template with { Invocation = new() { SkillId = "test", Parameters = Enumerable.Range(0, 3).ToDictionary(i => "p" + i, _ => new string('x', 500)) } }
        };
        foreach (ScheduledSkill item in invalid)
            Require(!scheduler.Upsert(item, out string error) && error.Length > 0, "Invalid schedule was accepted.");
        Require(!scheduler.Remove(Guid.NewGuid(), out _), "Missing schedule removal succeeded.");
        Require(!store.Save(new[] { template, template }, out _), "Duplicate schedule IDs were saved.");
        Require(!store.Save(Enumerable.Range(0, LocalSchedulePolicy.MaxSchedules + 1)
            .Select(_ => template with { Id = Guid.NewGuid() }).ToArray(), out _), "Excess schedules were saved.");
        Require(File.ReadAllText(path) == before && scheduler.Schedules.Count == 3,
            "Rejected mutations altered memory or persisted data.");
        Require(File.Exists(path + ".bak") && !File.Exists(path + ".tmp"), "Atomic replacement did not keep backup or remove temporary file.");

        string original = File.ReadAllText(path);
        foreach (string document in new[] { "{", "{\"schemaVersion\":2}", "{\"schedules\":null}", "" })
        {
            File.WriteAllText(path, document);
            LocalScheduleLoadResult result = store.Load();
            Require(result.Schedules.Count == 0 && result.Issues.Count > 0, "Invalid schedule file was accepted.");
        }
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new LocalScheduleFile
        {
            Schedules = [null!, template with { Id = Guid.Empty }, template, template]
        }, SettingsService.JsonOptions));
        LocalScheduleLoadResult partial = store.Load();
        Require(partial.Schedules.Count == 1 && partial.Issues.Count == 3,
            "Load did not isolate null, invalid and duplicate entries from valid schedules.");
        File.WriteAllText(path, new string('x', 256 * 1024 + 1));
        Require(store.Load().Issues.Count > 0, "Oversized schedule file was accepted.");
        File.WriteAllText(path, original);

        // A locked destination must preserve both the previous file and in-memory snapshot.
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Require(!scheduler.Upsert(template with { DisplayName = "Must not commit" }, out _),
                "Saving over a locked schedule destination succeeded.");
            Require(!scheduler.Remove(firstId, out _), "Removing from a locked store succeeded.");
            Require(scheduler.Schedules.Count == 3 && scheduler.Schedules.All(x => x.DisplayName != "Must not commit"),
                "Failed persistence changed scheduler memory.");
        }
        Require(File.ReadAllText(path) == original && !File.Exists(path + ".tmp"),
            "Failed persistence damaged the previous file or left temporary data.");
        var missing = new LocalScheduleStore(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "missing.json"));
        Require(missing.Load() is { Schedules.Count: 0, Issues.Count: 0 }, "Missing schedule store was not empty.");
    }
}
