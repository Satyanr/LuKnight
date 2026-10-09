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
        Require(
            verify.Contains(
                "Release provenance application identity mismatch.",
                StringComparison.Ordinal) &&
            verify.Contains(
                "Published application file version mismatch.",
                StringComparison.Ordinal) &&
            verify.Contains(
                "PublishDirectory",
                StringComparison.Ordinal),
            "Release verifier does not verify the published application binary.");

        Require(
            workflow.Contains(
                "-PublishDirectory",
                StringComparison.Ordinal),
            "GitHub release workflow does not verify published application provenance.");

        Require(
            build.Contains(
                "fileVersion",
                StringComparison.Ordinal) &&
            build.Contains(
                "Published application file version mismatch.",
                StringComparison.Ordinal),
            "Release packaging does not bind application version metadata.");

        Require(
            verify.Contains(
                "Release requires a valid Authenticode signature for LuKnight.exe.",
                StringComparison.Ordinal) &&
            verify.Contains(
                "Release requires a valid Authenticode signature for LuKnightSetup.exe.",
                StringComparison.Ordinal) &&
            verify.Contains(
                "Release requires an Authenticode timestamp for LuKnight.exe.",
                StringComparison.Ordinal) &&
            verify.Contains(
                "Release requires an Authenticode timestamp for LuKnightSetup.exe.",
                StringComparison.Ordinal) &&
            verify.Contains(
                "TimeStamperCertificate",
                StringComparison.Ordinal),
            "Signed release gate does not require application, installer and timestamp verification.");

        Require(
            build.Contains(
                "appSignatureStatus",
                StringComparison.Ordinal) &&
            build.Contains(
                "installerSignatureStatus",
                StringComparison.Ordinal) &&
            build.Contains(
                "timestamped",
                StringComparison.Ordinal),
            "Release provenance does not capture signed application and installer state.");

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

        start.ArgumentList[start.ArgumentList.Count - 1] =
            Path.Combine(root, "tools", "Test-ReleaseApplicationProvenance.ps1");
        using Process applicationChecks = Process.Start(start) ??
            throw new InvalidOperationException("Unable to start application provenance fixtures.");
        applicationChecks.WaitForExit();
        Require(applicationChecks.ExitCode == 0, "Application provenance behavior checks failed.");
    }
}
