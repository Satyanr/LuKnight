namespace LuKnight.Services;

public static class DesktopUiActionPolicy
{
    private static readonly string[] BlockedPhrases =
    [
        "save", "save as", "overwrite", "replace", "delete", "remove", "erase",
        "wipe", "format", "reset", "factory reset", "uninstall",
        "send", "submit", "upload", "publish", "post", "buy", "purchase",
        "pay", "checkout", "order", "subscribe",
        "close", "exit", "quit", "sign out", "log out", "logout",
        "simpan", "simpan sebagai", "timpa", "ganti file", "hapus", "buang",
        "kirim", "unggah", "publikasikan", "beli", "bayar", "pesan",
        "berlangganan", "tutup", "keluar"
    ];

    private static readonly HashSet<string> GenericConfirmationLabels = new(StringComparer.Ordinal)
    {
        "ok", "yes", "ya", "iya", "confirm", "konfirmasi", "continue",
        "lanjut", "proceed", "apply", "terapkan"
    };

    public static bool IsTemporarilyBlocked(DesktopUiNodeSnapshot node, out string reason)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.IsProtected)
        {
            reason = "Protected control tidak dapat dioperasikan.";
            return true;
        }
        if (!string.Equals(node.ControlType, "Button", StringComparison.Ordinal))
        {
            reason = "Hanya tombol yang didukung pada tahap ini.";
            return true;
        }
        if (!node.IsEnabled || node.IsOffscreen)
        {
            reason = "Tombol tidak tersedia untuk diaktifkan.";
            return true;
        }

        string label = Normalize(node.Name);
        if (label.Length == 0)
        {
            reason = "Tombol tanpa nama belum boleh diaktifkan.";
            return true;
        }
        if (GenericConfirmationLabels.Contains(label))
        {
            reason = "Tombol konfirmasi generik diblokir sampai permission model tersedia.";
            return true;
        }
        foreach (string phrase in BlockedPhrases)
        {
            if (ContainsPhrase(label, Normalize(phrase)))
            {
                reason = "Tombol ini dapat menghasilkan perubahan sensitif atau destruktif dan belum diizinkan pada tahap ini.";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    private static bool ContainsPhrase(string value, string phrase) =>
        $" {value} ".Contains($" {phrase} ", StringComparison.Ordinal);

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return string.Join(
            ' ',
            value.Trim().ToLowerInvariant()
                .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }
}
