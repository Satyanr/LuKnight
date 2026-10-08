using System.IO;

internal static partial class Program
{
    private static void
        CheckRegressionSignoff()
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


        string path =
            Path.Combine(
                root,
                "tools",
                "New-RegressionSignoff.ps1");


        Require(
            File.Exists(path),
            "Regression sign-off generator is missing.");


        string source =
            File.ReadAllText(path);


        Require(
            source.Contains(
                "regression-summary.json",
                StringComparison.Ordinal) &&
            source.Contains(
                "rev-parse HEAD",
                StringComparison.Ordinal) &&
            source.Contains(
                "Regression evidence belongs to another commit",
                StringComparison.Ordinal),
            "Regression sign-off does not reject stale commit evidence.");


        Require(
            source.Contains(
                "$nativeFixtures -eq 'Pass'",
                StringComparison.Ordinal),
            "Phase 12E sign-off does not require native fixture evidence.");


        Require(
            source.Contains(
                "$Microphone = 'NotRun'",
                StringComparison.Ordinal) &&
            source.Contains(
                "$Speaker = 'NotRun'",
                StringComparison.Ordinal) &&
            source.Contains(
                "$MultiMonitor = 'NotRun'",
                StringComparison.Ordinal) &&
            source.Contains(
                "$TrayShell = 'NotRun'",
                StringComparison.Ordinal),
            "Physical regression defaults are not fail-closed.");


        Require(
            source.Contains(
                "NotRun and MissingEvidence are never equivalent to PASS.",
                StringComparison.Ordinal),
            "Regression sign-off can blur missing evidence and PASS.");


        Require(
            source.Contains(
                "installer =",
                StringComparison.Ordinal) &&
            source.Contains(
                "'Phase12G'",
                StringComparison.Ordinal),
            "Installer/updater evidence was incorrectly claimed during Phase 12E.");


        foreach (string forbidden
                 in new[]
                 {
                     "Tee-Object",
                     "Start-Transcript",
                     "Out-File",
                     "RedirectStandardOutput",
                     "RedirectStandardError"
                 })
        {
            Require(
                !source.Contains(
                    forbidden,
                    StringComparison.OrdinalIgnoreCase),
                $"Regression sign-off may persist raw output via {forbidden}.");
        }
    }
}
