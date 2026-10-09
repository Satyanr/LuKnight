using System.IO;
using System.Text.RegularExpressions;

namespace LuKnight.Services;


public sealed record RuntimeProfileDefinition(
    bool IsE2E,
    string? Token,
    string UserDirectory,
    string MutexName,
    string ShowEventName,
    string StartupRunValueName,
    string StartupPreferenceKey,
    string CredentialTarget);


public static class RuntimeProfile
{
    public const string
        E2EEnvironmentVariable =
            "LUKNIGHT_E2E_TOKEN";


    private static readonly Regex
        TokenPattern =
            new(
                @"\A[a-fA-F0-9]{32}\z",
                RegexOptions.Compiled |
                RegexOptions.CultureInvariant);


    public static RuntimeProfileDefinition
        Current
    {
        get;
    } =
        Resolve(
            Environment
                .GetEnvironmentVariable(
                    E2EEnvironmentVariable));


    public static RuntimeProfileDefinition
        Resolve(
            string? token)
    {
        string local =
            Environment.GetFolderPath(
                Environment.SpecialFolder
                    .LocalApplicationData);


        if (string.IsNullOrWhiteSpace(
                token))
        {
            return new(
                IsE2E:
                    false,

                Token:
                    null,

                UserDirectory:
                    Path.Combine(
                        local,
                        "LuKnight"),

                MutexName:
                    @"Local\LuKnight-App",

                ShowEventName:
                    @"Local\LuKnight-Show",

                StartupRunValueName:
                    "LuKnight",

                StartupPreferenceKey:
                    @"Software\LuKnight",

                CredentialTarget:
                    "LuKnight/GeminiApiKey");
        }


        token =
            token.Trim();


        if (!TokenPattern.IsMatch(
                token))
        {
            throw new
                InvalidOperationException(
                    "Invalid Lu-Knight E2E profile token.");
        }


        return new(
            IsE2E:
                true,

            Token:
                token,

            UserDirectory:
                Path.Combine(
                    local,
                    "LuKnight-E2E",
                    token),

            MutexName:
                $@"Local\LuKnight-E2E-{token}",

            ShowEventName:
                $@"Local\LuKnight-E2E-Show-{token}",

            StartupRunValueName:
                $"LuKnight-E2E-{token}",

            StartupPreferenceKey:
                $@"Software\LuKnight-E2E\{token}",

            CredentialTarget:
                $"LuKnight-E2E/{token}/GeminiApiKey");
    }
}
