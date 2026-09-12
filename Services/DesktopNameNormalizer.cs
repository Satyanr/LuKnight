using System.Text.RegularExpressions;

namespace LuKnight.Services;

public static partial class DesktopNameNormalizer
{
    public static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? "" :
        Separators().Replace(value.Trim().ToLowerInvariant(), " ").Trim();

    public static IEnumerable<string> BuildAliases(string displayName, IEnumerable<string> processes)
    {
        string full = Normalize(displayName);
        string simplified = Regex.Replace(full, @"^(microsoft|adobe|google|mozilla|autodesk) ", "");
        simplified = Regex.Replace(simplified, @"\b(20\d{2}|x64|x86|64 bit|32 bit)\b", "");
        return new[] { full, Normalize(simplified) }.Concat(processes.Select(Normalize))
            .Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static double Similarity(string left, string right)
    {
        if (left == right) return 1;
        int maximum = Math.Max(left.Length, right.Length);
        if (maximum == 0) return 1;
        if (maximum > 256) return 0;
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        int[] current = new int[right.Length + 1];
        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return 1d - previous[right.Length] / (double)maximum;
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex Separators();
}
