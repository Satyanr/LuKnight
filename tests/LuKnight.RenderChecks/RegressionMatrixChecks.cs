using System.IO;
using System.Text.RegularExpressions;

internal static partial class Program
{
    private static void
        CheckRegressionMatrix()
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


        string runnerPath =
            Path.Combine(
                root,
                "tools",
                "Run-Regression.ps1");


        Require(
            File.Exists(
                runnerPath),
            "Release regression runner is missing.");


        string script =
            File.ReadAllText(
                runnerPath);


        string program =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "tests",
                    "LuKnight.RenderChecks",
                    "Program.cs"));


        static string[]
            ReadGroup(
                string source,
                string name)
        {
            Match block =
                Regex.Match(
                    source,
                    @"\$" +
                    Regex.Escape(
                        name) +
                    @"\s*=\s*@\((.*?)\)",
                    RegexOptions.Singleline);


            Require(
                block.Success,
                $"Regression matrix group {name} is missing.");


            return Regex
                .Matches(
                    block.Groups[1]
                        .Value,
                    @"'(?<flag>--[a-z0-9-]+)'",
                    RegexOptions
                        .IgnoreCase)
                .Select(
                    match =>
                        match.Groups[
                                "flag"]
                            .Value)
                .ToArray();
        }


        string[] core =
            ReadGroup(
                script,
                "coreCases");

        string[] native =
            ReadGroup(
                script,
                "nativeFixtureCases");

        string[] ambient =
            ReadGroup(
                script,
                "ambientDesktopCases");

        string[] interactive =
            ReadGroup(
                script,
                "interactiveDesktopCases");

        string[] externalAi =
            ReadGroup(
                script,
                "externalAiCases");


        var groups =
            new[]
            {
                core,
                native,
                ambient,
                interactive,
                externalAi
            };


        string[] all =
            groups
                .SelectMany(
                    group =>
                        group)
                .ToArray();


        Require(
            all.Length ==
            all.Distinct(
                    StringComparer
                        .OrdinalIgnoreCase)
                .Count(),
            "A regression runner belongs to multiple execution tiers.");


        Require(
            core.Contains(
                "--regression-matrix") &&
            !core.Any(
                flag =>
                    flag.EndsWith(
                        "-live",
                        StringComparison
                            .OrdinalIgnoreCase)),
            "Core regression tier contains an explicit live runner.");


        Require(
            ambient.ToHashSet(
                StringComparer
                    .OrdinalIgnoreCase)
                .SetEquals(
                    new[]
                    {
                        "--window-targeting-live",
                        "--uia-live"
                    }),
            "Ambient desktop tier changed unexpectedly.");


        Require(
            interactive.Length ==
                1 &&
            interactive[0] ==
                "--desktop-commands-live",
            "Real desktop command smoke test lost its explicit tier.");


        Require(
            externalAi.Length ==
                1 &&
            externalAi[0] ==
                "--assistant-live",
            "External AI test lost its explicit tier.");


        foreach (string flag
                 in all)
        {
            Require(
                program.Contains(
                    $"\"{flag}\"",
                    StringComparison.Ordinal),
                $"Regression matrix references unknown runner {flag}.");
        }


        foreach (string forbidden
                 in new[]
                 {
                     "Tee-Object",
                     "Start-Transcript",
                     "RedirectStandardOutput",
                     "RedirectStandardError",
                     "Out-File"
                 })
        {
            Require(
                !script.Contains(
                    forbidden,
                    StringComparison
                        .OrdinalIgnoreCase),
                $"Regression runner can persist raw child output via {forbidden}.");
        }


        Require(
            script.Contains(
                "regression-summary.json",
                StringComparison.Ordinal) &&
            script.Contains(
                "regression-summary.md",
                StringComparison.Ordinal),
            "Regression runner does not produce sanitized release evidence.");
    }
}
