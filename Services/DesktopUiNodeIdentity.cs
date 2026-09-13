using System.Security.Cryptography;
using System.Text;

namespace LuKnight.Services;

public static class DesktopUiNodeIdentity
{
    public static string Fingerprint(DesktopUiNodeSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);
        string value = string.Join(
            "|",
            node.Path,
            node.ControlType,
            node.Name,
            node.AutomationId,
            node.ClassName,
            node.IsPassword);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }
}
