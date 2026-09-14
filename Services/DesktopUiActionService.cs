using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace LuKnight.Services;

public enum DesktopUiInvokeOutcome
{
    Invoked,
    UnsupportedPattern,
    Rejected,
    Indeterminate
}

public sealed record DesktopUiInvokeResult(DesktopUiInvokeOutcome Outcome, string Message)
{
    public bool Success => Outcome == DesktopUiInvokeOutcome.Invoked;
    public bool CanMouseFallback => Outcome == DesktopUiInvokeOutcome.UnsupportedPattern;

    public static DesktopUiInvokeResult Invoked(string message) =>
        new(DesktopUiInvokeOutcome.Invoked, message);
    public static DesktopUiInvokeResult Unsupported(string message) =>
        new(DesktopUiInvokeOutcome.UnsupportedPattern, message);
    public static DesktopUiInvokeResult Rejected(string message) =>
        new(DesktopUiInvokeOutcome.Rejected, message);
    public static DesktopUiInvokeResult Indeterminate(string message) =>
        new(DesktopUiInvokeOutcome.Indeterminate, message);
}

public interface IDesktopUiActionExecutor
{
    Task<DesktopUiInvokeResult> InvokeAsync(
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

    public async Task<DesktopUiInvokeResult> InvokeAsync(
        DesktopWindowTarget window,
        string controlPath,
        string expectedFingerprint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool entered = await _gate.WaitAsync(0, cancellationToken);
        if (!entered)
            return DesktopUiInvokeResult.Rejected("UI Automation sedang sibuk.");

        long deadline = Environment.TickCount64 + (long)InvokeTimeout.TotalMilliseconds;
        Task<DesktopUiInvokeResult> worker;
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
            return DesktopUiInvokeResult.Indeterminate(
                "UI Automation melewati batas waktu. Status aksi tidak diketahui; mouse fallback dibatalkan.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private static DesktopUiInvokeResult InvokeCore(
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
                return DesktopUiInvokeResult.Indeterminate("UI Automation melewati batas waktu sebelum eksekusi.");
            if (window.Handle == nint.Zero || !IsWindow(window.Handle))
                return DesktopUiInvokeResult.Rejected("Window target sudah tidak tersedia.");

            uint threadId = GetWindowThreadProcessId(window.Handle, out uint currentPid);
            if (threadId == 0 || currentPid != (uint)window.ProcessId)
                return DesktopUiInvokeResult.Rejected("Window target berubah sebelum eksekusi.");

            AutomationElement root = AutomationElement.FromHandle(window.Handle);
            if (root.Current.ProcessId != window.ProcessId)
                return DesktopUiInvokeResult.Rejected("Window target berubah sebelum eksekusi.");

            AutomationElement? element = DesktopUiAutomationLocator.ResolvePath(root, path);
            if (element is null)
                return DesktopUiInvokeResult.Rejected("Control target sudah tidak tersedia.");

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
            if (DesktopUiActionPolicy.IsTemporarilyBlocked(window, snapshot, out string policyReason))
                return DesktopUiInvokeResult.Rejected(policyReason);

            if (!string.Equals(
                    DesktopUiNodeIdentity.Fingerprint(snapshot),
                    expectedFingerprint,
                    StringComparison.Ordinal))
            {
                return DesktopUiInvokeResult.Rejected("Control berubah sejak konfirmasi. Ulangi perintah.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(deadline))
                return DesktopUiInvokeResult.Indeterminate("UI Automation melewati batas waktu sebelum tombol diaktifkan.");

            bool hasInvokePattern = element.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern);
            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(deadline))
                return DesktopUiInvokeResult.Indeterminate("UI Automation melewati batas waktu saat membaca InvokePattern; mouse fallback dibatalkan.");

            if (!hasInvokePattern || pattern is not InvokePattern invoke)
            {
                return DesktopUiInvokeResult.Unsupported(
                    "Tombol tidak menyediakan UI Automation InvokePattern.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(deadline))
                return DesktopUiInvokeResult.Indeterminate("UI Automation melewati batas waktu sebelum tombol diaktifkan.");

            try
            {
                invoke.Invoke();
                return DesktopUiInvokeResult.Invoked(
                    $"Tombol {snapshot.DisplayName} diaktifkan melalui UI Automation.");
            }
            catch (ElementNotEnabledException)
            {
                return DesktopUiInvokeResult.Rejected("Tombol sudah tidak aktif.");
            }
            catch (ElementNotAvailableException)
            {
                return DesktopUiInvokeResult.Rejected("Control target sudah tidak tersedia.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException)
            {
                // Invoke was attempted; the provider may already have performed the action.
                return DesktopUiInvokeResult.Indeterminate(
                    "UI Automation gagal saat Invoke. Status aksi tidak diketahui; mouse fallback dibatalkan.");
            }
        }
        catch (ElementNotEnabledException)
        {
            return DesktopUiInvokeResult.Rejected("Tombol sudah tidak aktif.");
        }
        catch (ElementNotAvailableException)
        {
            return DesktopUiInvokeResult.Rejected("Control target sudah tidak tersedia.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            return DesktopUiInvokeResult.Rejected("UI Automation gagal mengaktifkan tombol.");
        }
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
