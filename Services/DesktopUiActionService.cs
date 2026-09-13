using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace LuKnight.Services;

public interface IDesktopUiActionExecutor
{
    Task<DesktopActionResult> InvokeAsync(
        DesktopWindowTarget window,
        string controlPath,
        string expectedFingerprint,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsDesktopUiActionExecutor : IDesktopUiActionExecutor
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<DesktopActionResult> InvokeAsync(
        DesktopWindowTarget window,
        string controlPath,
        string expectedFingerprint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool entered = await _gate.WaitAsync(0, cancellationToken);
        if (!entered)
            return new(false, "UI Automation sedang sibuk.");

        try
        {
            return await Task.Run(
                () => InvokeCore(window, controlPath, expectedFingerprint),
                CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static DesktopActionResult InvokeCore(
        DesktopWindowTarget window,
        string path,
        string expectedFingerprint)
    {
        try
        {
            AutomationElement root = AutomationElement.FromHandle(window.Handle);
            AutomationElement? element = ResolvePath(root, path);
            if (element is null)
                return new(false, "Control target sudah tidak tersedia.");

            AutomationElement.AutomationElementInformation info = element.Current;
            if (info.IsPassword)
                return new(false, "Protected control tidak dapat dioperasikan.");

            string type = NormalizeControlType(info.ControlType);
            if (!string.Equals(type, "Button", StringComparison.Ordinal))
                return new(false, "Control target bukan tombol.");
            if (!info.IsEnabled || info.IsOffscreen)
                return new(false, "Tombol tidak tersedia untuk diaktifkan.");

            var snapshot = new DesktopUiNodeSnapshot(
                path,
                DepthFromPath(path),
                type,
                Clean(info.Name),
                Clean(info.AutomationId),
                Clean(info.ClassName),
                info.BoundingRectangle,
                info.IsEnabled,
                info.IsOffscreen,
                info.HasKeyboardFocus,
                info.IsPassword);
            if (!string.Equals(
                    DesktopUiNodeIdentity.Fingerprint(snapshot),
                    expectedFingerprint,
                    StringComparison.Ordinal))
            {
                return new(false, "Control berubah sejak konfirmasi. Ulangi perintah.");
            }

            if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern) ||
                pattern is not InvokePattern invoke)
            {
                return new(false, "Tombol ini tidak mendukung UI Automation Invoke.");
            }

            invoke.Invoke();
            return new(true, $"Tombol {snapshot.DisplayName} diaktifkan.");
        }
        catch (ElementNotEnabledException)
        {
            return new(false, "Tombol sudah tidak aktif.");
        }
        catch (ElementNotAvailableException)
        {
            return new(false, "Control target sudah tidak tersedia.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            return new(false, "UI Automation gagal mengaktifkan tombol.");
        }
    }

    private static AutomationElement? ResolvePath(AutomationElement root, string path)
    {
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] != "0")
            return null;

        AutomationElement current = root;
        TreeWalker walker = TreeWalker.ControlViewWalker;
        for (int level = 1; level < parts.Length; level++)
        {
            if (!int.TryParse(parts[level], out int index) || index < 0)
                return null;

            AutomationElement? child = walker.GetFirstChild(current);
            for (int i = 0; i < index && child is not null; i++)
                child = walker.GetNextSibling(child);
            if (child is null)
                return null;
            current = child;
        }

        return current;
    }

    private static int DepthFromPath(string path) =>
        Math.Max(0, path.Count(x => x == '/'));

    private static string Clean(string? value) =>
        value?.Replace('\r', ' ').Replace('\n', ' ').Trim() ?? string.Empty;

    private static string NormalizeControlType(ControlType? type)
    {
        string value = type?.ProgrammaticName ?? "ControlType.Unknown";
        const string prefix = "ControlType.";
        return value.StartsWith(prefix, StringComparison.Ordinal)
            ? value[prefix.Length..]
            : value;
    }
}
