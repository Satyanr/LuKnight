using System.IO;
using System.Text.RegularExpressions;

internal static partial class Program
{
    private static void
        CheckNativeFixtureIsolation()
    {
        string root =
            AppContext.BaseDirectory;


        while (!File.Exists(
                   Path.Combine(
                       root,
                       "LuKnight.csproj")))
        {
            root =
                Directory
                    .GetParent(
                        root)?
                    .FullName
                ?? throw new
                    InvalidOperationException(
                        "Repository not found.");
        }


        string runner =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "tools",
                    "Run-Regression.ps1"));


        string actions =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "tests",
                    "LuKnight.RenderChecks",
                    "UiActionLiveChecks.cs"));


        string scheduler =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "tests",
                    "LuKnight.RenderChecks",
                    "SchedulerLiveChecks.cs"));


        string companion =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "tests",
                    "LuKnight.RenderChecks",
                    "CompanionLiveChecks.cs"));


        Match nativeBlock =
            Regex.Match(
                runner,
                @"\$nativeFixtureCases\s*=\s*@\((.*?)\)",
                RegexOptions.Singleline);


        Require(
            nativeBlock.Success,
            "Native fixture tier is missing.");


        string[] nativeFlags =
            Regex.Matches(
                    nativeBlock.Groups[1]
                        .Value,
                    @"'(?<flag>--[a-z0-9-]+)'",
                    RegexOptions.IgnoreCase)
                .Select(
                    match =>
                        match.Groups[
                                "flag"]
                            .Value)
                .ToArray();


        string[] expected =
        [
            "--uia-action-live",
            "--mouse-action-live",
            "--mouse-fallback-live",
            "--uia-text-live",
            "--keyboard-fallback-live",
            "--screen-assist-live",

            "--permissions-live",
            "--planner-live",
            "--user-skill-live",
            "--workflow-live",

            "--scheduler-live",
            "--companion-live"
        ];


        Require(
            nativeFlags
                .ToHashSet(
                    StringComparer
                        .OrdinalIgnoreCase)
                .SetEquals(
                    expected),
            "Native fixture regression tier changed without isolation review.");


        //
        // Ambient / arbitrary desktop operations must never
        // silently migrate into fixture tier.
        //

        foreach (string forbidden
                 in new[]
                 {
                     "--window-targeting-live",
                     "--uia-live",
                     "--desktop-commands-live",
                     "--assistant-live"
                 })
        {
            Require(
                !nativeFlags.Contains(
                    forbidden,
                    StringComparer
                        .OrdinalIgnoreCase),
                $"{forbidden} escaped into native fixture tier.");
        }


        //
        // All native desktop mutation tests in UiActionLiveChecks
        // must use the dedicated fixture process.
        //

        Require(
            actions.Contains(
                "StartUiFixtureProcess(",
                StringComparison.Ordinal) &&
            actions.Contains(
                "--uia-action-fixture-host",
                StringComparison.Ordinal) &&
            actions.Contains(
                "--uia-fixture-token=",
                StringComparison.Ordinal),
            "Native UI action tests no longer use dedicated fixture host.");


        //
        // Central target lookup must bind BOTH exact process
        // identity and exact tokenized title.
        //

        int waitMethod =
            actions.IndexOf(
                "private static async Task<DesktopWindowTarget> WaitForFixtureWindowAsync(",
                StringComparison.Ordinal);


        Require(
            waitMethod >= 0,
            "Native fixture target resolver is missing.");


        string waitSource =
            actions[
                waitMethod..];

        Require(waitSource.Contains("ValidateFixtureTarget(target, fixture, expectedTitle);", StringComparison.Ordinal),
            "Native fixture lookup bypasses centralized ownership validation.");


        Require(
            waitSource.Contains(
                "x.ProcessId == fixture.Id",
                StringComparison.Ordinal) &&
            waitSource.Contains(
                "string.Equals(x.Title, expectedTitle, StringComparison.Ordinal)",
                StringComparison.Ordinal),
            "Fixture window resolution is not bound to PID + exact title.");


        Require(
            actions.Contains(
                "UseShellExecute = false",
                StringComparison.Ordinal),
            "Fixture process unexpectedly uses shell execution.");


        //
        // Scheduler native acceptance must use the same
        // isolated fixture host.
        //

        Require(
            scheduler.Contains(
                "StartUiFixtureProcess(",
                StringComparison.Ordinal) &&
            scheduler.Contains(
                "WaitForFixtureWindowAsync(",
                StringComparison.Ordinal),
            "Native scheduler acceptance no longer targets dedicated fixture.");


        //
        // Companion live is allowed in native-fixture tier because
        // it exercises WPF/tray integration, but it must not inspect
        // or mutate ambient desktop windows.
        //

        foreach (string forbidden
                 in new[]
                 {
                     "DesktopWindowTargetService",
                     "WindowsDesktopUiAutomationReader",
                     "WindowsDesktopUiActionExecutor",
                     "WindowsDesktopMouseActionExecutor",
                     "WindowsDesktopKeyboardTextActionExecutor",
                     "AutomationElement.FromHandle"
                 })
        {
            Require(
                !companion.Contains(
                    forbidden,
                    StringComparison.Ordinal),
                $"Companion live acceptance gained ambient/native desktop authority through {forbidden}.");
        }
    }
}
