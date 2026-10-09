using System.IO;
using System.Reflection;
using Microsoft.Win32;
using LuKnight.Models;

namespace LuKnight.Services;


public static class InstallerE2EProbe
{
    private const string
        Prefix =
            "--installer-e2e-probe=";


    public static bool TryRun(
        IEnumerable<string> arguments,
        out int exitCode)
    {
        string? argument =
            arguments.FirstOrDefault(
                value =>
                    value.StartsWith(
                        Prefix,
                        StringComparison
                            .OrdinalIgnoreCase));


        if (argument is null)
        {
            exitCode =
                0;

            return false;
        }


        RuntimeProfileDefinition profile =
            RuntimeProfile.Current;


        if (!profile.IsE2E ||
            string.IsNullOrWhiteSpace(
                profile.Token))
        {
            exitCode =
                90;

            return true;
        }


        string mode =
            argument[Prefix.Length..]
                .Trim()
                .ToLowerInvariant();


        try
        {
            exitCode =
                mode switch
                {
                    "seed" =>
                        Seed(profile),

                    "verify-installed" =>
                        VerifyInstalled(profile),

                    "verify-preserved" =>
                        VerifyPreserved(profile),

                    "verify-clean" =>
                        VerifyClean(profile),

                    "hold" =>
                        Hold(),

                    "cleanup" =>
                        Cleanup(profile),

                    _ =>
                        91
                };
        }
        catch
        {
            //
            // E2E probe output must not leak
            // paths, credentials or exception text.
            //

            exitCode =
                99;
        }


        return true;
    }


    private static string
        CredentialValue(
            RuntimeProfileDefinition
                profile) =>
            $"luknight-e2e-{profile.Token}";


    private static string
        SettingsPath(
            RuntimeProfileDefinition
                profile) =>
            Path.Combine(
                profile.UserDirectory,
                "settings.json");


    private static string
        SentinelPath(
            RuntimeProfileDefinition
                profile) =>
            Path.Combine(
                profile.UserDirectory,
                "installer-e2e-sentinel.txt");


    private static int Seed(
        RuntimeProfileDefinition
            profile)
    {
        string settingsPath =
            SettingsPath(
                profile);


        var settings =
            new SettingsService(
                settingsPath);


        settings.Load();


        AppSettings seeded =
            settings.Current with
            {
                General =
                    settings.Current.General with
                    {
                        StartHidden =
                            true
                    },

                Onboarding =
                    settings.Current.Onboarding with
                    {
                        Completed =
                            true
                    }
            };


        if (!settings.Update(
                seeded))
        {
            return 92;
        }


        var credential =
            new SecureCredentialService();


        credential.Write(
            CredentialValue(
                profile));


        File.WriteAllText(
            SentinelPath(
                profile),
            "LuKnight installer E2E");


        var startup =
            new RegistryStartupStore();


        startup.WriteStartHidden(
            true);


        string executable =
            Environment.ProcessPath
            ?? throw new
                InvalidOperationException();


        startup.WriteCommand(
            StartupService.BuildCommand(
                executable,
                Assembly.GetExecutingAssembly()
                    .Location));


        return 0;
    }


    private static bool
        HasPreservedData(
            RuntimeProfileDefinition profile)
    {
        var settings =
            new SettingsService(
                SettingsPath(
                    profile));


        settings.Load();


        var credential =
            new SecureCredentialService();


        return
            settings.Current
                .General
                .StartHidden &&
            settings.Current
                .Onboarding
                .Completed &&
            File.Exists(
                SentinelPath(
                    profile)) &&
            string.Equals(
                credential.Read(),
                CredentialValue(
                    profile),
                StringComparison.Ordinal);
    }

    private static int
        VerifyInstalled(
            RuntimeProfileDefinition profile)
    {
        if (!HasPreservedData(
                profile))
        {
            return 93;
        }


        var startup =
            new RegistryStartupStore();


        string executable =
            Environment.ProcessPath
            ?? throw new
                InvalidOperationException();


        string expected =
            StartupService.BuildCommand(
                executable,
                Assembly.GetExecutingAssembly()
                    .Location);


        return
            startup.ReadStartHidden() &&
            string.Equals(
                startup.ReadCommand(),
                expected,
                StringComparison.Ordinal)
                ? 0
                : 95;
    }

    private static int
        VerifyPreserved(
            RuntimeProfileDefinition profile)
    {
        if (!HasPreservedData(
                profile))
        {
            return 93;
        }


        var startup =
            new RegistryStartupStore();


        //
        // Uninstall removes the Run entry,
        // but keep-data uninstall retains
        // user preference data.
        //

        return
            startup.ReadCommand() is null &&
            startup.ReadStartHidden()
                ? 0
                : 96;
    }

    private static int
        VerifyClean(
            RuntimeProfileDefinition profile)
    {
        var credential =
            new SecureCredentialService();


        var startup =
            new RegistryStartupStore();


        bool clean =
            !Directory.Exists(
                profile.UserDirectory) &&
            credential.Read() is null &&
            startup.ReadCommand() is null &&
            !startup.ReadStartHidden();


        return clean
            ? 0
            : 94;
    }

    private static int Hold()
    {
        Thread.Sleep(
            TimeSpan.FromSeconds(
                5));


        return 0;
    }

    private static int
        Cleanup(
            RuntimeProfileDefinition profile)
    {
        if (!profile.IsE2E ||
            string.IsNullOrWhiteSpace(
                profile.Token))
        {
            return 97;
        }


        var credential =
            new SecureCredentialService();


        credential.Remove();


        var startup =
            new RegistryStartupStore();


        startup.DeleteCommand();


        Registry.CurrentUser
            .DeleteSubKeyTree(
                profile.StartupPreferenceKey,
                throwOnMissingSubKey:
                    false);


        if (Directory.Exists(
                profile.UserDirectory))
        {
            Directory.Delete(
                profile.UserDirectory,
                recursive:
                    true);
        }


        return VerifyClean(
            profile);
    }
}
