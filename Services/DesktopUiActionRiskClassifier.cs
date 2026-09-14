using System.Text.RegularExpressions;
using LuKnight.Assistant;

namespace LuKnight.Services;

public sealed record DesktopUiActionRiskAssessment(
    bool Valid,
    AssistantActionRisk Risk,
    string Message);

public static class DesktopUiActionRiskClassifier
{
    private static readonly string[]
        SensitiveTerms =
    [
        // Persistent/destructive
        "save", "save as", "overwrite", "replace",
        "delete", "remove", "erase", "discard",
        "clear", "wipe", "format", "reset",
        "factory reset", "uninstall",
        "rename", "move", "archive",

        // External side effects
        "send", "submit", "upload",
        "publish", "post", "share",
        "buy", "purchase", "pay",
        "checkout", "order", "subscribe",

        // Files/software
        "download", "export", "import",
        "install", "update",

        // Authorization/session
        "accept", "allow", "authorize",
        "grant", "approve", "finish",
        "sign in", "log in", "login",

        // Execution/system
        "run", "execute",
        "restart", "reboot", "shutdown",
        "close", "exit", "quit",
        "sign out", "log out", "logout",

        // Indonesian
        "simpan", "simpan sebagai",
        "timpa", "ganti file",
        "hapus", "buang", "bersihkan",
        "format", "reset",
        "ganti nama", "pindahkan", "arsipkan",

        "kirim", "unggah",
        "publikasikan", "bagikan",
        "beli", "bayar", "pesan",
        "berlangganan",

        "unduh", "ekspor", "impor",
        "instal", "perbarui",

        "terima", "izinkan",
        "otorisasi", "setujui",
        "selesai",

        "masuk", "login",
        "jalankan", "eksekusi",

        "mulai ulang", "matikan",
        "tutup", "keluar"
    ];

    private static readonly HashSet<string>
        OpaqueConfirmationLabels =
        new(StringComparer.Ordinal)
        {
            "ok",
            "yes",
            "ya",
            "iya",
            "confirm",
            "konfirmasi",
            "continue",
            "lanjut",
            "proceed",
            "apply",
            "terapkan"
        };

    private static readonly Regex
        CamelBoundary =
        new(
            @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly Regex
        NonAlphaNumeric =
        new(
            @"[^\p{L}\p{N}]+",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    public static DesktopUiActionRiskAssessment
        ClassifyButton(
            DesktopUiNodeSnapshot node,
            DesktopWindowTarget? window = null)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.IsProtected)
        {
            return Invalid(
                "Protected control tidak dapat dioperasikan.");
        }

        if (!string.Equals(
                node.ControlType,
                "Button",
                StringComparison.Ordinal))
        {
            return Invalid(
                "Hanya tombol yang didukung pada tahap ini.");
        }

        if (!node.IsEnabled ||
            node.IsOffscreen)
        {
            return Invalid(
                "Tombol tidak tersedia untuk diaktifkan.");
        }

        string label =
            Normalize(node.Name);

        if (label.Length == 0)
        {
            return Invalid(
                "Tombol tanpa nama belum boleh diaktifkan.");
        }

        // Label seperti OK / Yes tidak memberi kita cukup
        // informasi tentang side effect sebenarnya.
        if (OpaqueConfirmationLabels.Contains(
                label))
        {
            return new(
                true,
                AssistantActionRisk.Prohibited,
                "Tombol konfirmasi generik tidak memiliki konteks yang cukup untuk dioperasikan dengan aman.");
        }

        string metadata =
            Normalize(
                string.Join(
                    " ",
                    node.Name,
                    node.AutomationId,
                    node.ClassName,
                    window?.Title ?? string.Empty,
                    window?.ProcessName ??
                        string.Empty));

        foreach (string term in SensitiveTerms)
        {
            if (ContainsTerm(
                    metadata,
                    Normalize(term)))
            {
                return new(
                    true,
                    AssistantActionRisk.Sensitive,
                    "Tindakan UI diklasifikasikan sebagai sensitif.");
            }
        }

        return new(
            true,
            AssistantActionRisk.Interaction,
            string.Empty);
    }

    private static DesktopUiActionRiskAssessment
        Invalid(
            string message) =>
        new(
            false,
            AssistantActionRisk.Prohibited,
            message);

    private static bool ContainsTerm(
        string metadata,
        string term)
    {
        string[] actual =
            metadata.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

        string[] expected =
            term.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

        if (expected.Length == 0 ||
            actual.Length < expected.Length)
        {
            return false;
        }

        for (int start = 0;
             start <= actual.Length -
                 expected.Length;
             start++)
        {
            bool match = true;

            for (int index = 0;
                 index < expected.Length;
                 index++)
            {
                if (string.Equals(
                        actual[start + index],
                        expected[index],
                        StringComparison.Ordinal))
                {
                    continue;
                }

                bool final =
                    index ==
                    expected.Length - 1;

                if (final &&
                    HasNumericSuffix(
                        actual[start + index],
                        expected[index]))
                {
                    continue;
                }

                match = false;
                break;
            }

            if (match)
                return true;
        }

        return false;
    }

    private static bool HasNumericSuffix(
        string actual,
        string expected)
    {
        if (!actual.StartsWith(
                expected,
                StringComparison.Ordinal) ||
            actual.Length <=
                expected.Length)
        {
            return false;
        }

        ReadOnlySpan<char> suffix =
            actual.AsSpan(
                expected.Length);

        foreach (char c in suffix)
        {
            if (!char.IsDigit(c))
                return false;
        }

        return true;
    }

    private static string Normalize(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        string split =
            CamelBoundary.Replace(
                value,
                " ");

        return NonAlphaNumeric
            .Replace(
                split,
                " ")
            .Trim()
            .ToLowerInvariant();
    }
}
