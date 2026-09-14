namespace LuKnight.Services;

public static class DesktopUiTextInputPolicy
{
    public const int MaxInputLength =
        1000;

    private static readonly string[]
        SensitiveTerms =
        [
            "password",
            "passwd",
            "passcode",
            "pin",
            "otp",
            "one time password",

            "cvv",
            "cvc",
            "security code",

            "secret",
            "token",
            "api key",
            "apikey",
            "private key",

            "recovery code",
            "backup code",
            "verification code",
            "auth code",

            "kata sandi",
            "sandi",
            "kode otp",
            "kode keamanan",
            "kode verifikasi",
            "kunci api",
            "kunci pribadi"
        ];

    public static bool ValidateTarget(
        DesktopUiNodeSnapshot node,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(
            node);

        if (!string.Equals(
                node.ControlType,
                "Edit",
                StringComparison.Ordinal))
        {
            reason =
                "Text input hanya didukung untuk control Edit.";

            return false;
        }

        if (node.IsProtected ||
            node.IsPassword)
        {
            reason =
                "Lu-Knight tidak boleh mengisi password atau protected field.";

            return false;
        }

        if (!node.IsEnabled ||
            node.IsOffscreen)
        {
            reason =
                "Text field tidak tersedia untuk diisi.";

            return false;
        }

        string metadata =
            Normalize(
                string.Join(
                    ' ',
                    node.Name,
                    node.AutomationId,
                    node.ClassName));

        foreach (string term in SensitiveTerms)
        {
            if (ContainsPhrase(
                    metadata,
                    Normalize(term)))
            {
                reason =
                    "Field terlihat seperti credential atau secret dan tidak boleh diisi.";

                return false;
            }
        }

        reason =
            string.Empty;

        return true;
    }

    public static bool ValidateValue(
        string value,
        out string reason)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            reason =
                "Teks tidak boleh kosong.";

            return false;
        }

        if (value.Length >
            MaxInputLength)
        {
            reason =
                $"Teks terlalu panjang. Maksimum {MaxInputLength} karakter.";

            return false;
        }

        // 10D.1 hanya single-line ValuePattern.
        if (value.Contains('\r') ||
            value.Contains('\n'))
        {
            reason =
                "10D.1 hanya mendukung teks satu baris.";

            return false;
        }

        if (value.Any(
                char.IsControl))
        {
            reason =
                "Teks mengandung control character yang tidak didukung.";

            return false;
        }

        reason =
            string.Empty;

        return true;
    }

    private static bool ContainsPhrase(
        string value,
        string phrase) =>
        $" {value} ".Contains(
            $" {phrase} ",
            StringComparison.Ordinal);

    private static string Normalize(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        // UIA AutomationId/ClassName commonly use camelCase or acronym prefixes.
        value = System.Text.RegularExpressions.Regex.Replace(
            value, "([A-Z]+)([A-Z][a-z])", "$1 $2");
        value = System.Text.RegularExpressions.Regex.Replace(
            value, "([a-z0-9])([A-Z])", "$1 $2");

        var buffer =
            new System.Text.StringBuilder(
                value.Length);

        bool spacing =
            false;

        foreach (char c in
                 value
                     .Trim()
                     .ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (spacing &&
                    buffer.Length > 0)
                {
                    buffer.Append(' ');
                }

                buffer.Append(c);
                spacing =
                    false;
            }
            else if (buffer.Length > 0)
            {
                spacing =
                    true;
            }
        }

        return buffer
            .ToString()
            .Trim();
    }
}
