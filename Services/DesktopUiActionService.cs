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
    private static readonly TimeSpan InvokeTimeout = TimeSpan.FromSeconds(4);
    private readonly SemaphoreSlim _gate = new(1, 1);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

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

        long deadline = Environment.TickCount64 + (long)InvokeTimeout.TotalMilliseconds;
        Task<DesktopActionResult> worker;
        try
        {
            worker = Task.Run(
                () =>
                {
                    try
                    {
                        return InvokeCore(
                            window,
                            controlPath,
                            expectedFingerprint,
                            deadline,
                            cancellationToken);
                    }
                    finally
                    {
                        _gate.Release();
                    }
                },
                CancellationToken.None);
        }
        catch
        {
            _gate.Release();
            throw;
        }

        try
        {
            return await worker.WaitAsync(InvokeTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return new(
                false,
                "UI Automation melewati batas waktu. Status aksi tidak diketahui; jangan ulangi tombol secara otomatis.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private static DesktopActionResult InvokeCore(
        DesktopWindowTarget window,
        string path,
        string expectedFingerprint,
        long deadline,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(deadline))
                return new(false, "UI Automation melewati batas waktu sebelum eksekusi.");
            if (window.Handle == nint.Zero || !IsWindow(window.Handle))
                return new(false, "Window target sudah tidak tersedia.");

            uint threadId = GetWindowThreadProcessId(window.Handle, out uint currentPid);
            if (threadId == 0 || currentPid != (uint)window.ProcessId)
                return new(false, "Window target berubah sebelum eksekusi.");

            AutomationElement root = AutomationElement.FromHandle(window.Handle);
            if (root.Current.ProcessId != window.ProcessId)
                return new(false, "Window target berubah sebelum eksekusi.");

            AutomationElement? element = ResolvePath(root, path);
            if (element is null)
                return new(false, "Control target sudah tidak tersedia.");

            AutomationElement.AutomationElementInformation info = element.Current;
            string type = NormalizeControlType(info.ControlType);
            var snapshot = new DesktopUiNodeSnapshot(
                path,
                DepthFromPath(path),
                type,
                DesktopUiText.Normalize(info.Name),
                DesktopUiText.Normalize(info.AutomationId),
                DesktopUiText.Normalize(info.ClassName),
                info.BoundingRectangle,
                info.IsEnabled,
                info.IsOffscreen,
                info.HasKeyboardFocus,
                info.IsPassword);
            if (DesktopUiActionPolicy.IsTemporarilyBlocked(snapshot, out string policyReason))
                return new(false, policyReason);

            if (!string.Equals(
                    DesktopUiNodeIdentity.Fingerprint(snapshot),
                    expectedFingerprint,
                    StringComparison.Ordinal))
            {
                return new(false, "Control berubah sejak konfirmasi. Ulangi perintah.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(deadline))
                return new(false, "UI Automation melewati batas waktu sebelum tombol diaktifkan.");

            if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern) ||
                pattern is not InvokePattern invoke)
            {
                return new(false, "Tombol ini tidak mendukung UI Automation Invoke.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(deadline))
                return new(false, "UI Automation melewati batas waktu sebelum tombol diaktifkan.");

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

    private static bool Expired(long deadline) =>
        Environment.TickCount64 >= deadline;

    private static string NormalizeControlType(ControlType? type)
    {
        string value = type?.ProgrammaticName ?? "ControlType.Unknown";
        const string prefix = "ControlType.";
        return value.StartsWith(prefix, StringComparison.Ordinal)
            ? value[prefix.Length..]
            : value;
    }
}
