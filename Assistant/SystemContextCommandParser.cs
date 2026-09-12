namespace LuKnight.Assistant;

public enum SystemContextScope
{
    Summary,
    Battery,
    Memory,
    Network,
    OperatingSystem
}

public sealed record SystemContextCommand(SystemContextScope Scope);

public static class SystemContextCommandParser
{
    public static SystemContextCommand? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        string value = input.Trim().ToLowerInvariant();

        if (Matches(value,
                "status sistem", "info sistem", "informasi sistem",
                "cek kondisi komputer", "cek kondisi pc", "system status", "system info"))
            return new(SystemContextScope.Summary);

        if (Matches(value,
                "cek baterai", "status baterai", "berapa baterai",
                "berapa persen baterai", "battery status", "battery level"))
            return new(SystemContextScope.Battery);

        if (Matches(value,
                "cek ram", "status ram", "ram tersedia",
                "berapa ram tersedia", "berapa ram yang tersedia",
                "memory status", "available ram"))
            return new(SystemContextScope.Memory);

        if (Matches(value,
                "cek jaringan", "status jaringan", "network status", "network available"))
            return new(SystemContextScope.Network);

        if (Matches(value,
                "versi windows", "info windows", "sistem operasi apa",
                "windows version", "os version"))
            return new(SystemContextScope.OperatingSystem);

        return null;
    }

    private static bool Matches(string value, params string[] phrases)
    {
        return phrases.Any(phrase =>
            value.Equals(phrase, StringComparison.Ordinal) ||
            value.StartsWith(phrase + " ", StringComparison.Ordinal) ||
            value.StartsWith(phrase + "?", StringComparison.Ordinal));
    }
}
