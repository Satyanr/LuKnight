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
            CheckSchedulerReminderPresentation(directory, future, due, now);
            CheckTrayReminderRouting();
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
    private static void CheckSchedulerReminderPresentation(string directory,
        ScheduledSkill future, ScheduledSkill due, DateTimeOffset now)
    {
        string path = Path.Combine(directory, "reminders.json");
        var scheduler = new LocalSchedulerService(new LocalScheduleStore(path));
        Require(scheduler.Upsert(future, out _) && scheduler.Upsert(due, out _),
            "Reminder presentation fixture could not be saved.");
        IReadOnlyList<ScheduledSkill>
            unpresented =
                scheduler.GetUnpresentedDue(
                    now);

        Require(
            unpresented.Count == 1 &&
            unpresented[0].Id ==
                due.Id,
            "Due schedule was not available for presentation.");

        Require(
            scheduler.MarkPresented(
                due.Id,
                now,
                out _),
            "Due schedule could not be marked presented.");

        Require(
            scheduler.GetUnpresentedDue(
                now)
                .All(
                    item =>
                        item.Id !=
                        due.Id),
            "Presented schedule was offered repeatedly.");

        Require(
            scheduler.GetDue(
                now)
                .Any(
                    item =>
                        item.Id ==
                            due.Id),
            "Presentation incorrectly consumed the schedule.");

        var presentationReload =
            new LocalSchedulerService(
                new LocalScheduleStore(
                    path));

        presentationReload.Load();


        ScheduledSkill presented =
            presentationReload
                .Schedules
                .Single(
                    item =>
                        item.Id ==
                        due.Id);


        Require(
            presented.LastPresentedAtUtc ==
                now.ToUniversalTime(),
            "Reminder presentation state did not survive restart.");


        Require(
            presentationReload
                .GetUnpresentedDue(
                    now)
                .All(
                    item =>
                        item.Id !=
                            due.Id),
            "Presented reminder returned after reload.");

        Require(
            !scheduler.MarkPresented(
                future.Id,
                now,
                out string futureError) &&
            futureError.Length > 0,
            "Future schedule was marked as presented.");

        ScheduledSkill disabled =
            future with
            {
                Id =
                    Guid.NewGuid(),

                DisplayName =
                    "Disabled reminder",

                Enabled =
                    false,

                DueAtUtc =
                    now.AddMinutes(
                        -1)
            };


        Require(
            scheduler.Upsert(
                disabled,
                out _),
            "Disabled reminder could not be stored.");


        Require(
            scheduler.GetUnpresentedDue(
                now)
                .All(
                    item =>
                        item.Id !=
                            disabled.Id),
            "Disabled schedule produced a reminder.");

        Require(
            !scheduler.MarkPresented(
                disabled.Id,
                now,
                out _),
            "Disabled schedule was marked presented.");

        DateTimeOffset originalPresentedAt =
            scheduler.Schedules
                .Single(
                    item =>
                        item.Id ==
                            due.Id)
                .LastPresentedAtUtc!
                .Value;


        Require(
            scheduler.MarkPresented(
                due.Id,
                now.AddMinutes(
                    5),
                out _),
            "Repeated presentation acknowledgement was not idempotent.");


        Require(
            scheduler.Schedules
                .Single(
                    item =>
                        item.Id ==
                            due.Id)
                .LastPresentedAtUtc ==
                originalPresentedAt,
            "Repeated MarkPresented changed the original timestamp.");
        Require(!scheduler.MarkPresented(Guid.NewGuid(), now, out string missingError) && missingError.Length > 0,
            "Missing schedule was marked presented.");
        ScheduledSkill pending = due with { Id = Guid.NewGuid(), LastPresentedAtUtc = null };
        Require(scheduler.Upsert(pending, out _), "Pending reminder fixture could not be saved.");
        string before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Require(!scheduler.MarkPresented(pending.Id, now, out string error) && error.Length > 0,
                "Presentation unexpectedly persisted through a locked destination.");
            Require(scheduler.Schedules.Single(x => x.Id == pending.Id).LastPresentedAtUtc is null &&
                scheduler.GetUnpresentedDue(now).Any(x => x.Id == pending.Id),
                "Failed presentation persistence changed memory or lost the pending reminder.");
            Require(scheduler.MarkPresented(due.Id, now.AddHours(1), out _),
                "Idempotent presentation acknowledgement attempted to save again.");
        }
        Require(File.ReadAllText(path) == before, "Failed presentation persistence changed the store.");
        Require(scheduler.MarkPresented(pending.Id, now.ToOffset(TimeSpan.FromHours(8)), out _) &&
            scheduler.Schedules.Single(x => x.Id == pending.Id).LastPresentedAtUtc?.Offset == TimeSpan.Zero,
            "Presentation timestamp was not normalized to UTC.");
    }

    private static void CheckTrayReminderRouting()
    {
        int chat = 0, settings = 0;
        using var tray = new TrayIconService(() => { }, () => { }, () => chat++, () => settings++, () => { }, () => { });
        var icon = Get<System.Windows.Forms.NotifyIcon>(tray, "_icon");
        var click = typeof(System.Windows.Forms.NotifyIcon).GetMethod("OnBalloonTipClicked", Private)!;
        click.Invoke(icon, Array.Empty<object>());
        Require(chat == 0 && settings == 0, "Balloon click without a notification opened a window.");
        Require(!tray.NotifyReminder("   "), "Empty reminder name was accepted.");
        tray.NotifyUpdate("test");
        click.Invoke(icon, Array.Empty<object>());
        Require(settings == 1 && chat == 0, "Update balloon did not open Settings only.");
        Require(tray.NotifyReminder("Due workflow"), "Reminder notification was rejected.");
        click.Invoke(icon, Array.Empty<object>());
        Require(settings == 1 && chat == 1, "Reminder balloon did not open Chat only.");
        tray.NotifyUpdate("test-again");
        click.Invoke(icon, Array.Empty<object>());
        Require(settings == 2 && chat == 1, "Update after reminder retained the Chat click action.");
        tray.Dispose();
        Require(!tray.NotifyReminder("Disposed"), "Disposed tray accepted reminder notification.");
        tray.NotifyUpdate("disposed");
        click.Invoke(icon, Array.Empty<object>());
        Require(settings == 2 && chat == 1, "Disposed tray dispatched balloon clicks.");
    }
}
