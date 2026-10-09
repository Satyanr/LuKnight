using System.IO;
using LuKnight.Services;

internal static partial class Program
{
    private static void
        CheckInstallerE2EIsolation()
    {
        RuntimeProfileDefinition production =
            RuntimeProfile.Resolve(
                null);


        Require(
            !production.IsE2E &&
            production.UserDirectory.EndsWith(
                "LuKnight",
                StringComparison.OrdinalIgnoreCase) &&
            production.MutexName ==
                @"Local\LuKnight-App" &&
            production.StartupRunValueName ==
                "LuKnight" &&
            production.CredentialTarget ==
                "LuKnight/GeminiApiKey",
            "Production runtime identity changed.");


        const string token =
            "0123456789abcdef0123456789abcdef";


        RuntimeProfileDefinition e2e =
            RuntimeProfile.Resolve(
                token);


        Require(
            e2e.IsE2E &&
            e2e.Token ==
                token &&
            e2e.UserDirectory.Contains(
                "LuKnight-E2E",
                StringComparison.Ordinal) &&
            e2e.UserDirectory !=
                production.UserDirectory &&
            e2e.MutexName !=
                production.MutexName &&
            e2e.ShowEventName !=
                production.ShowEventName &&
            e2e.StartupRunValueName !=
                production.StartupRunValueName &&
            e2e.StartupPreferenceKey !=
                production.StartupPreferenceKey &&
            e2e.CredentialTarget !=
                production.CredentialTarget,
            "Installer E2E profile overlaps production identity.");


        try
        {
            RuntimeProfile.Resolve(
                "../production");

            throw new Exception(
                "Unsafe E2E profile token accepted.");
        }
        catch (InvalidOperationException)
        {
            Require(
                true,
                "Unsafe profile rejected.");
        }


        Require(production.ShowEventName == @"Local\LuKnight-Show" &&
            production.StartupPreferenceKey == @"Software\LuKnight" && production.Token is null,
            "Production event/preference identity changed.");
        Require(e2e.UserDirectory == Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LuKnight-E2E", token) &&
            e2e.MutexName == $@"Local\LuKnight-E2E-{token}" &&
            e2e.ShowEventName == $@"Local\LuKnight-E2E-Show-{token}" &&
            e2e.StartupRunValueName == $"LuKnight-E2E-{token}" &&
            e2e.StartupPreferenceKey == $@"Software\LuKnight-E2E\{token}" &&
            e2e.CredentialTarget == $"LuKnight-E2E/{token}/GeminiApiKey",
            "E2E profile does not use the exact token-scoped identities.");
        Require(RuntimeProfile.Resolve("  " ) == production &&
            RuntimeProfile.Resolve(" " + token + " ") == e2e,
            "Profile whitespace handling changed.");
        foreach (string invalid in new[] { "../production", "g" + token[1..],
                     token[..31], token + "0", token + "\ninside" })
        {
            bool rejected = false;
            try { RuntimeProfile.Resolve(invalid); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Unsafe or malformed E2E profile token accepted.");
        }
        Require(new SecureCredentialService().TargetName == RuntimeProfile.Current.CredentialTarget &&
            new SecureCredentialService("LuKnight-test/injected").TargetName == "LuKnight-test/injected",
            "Credential service ignores runtime identity or explicit injection.");
        var startup = new RegistryStartupStore();
        Require(Get<string>(startup, "_runValueName") == RuntimeProfile.Current.StartupRunValueName &&
            Get<string>(startup, "_preferenceKey") == RuntimeProfile.Current.StartupPreferenceKey,
            "Startup store does not use the runtime profile.");
        Require(SettingsService.UserDirectory == RuntimeProfile.Current.UserDirectory &&
            Get<string>(new LocalWhisperSpeechToTextService(), "_modelsDirectory") ==
                Path.Combine(RuntimeProfile.Current.UserDirectory, "Models"),
            "Settings or Whisper models escape the runtime data namespace.");
        Require(!InstallerE2EProbe.TryRun(Array.Empty<string>(), out int noProbe) && noProbe == 0,
            "Normal startup was captured by the installer probe.");
        Require(InstallerE2EProbe.TryRun(new[] { "--installer-e2e-probe=unsupported" }, out int unknown) &&
            unknown == (RuntimeProfile.Current.IsE2E ? 91 : 90),
            "Installer probe does not reject an unavailable profile or unknown mode.");

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


        string installer =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "installer",
                    "LuKnight.iss"));


        foreach (string define
                 in new[]
                 {
                     "AppIdValue",
                     "RunValueNameValue",
                     "PreferenceKeyValue",
                     "AppMutexValue",
                     "DataDirectoryValue",
                     "CredentialTargetValue",
                     "AllowSilentRemovePreferences"
                 })
        {
            Require(
                installer.Contains(
                    define,
                    StringComparison.Ordinal),
                $"Installer E2E isolation missing {define}.");
        }


        Require(
            installer.Contains(
                "'{#DataDirectoryValue}'",
                StringComparison.Ordinal) &&
            installer.Contains(
                "'{#CredentialTargetValue}'",
                StringComparison.Ordinal) &&
            installer.Contains(
                "'{#PreferenceKeyValue}'",
                StringComparison.Ordinal),
            "Uninstaller destructive targets are not parameterized.");


        Require(
            installer.Contains(
                "/NORUNAPP",
                StringComparison.Ordinal),
            "Silent update E2E cannot suppress application launch.");
        string app = File.ReadAllText(Path.Combine(root, "App.xaml.cs"));
        Require(app.IndexOf("InstallerE2EProbe.TryRun(", StringComparison.Ordinal) <
                app.IndexOf("new Mutex(", StringComparison.Ordinal) &&
            app.Contains("profile.MutexName", StringComparison.Ordinal) &&
            app.Contains("profile.ShowEventName", StringComparison.Ordinal),
            "Installer probe starts after native app initialization or uses production process identity.");
        Require(installer.Contains("#define AllowSilentRemovePreferences 0", StringComparison.Ordinal) &&
            installer.Contains("#if Int(AllowSilentRemovePreferences) == 1", StringComparison.Ordinal) &&
            installer.Contains("skipifsilent", StringComparison.Ordinal),
            "Production silent removal or fresh-install launch defaults changed.");
        string e2eRunner =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "tools",
                    "Test-InstallerE2E.ps1"));

        Require(
            e2eRunner.Contains(
                "LUKNIGHT_E2E_TOKEN",
                StringComparison.Ordinal) &&
            e2eRunner.Contains(
                "LuKnight-E2E",
                StringComparison.Ordinal) &&
            e2eRunner.Contains(
                "/REMOVEPREFERENCES",
                StringComparison.Ordinal) &&
            e2eRunner.Contains(
                "/NORUNAPP",
                StringComparison.Ordinal) &&
            e2eRunner.Contains(
                "/TARGETPID=",
                StringComparison.Ordinal),
            "Installer E2E runner does not exercise the isolated production update path.");

        Require(
            e2eRunner.Contains(
                "installer-e2e-summary.json",
                StringComparison.Ordinal) &&
            e2eRunner.Contains(
                "failed =",
                StringComparison.Ordinal),
            "Installer E2E produces no machine-readable acceptance evidence.");

        Require(
            e2eRunner.Contains(
                "Assert-ChildPath",
                StringComparison.Ordinal) &&
            e2eRunner.Contains(
                "Installer E2E requires a clean Git working tree.",
                StringComparison.Ordinal),
            "Installer E2E lacks filesystem/source safety guards.");
    }
}
