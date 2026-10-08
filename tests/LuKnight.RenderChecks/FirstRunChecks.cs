using System.IO;
using System.Text.Json;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private static void CheckFirstRunExperience()
    {
        AppSettings fresh = new();
        Require(fresh.SchemaVersion == SettingsService.CurrentSchemaVersion && !fresh.Onboarding.Completed,
            "Fresh installation does not enter first-run state.");
        Require(FirstRunPolicy.ShouldShow(fresh), "Fresh first-run state was not detected.");
        ChatSettings chat = fresh.Chat;
        Require(!chat.UseApplicationContext && !chat.UseFileContext && !chat.UseClipboardContext &&
            !chat.UseSystemContext && !chat.UseScreenContext && !chat.UseVoiceInput &&
            !chat.UseDesktopActions && !fresh.Companion.Enabled,
            "Fresh installation enabled a sensitive capability.");

        AppSettings completed = FirstRunPolicy.Complete(fresh);
        Require(completed.Onboarding.Completed, "First-run completion was not recorded.");
        Require(completed == fresh with { Onboarding = new() { Completed = true } },
            "Completing first-run changed unrelated configuration or permissions.");
        Require(!FirstRunPolicy.ShouldShow(completed), "Completed first-run would reopen on next startup.");

        AppSettings optedIn = fresh with
        {
            General = new(StartHidden: true),
            Chat = fresh.Chat with { UseScreenContext = true, UseDesktopActions = true },
            Companion = fresh.Companion with { Enabled = true }
        };
        Require(FirstRunPolicy.ShouldShow(optedIn), "StartHidden suppressed first-run.");
        Require(FirstRunPolicy.Complete(optedIn) == optedIn with { Onboarding = new() { Completed = true } },
            "First-run completion overwrote existing preferences.");
    }

    private static void CheckFirstRunMigration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "LuKnight-first-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (int schema in new[] { 0, 1, 2 })
            {
                string file = Path.Combine(directory, $"schema-{schema}.json");
                string json = schema == 0
                    ? """{ "general": { "alwaysOnTop": false } }"""
                    : JsonSerializer.Serialize(new { schemaVersion = schema, general = new { alwaysOnTop = false } });
                File.WriteAllText(file, json);
                var settings = new SettingsService(file);
                settings.Load();
                Require(settings.Current.SchemaVersion == SettingsService.CurrentSchemaVersion,
                    $"Schema {schema} did not migrate to the current version.");
                Require(settings.Current.Onboarding.Completed && !settings.Current.General.AlwaysOnTop,
                    $"Existing schema {schema} re-entered first-run or lost preferences.");
            }

            string v3 = Path.Combine(directory, "schema-3.json");
            File.WriteAllText(v3, """{ "schemaVersion": 3, "onboarding": { "completed": false } }""");
            var freshPersisted = new SettingsService(v3);
            freshPersisted.Load();
            Require(!freshPersisted.Current.Onboarding.Completed,
                "Schema-3 unfinished onboarding was not preserved.");
            Require(freshPersisted.Update(FirstRunPolicy.Complete(freshPersisted.Current)),
                "First-run completion could not be saved.");
            var reloaded = new SettingsService(v3);
            reloaded.Load();
            Require(!FirstRunPolicy.ShouldShow(reloaded.Current),
                "Persisted first-run completion did not survive restart.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CheckFirstRun()
    {
        CheckFirstRunExperience();
        CheckFirstRunMigration();
    }
}
