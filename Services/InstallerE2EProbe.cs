using System.IO;
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
                        Seed(
                            profile),

                    "verify-preserved" =>
                        VerifyPreserved(
                            profile),

                    "verify-clean" =>
                        VerifyClean(
                            profile),

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


        return 0;
    }


    private static int VerifyPreserved(
        RuntimeProfileDefinition
            profile)
    {
        var settings =
            new SettingsService(
                SettingsPath(
                    profile));


        settings.Load();


        var credential =
            new SecureCredentialService();


        bool valid =
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


        return valid
            ? 0
            : 93;
    }


    private static int VerifyClean(
        RuntimeProfileDefinition
            profile)
    {
        var credential =
            new SecureCredentialService();


        bool clean =
            !File.Exists(
                SettingsPath(
                    profile)) &&
            !File.Exists(
                SentinelPath(
                    profile)) &&
            credential.Read() is null;


        return clean
            ? 0
            : 94;
    }
}
