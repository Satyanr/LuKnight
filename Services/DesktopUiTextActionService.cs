using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace LuKnight.Services;

public sealed record DesktopUiTextResult(
    bool Success,
    string Message);

public interface IDesktopUiTextActionExecutor
{
    Task<DesktopUiTextResult> SetTextAsync(
        DesktopWindowTarget window,
        string controlPath,
        string expectedFingerprint,
        string value,
        CancellationToken cancellationToken = default);
}

public sealed class
    WindowsDesktopUiTextActionExecutor
        : IDesktopUiTextActionExecutor
{
    private static readonly TimeSpan
        Timeout =
            TimeSpan.FromSeconds(4);

    private readonly SemaphoreSlim _gate =
        new(1, 1);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(
        nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint
        GetWindowThreadProcessId(
            nint hwnd,
            out uint processId);

    public async Task<DesktopUiTextResult>
        SetTextAsync(
            DesktopWindowTarget window,
            string controlPath,
            string expectedFingerprint,
            string value,
            CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        if (!DesktopUiTextInputPolicy
                .ValidateValue(
                    value,
                    out string valueReason))
        {
            return new(
                false,
                valueReason);
        }

        bool entered =
            await _gate.WaitAsync(
                0,
                cancellationToken);

        if (!entered)
        {
            return new(
                false,
                "UI text automation sedang sibuk.");
        }

        long deadline =
            Environment.TickCount64 +
            (long)Timeout.TotalMilliseconds;

        Task<DesktopUiTextResult> worker;

        try
        {
            worker =
                Task.Run(
                    () =>
                    {
                        try
                        {
                            return SetTextCore(
                                window,
                                controlPath,
                                expectedFingerprint,
                                value,
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
            return await worker.WaitAsync(
                Timeout,
                cancellationToken);
        }
        catch (TimeoutException)
        {
            return new(
                false,
                "UI text automation melewati batas waktu. Status input tidak diketahui; jangan retry otomatis.");
        }
    }

    private static DesktopUiTextResult
        SetTextCore(
            DesktopWindowTarget window,
            string path,
            string expectedFingerprint,
            string value,
            long deadline,
            CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            if (Expired(deadline))
            {
                return new(
                    false,
                    "UI text automation melewati batas waktu.");
            }

            if (!ValidateWindow(
                    window))
            {
                return new(
                    false,
                    "Window target berubah atau sudah tidak tersedia.");
            }

            AutomationElement root =
                AutomationElement.FromHandle(
                    window.Handle);

            if (root.Current.ProcessId !=
                window.ProcessId)
            {
                return new(
                    false,
                    "Window target berubah.");
            }

            AutomationElement? element =
                DesktopUiAutomationLocator
                    .ResolvePath(
                        root,
                        path);

            if (element is null)
            {
                return new(
                    false,
                    "Text field sudah tidak tersedia.");
            }

            DesktopUiNodeSnapshot snapshot =
                Snapshot(
                    element,
                    path);

            if (!string.Equals(
                    DesktopUiNodeIdentity
                        .Fingerprint(snapshot),
                    expectedFingerprint,
                    StringComparison.Ordinal))
            {
                return new(
                    false,
                    "Text field berubah sejak konfirmasi.");
            }

            if (!DesktopUiTextInputPolicy
                    .ValidateTarget(
                        snapshot,
                        out string policyReason))
            {
                return new(
                    false,
                    policyReason);
            }

            cancellationToken
                .ThrowIfCancellationRequested();

            if (Expired(deadline))
            {
                return new(
                    false,
                    "UI text automation melewati batas waktu sebelum input.");
            }

            bool hasPattern =
                element.TryGetCurrentPattern(
                    ValuePattern.Pattern,
                    out object? rawPattern);

            cancellationToken
                .ThrowIfCancellationRequested();

            if (Expired(deadline))
            {
                return new(
                    false,
                    "UI text automation melewati batas waktu saat membaca ValuePattern.");
            }

            if (!hasPattern ||
                rawPattern is not
                    ValuePattern pattern)
            {
                return new(
                    false,
                    "Text field tidak menyediakan UI Automation ValuePattern.");
            }

            if (pattern.Current.IsReadOnly)
            {
                return new(
                    false,
                    "Text field bersifat read-only.");
            }

            // Re-read immediately before mutation.
            DesktopUiNodeSnapshot final =
                Snapshot(
                    element,
                    path);

            if (!string.Equals(
                    DesktopUiNodeIdentity
                        .Fingerprint(final),
                    expectedFingerprint,
                    StringComparison.Ordinal))
            {
                return new(
                    false,
                    "Text field berubah tepat sebelum input.");
            }

            if (!DesktopUiTextInputPolicy
                    .ValidateTarget(
                        final,
                        out policyReason))
            {
                return new(
                    false,
                    policyReason);
            }

            cancellationToken
                .ThrowIfCancellationRequested();

            if (Expired(deadline) ||
                !ValidateWindow(window))
            {
                return new(
                    false,
                    "Target berubah sebelum text input.");
            }

            try
            {
                pattern.SetValue(
                    value);

                return new(
                    true,
                    $"Teks berhasil dimasukkan ke {final.DisplayName} melalui UI Automation.");
            }
            catch (ElementNotEnabledException)
            {
                return new(
                    false,
                    "Text field sudah tidak aktif.");
            }
            catch (InvalidOperationException)
            {
                // SetValue sudah dicoba / provider mungkin
                // berubah menjadi readonly.
                return new(
                    false,
                    "Text field menolak perubahan nilai.");
            }
            catch (ElementNotAvailableException)
            {
                return new(
                    false,
                    "Text field sudah tidak tersedia.");
            }
            catch (COMException)
            {
                return new(
                    false,
                    "UI Automation gagal ketika mengisi text field. Status input tidak diketahui; jangan retry otomatis.");
            }
        }
        catch (ElementNotAvailableException)
        {
            return new(
                false,
                "Text field sudah tidak tersedia.");
        }
        catch (COMException)
        {
            return new(
                false,
                "UI Automation gagal memvalidasi text field.");
        }
        catch (InvalidOperationException)
        {
            return new(
                false,
                "Text field tidak lagi valid.");
        }
    }

    private static DesktopUiNodeSnapshot
        Snapshot(
            AutomationElement element,
            string path)
    {
        AutomationElement
            .AutomationElementInformation
            info =
                element.Current;

        string type =
            NormalizeControlType(
                info.ControlType);

        return new DesktopUiNodeSnapshot(
            path,
            DepthFromPath(path),
            type,
            info.IsPassword
                ? "[protected]"
                : DesktopUiText.Normalize(
                    info.Name),
            info.IsPassword
                ? string.Empty
                : DesktopUiText.Normalize(
                    info.AutomationId),
            info.IsPassword
                ? string.Empty
                : DesktopUiText.Normalize(
                    info.ClassName),
            info.BoundingRectangle,
            info.IsEnabled,
            info.IsOffscreen,
            info.HasKeyboardFocus,
            info.IsPassword);
    }

    private static bool ValidateWindow(
        DesktopWindowTarget window)
    {
        if (window.Handle ==
                nint.Zero ||
            !IsWindow(
                window.Handle))
        {
            return false;
        }

        uint thread =
            GetWindowThreadProcessId(
                window.Handle,
                out uint pid);

        return thread != 0 &&
               pid ==
                   (uint)window.ProcessId;
    }

    private static bool Expired(
        long deadline) =>
        Environment.TickCount64 >=
        deadline;

    private static int DepthFromPath(
        string path) =>
        Math.Max(
            0,
            path.Count(
                x => x == '/'));

    private static string NormalizeControlType(
        ControlType? type)
    {
        string value =
            type?.ProgrammaticName ??
            "ControlType.Unknown";

        const string prefix =
            "ControlType.";

        return value.StartsWith(
                prefix,
                StringComparison.Ordinal)
            ? value[prefix.Length..]
            : value;
    }
}
