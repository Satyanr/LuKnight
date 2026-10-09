using System.IO;
using System.Diagnostics;

internal static partial class Program
{
    private static void
        CheckReleaseBuildIntegrity()
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


        string build =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "tools",
                    "Build-Release.ps1"));


        string verify =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "tools",
                    "Test-ReleaseArtifacts.ps1"));


        string workflow =
            File.ReadAllText(
                Path.Combine(
                    root,
                    ".github",
                    "workflows",
                    "release.yml"));


        Require(
            build.Contains(
                "Assert-CleanWorkingTree",
                StringComparison.Ordinal) &&
            build.Contains(
                "ExpectedCommit",
                StringComparison.Ordinal) &&
            build.Contains(
                "sourceCommit",
                StringComparison.Ordinal),
            "Release build is not bound to clean committed source.");


        Require(
            build.Contains(
                "release-provenance.json",
                StringComparison.Ordinal) &&
            build.Contains(
                "authenticodeStatus",
                StringComparison.Ordinal),
            "Release build does not produce provenance evidence.");


        Require(
            verify.Contains(
                "update.json version mismatch",
                StringComparison.Ordinal) &&
            verify.Contains(
                "Installer SHA-256 does not match update.json",
                StringComparison.Ordinal) &&
            verify.Contains(
                "Release provenance commit mismatch",
                StringComparison.Ordinal),
            "Release artifact verifier does not cross-check identity.");


        Require(
            verify.Contains(
                "RequireSigned",
                StringComparison.Ordinal) &&
            verify.Contains(
                "Get-AuthenticodeSignature",
                StringComparison.Ordinal),
            "Release verifier has no future signing gate.");


        Require(
            workflow.Contains(
                "./tools/Run-Regression.ps1",
                StringComparison.Ordinal) &&
            workflow.Contains(
                "-ExpectedCommit",
                StringComparison.Ordinal) &&
            workflow.Contains(
                "./tools/Test-ReleaseArtifacts.ps1",
                StringComparison.Ordinal),
            "GitHub release workflow bypasses release integrity gates.");


        Require(
            workflow.Contains(
                "--draft",
                StringComparison.Ordinal) &&
            !workflow.Contains(
                "gh release edit",
                StringComparison.Ordinal),
            "Release workflow unexpectedly auto-publishes a release.");
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in new[]
                 { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                   Path.Combine(root, "tools", "Test-ReleaseBuildIntegrity.ps1") })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ??
            throw new InvalidOperationException("Unable to start release source-gate fixtures.");
        process.WaitForExit();
        Require(process.ExitCode == 0, "Release source-gate behavior checks failed.");
    }
}
