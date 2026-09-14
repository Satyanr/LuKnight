using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace LuKnight.Services;

public enum DesktopUiScreenEvidenceOutcome
{
    Captured,
    NotMapped,
    Rejected,
    Indeterminate
}

public sealed record DesktopUiScreenEvidenceResult(
    DesktopUiScreenEvidenceOutcome Outcome,
    string Message,
    DesktopUiScreenEvidence? Evidence = null)
{
    public bool Success =>
        Outcome ==
        DesktopUiScreenEvidenceOutcome.Captured;

    public bool CanEliminateCandidate =>
        Outcome ==
        DesktopUiScreenEvidenceOutcome.NotMapped;

    public static DesktopUiScreenEvidenceResult Captured(
        string message,
        DesktopUiScreenEvidence evidence) =>
        new(
            DesktopUiScreenEvidenceOutcome.Captured,
            message,
            evidence);

    public static DesktopUiScreenEvidenceResult NotMapped(
        string message) =>
        new(
            DesktopUiScreenEvidenceOutcome.NotMapped,
            message);

    public static DesktopUiScreenEvidenceResult Rejected(
        string message) =>
        new(
            DesktopUiScreenEvidenceOutcome.Rejected,
            message);

    public static DesktopUiScreenEvidenceResult Indeterminate(
        string message) =>
        new(
            DesktopUiScreenEvidenceOutcome.Indeterminate,
            message);
}

public interface IDesktopUiScreenEvidenceService
{
    Task<DesktopUiScreenEvidenceResult> CaptureAsync(
        DesktopWindowTarget window,
        string controlPath,
        string expectedFingerprint,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsDesktopUiScreenEvidenceService
    : IDesktopUiScreenEvidenceService
{
    private static readonly TimeSpan Timeout =
        TimeSpan.FromSeconds(4);

    private readonly SemaphoreSlim _gate =
        new(1, 1);

    private const uint GaRoot =
        2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;

        public NativePoint(
            int x,
            int y)
        {
            X = x;
            Y = y;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(
        nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint
        GetWindowThreadProcessId(
            nint hwnd,
            out uint processId);

    [DllImport("user32.dll")]
    private static extern nint
        WindowFromPoint(
            NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint
        GetAncestor(
            nint hwnd,
            uint flags);

    public async Task<DesktopUiScreenEvidenceResult>
        CaptureAsync(
            DesktopWindowTarget window,
            string controlPath,
            string expectedFingerprint,
            CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        bool entered =
            await _gate.WaitAsync(
                0,
                cancellationToken);

        if (!entered)
        {
            return DesktopUiScreenEvidenceResult.Indeterminate("Screen-assisted UIA sedang sibuk.");
        }

        long deadline =
            Environment.TickCount64 +
            (long)Timeout.TotalMilliseconds;

        Task<DesktopUiScreenEvidenceResult> worker;

        try
        {
            worker =
                Task.Run(
                    () =>
                    {
                        try
                        {
                            return CaptureCore(
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
            return await worker.WaitAsync(
                Timeout,
                cancellationToken);
        }
        catch (TimeoutException)
        {
            return DesktopUiScreenEvidenceResult.Indeterminate("Screen-assisted UIA melewati batas waktu.");
        }
    }

    private static DesktopUiScreenEvidenceResult
        CaptureCore(
            DesktopWindowTarget window,
            string path,
            string expectedFingerprint,
            long deadline,
            CancellationToken cancellationToken)
    {
        ScreenRegionCaptureSnapshot? captured = null;
        bool transferCapturedBytes = false;

        try
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            if (Expired(deadline))
                return DesktopUiScreenEvidenceResult.Indeterminate("Screen-assisted UIA melewati batas waktu.");

            if (!ValidateWindow(window))
            {
                return DesktopUiScreenEvidenceResult.Rejected(
                    "Window target berubah atau tidak tersedia.");
            }

            AutomationElement root =
                AutomationElement.FromHandle(
                    window.Handle);

            if (root.Current.ProcessId !=
                window.ProcessId)
            {
                return DesktopUiScreenEvidenceResult.Rejected(
                    "Window target berubah.");
            }

            AutomationElement? element =
                DesktopUiAutomationLocator
                    .ResolvePath(
                        root,
                        path);

            if (element is null)
            {
                return DesktopUiScreenEvidenceResult.Rejected(
                    "UI target tidak ditemukan.");
            }

            DesktopUiNodeSnapshot node =
                Snapshot(
                    element,
                    path);

            if (!string.Equals(
                    DesktopUiNodeIdentity
                        .Fingerprint(node),
                    expectedFingerprint,
                    StringComparison.Ordinal))
            {
                return DesktopUiScreenEvidenceResult.Rejected(
                    "UI target berubah sejak snapshot.");
            }

            if (!DesktopUiScreenAssistPolicy
                    .ValidateNode(
                        node,
                        out string reason))
            {
                return DesktopUiScreenEvidenceResult.Rejected(
                    reason);
            }

            if (!TryToDrawingRectangle(
                    root.Current
                        .BoundingRectangle,
                    out Drawing.Rectangle
                        windowBounds))
            {
                return DesktopUiScreenEvidenceResult.Rejected(
                    "Bounding rectangle window tidak valid.");
            }

            Drawing.Rectangle
                virtualScreen =
                    Forms.SystemInformation
                        .VirtualScreen;

            if (!DesktopUiScreenAssistGeometry
                    .TryGetCaptureBounds(
                        node.Bounds,
                        windowBounds,
                        virtualScreen,
                        out Drawing.Rectangle
                            captureBounds))
            {
                return DesktopUiScreenEvidenceResult.Rejected(
                    "Region screen evidence tidak valid.");
            }

            if (!DesktopMouseGeometry
                    .TryGetCenter(
                        node.Bounds,
                        out Point center))
            {
                return DesktopUiScreenEvidenceResult.Rejected(
                    "Center UI target tidak valid.");
            }

            var centerPoint =
                new Drawing.Point(
                    checked(
                        (int)Math.Round(
                            center.X)),
                    checked(
                        (int)Math.Round(
                            center.Y)));

            if (!windowBounds.Contains(
                    centerPoint) ||
                !virtualScreen.Contains(
                    centerPoint))
            {
                return DesktopUiScreenEvidenceResult.NotMapped("Center UI target berada di luar window.");
            }

            if (!ValidateHitTarget(
                    window,
                    element,
                    centerPoint))
            {
                return DesktopUiScreenEvidenceResult.NotMapped("Screen hit-test tidak cocok dengan UI target.");
            }

            if (!ValidateRegionOwnership(
                    window,
                    captureBounds))
            {
                return DesktopUiScreenEvidenceResult.NotMapped("Region target tertutup atau dimiliki window lain.");
            }

            cancellationToken
                .ThrowIfCancellationRequested();

            if (Expired(deadline))
            {
                return DesktopUiScreenEvidenceResult.Indeterminate("Screen evidence melewati batas waktu sebelum capture.");
            }

            captured = ScreenCaptureService.CaptureRegion(captureBounds);

            if (captured is null)
            {
                return DesktopUiScreenEvidenceResult.Indeterminate("Region layar tidak dapat diambil.");
            }

            if (captured.SourceBounds !=
                captureBounds)
            {
                return DesktopUiScreenEvidenceResult.Indeterminate("Captured region berubah.");
            }

            cancellationToken
                .ThrowIfCancellationRequested();

            if (Expired(deadline) ||
                !ValidateWindow(window))
            {
                return DesktopUiScreenEvidenceResult.Indeterminate(
                    "Target berubah setelah screen capture.");
            }

            AutomationElement freshRoot =
                AutomationElement.FromHandle(
                    window.Handle);

            if (freshRoot.Current.ProcessId !=
                window.ProcessId)
            {
                return DesktopUiScreenEvidenceResult.Indeterminate(
                    "Window berubah setelah capture.");
            }

            AutomationElement? freshElement =
                DesktopUiAutomationLocator
                    .ResolvePath(
                        freshRoot,
                        path);

            if (freshElement is null)
            {
                return DesktopUiScreenEvidenceResult.Indeterminate(
                    "UI target hilang setelah capture.");
            }

            DesktopUiNodeSnapshot fresh =
                Snapshot(
                    freshElement,
                    path);

            if (!string.Equals(
                    DesktopUiNodeIdentity
                        .Fingerprint(fresh),
                    expectedFingerprint,
                    StringComparison.Ordinal))
            {
                return DesktopUiScreenEvidenceResult.Indeterminate(
                    "UI target berubah selama screen capture.");
            }

            if (!DesktopUiScreenAssistPolicy
                    .ValidateNode(
                        fresh,
                        out reason))
            {
                return DesktopUiScreenEvidenceResult.Indeterminate(
                    reason);
            }

            if (!BoundsClose(
                    node.Bounds,
                    fresh.Bounds))
            {
                return DesktopUiScreenEvidenceResult.Indeterminate(
                    "UI target berpindah selama screen capture.");
            }

            if (!TryToDrawingRectangle(freshRoot.Current.BoundingRectangle, out Drawing.Rectangle freshWindowBounds) ||
                freshWindowBounds != windowBounds ||
                Forms.SystemInformation.VirtualScreen != virtualScreen)
                return DesktopUiScreenEvidenceResult.Indeterminate("Window atau virtual screen berubah selama capture.");

            if (!ValidateHitTarget(
                    window,
                    freshElement,
                    centerPoint) ||
                !ValidateRegionOwnership(
                    window,
                    captureBounds))
            {
                return DesktopUiScreenEvidenceResult.Indeterminate(
                    "Screen mapping berubah selama capture.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(deadline) || !ValidateWindow(window))
                return DesktopUiScreenEvidenceResult.Indeterminate("Screen mapping melewati batas waktu atau window berubah setelah revalidation.");

            var evidence = new DesktopUiScreenEvidence(
                path,
                expectedFingerprint,
                fresh.Bounds,
                captureBounds,
                captured.MimeType,
                captured.EncodedBytes,
                captured.Width,
                captured.Height);

            transferCapturedBytes = true;
            return DesktopUiScreenEvidenceResult.Captured(
                "Bounded screen evidence berhasil dikaitkan ke UIA target yang sama.",
                evidence);
        }
        catch (ElementNotAvailableException)
        {
            return DesktopUiScreenEvidenceResult.Indeterminate(
                "UI target sudah tidak tersedia.");
        }
        catch (COMException)
        {
            return DesktopUiScreenEvidenceResult.Indeterminate(
                "UI Automation gagal saat memetakan screen evidence.");
        }
        catch (InvalidOperationException)
        {
            return DesktopUiScreenEvidenceResult.Indeterminate(
                "UI target tidak lagi valid.");
        }
        catch (OverflowException)
        {
            return DesktopUiScreenEvidenceResult.Rejected(
                "Koordinat screen evidence tidak valid.");
        }
        finally
        {
            if (!transferCapturedBytes && captured?.EncodedBytes is { Length: > 0 } bytes)
                Array.Clear(bytes, 0, bytes.Length);
        }
    }

    private static bool ValidateHitTarget(
        DesktopWindowTarget window,
        AutomationElement target,
        Drawing.Point point)
    {
        nint hitWindow =
            WindowFromPoint(
                new NativePoint(
                    point.X,
                    point.Y));

        if (hitWindow ==
            nint.Zero)
        {
            return false;
        }

        nint rootWindow =
            GetAncestor(
                hitWindow,
                GaRoot);

        if (rootWindow !=
            window.Handle)
        {
            return false;
        }

        // UIA failures are indeterminate, never evidence for eliminating a candidate.
        AutomationElement hit = AutomationElement.FromPoint(new Point(point.X, point.Y));

        return IsSameOrDescendant(
            hit,
            target);
    }

    private static bool IsSameOrDescendant(
        AutomationElement candidate,
        AutomationElement target)
    {
        AutomationElement? current =
            candidate;

        TreeWalker walker =
            TreeWalker.RawViewWalker;

        for (int depth = 0;
             depth <= 12 &&
             current is not null;
             depth++)
        {
            if (Automation.Compare(
                    current,
                    target))
            {
                return true;
            }

            current =
                walker.GetParent(
                    current);
        }

        return false;
    }

    private static bool
        ValidateRegionOwnership(
            DesktopWindowTarget window,
            Drawing.Rectangle region)
    {
        if (region.Width < 1 ||
            region.Height < 1)
        {
            return false;
        }

        int left =
            region.Left;

        int centerX =
            region.Left +
            region.Width / 2;

        int right =
            region.Right - 1;

        int top =
            region.Top;

        int centerY =
            region.Top +
            region.Height / 2;

        int bottom =
            region.Bottom - 1;

        int[] xs =
            [left, centerX, right];

        int[] ys =
            [top, centerY, bottom];

        foreach (int x in xs)
        {
            foreach (int y in ys)
            {
                nint hit =
                    WindowFromPoint(
                        new NativePoint(
                            x,
                            y));

                if (hit ==
                    nint.Zero)
                {
                    return false;
                }

                if (GetAncestor(
                        hit,
                        GaRoot) !=
                    window.Handle)
                {
                    return false;
                }
            }
        }

        return true;
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
            info.ControlType?
                .ProgrammaticName ??
            "ControlType.Unknown";

        const string prefix =
            "ControlType.";

        if (type.StartsWith(
                prefix,
                StringComparison.Ordinal))
        {
            type =
                type[prefix.Length..];
        }

        return new DesktopUiNodeSnapshot(
            path,
            Math.Max(
                0,
                path.Count(
                    x => x == '/')),
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

    private static bool
        TryToDrawingRectangle(
            Rect rect,
            out Drawing.Rectangle result)
    {
        result =
            Drawing.Rectangle.Empty;

        if (rect.IsEmpty ||
            !Finite(rect.Left) ||
            !Finite(rect.Top) ||
            !Finite(rect.Right) ||
            !Finite(rect.Bottom))
        {
            return false;
        }

        double left =
            Math.Floor(
                rect.Left);

        double top =
            Math.Floor(
                rect.Top);

        double right =
            Math.Ceiling(
                rect.Right);

        double bottom =
            Math.Ceiling(
                rect.Bottom);

        if (left < int.MinValue ||
            top < int.MinValue ||
            right > int.MaxValue ||
            bottom > int.MaxValue ||
            right - left > int.MaxValue ||
            bottom - top > int.MaxValue ||
            right <= left ||
            bottom <= top)
        {
            return false;
        }

        result =
            Drawing.Rectangle.FromLTRB(
                (int)left,
                (int)top,
                (int)right,
                (int)bottom);

        return true;
    }

    private static bool BoundsClose(
        Rect first,
        Rect second)
    {
        const double tolerance =
            1.0;

        return
            Math.Abs(
                first.Left -
                second.Left) <=
            tolerance &&

            Math.Abs(
                first.Top -
                second.Top) <=
            tolerance &&

            Math.Abs(
                first.Width -
                second.Width) <=
            tolerance &&

            Math.Abs(
                first.Height -
                second.Height) <=
            tolerance;
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

    private static bool Finite(
        double value) =>
        !double.IsNaN(value) &&
        !double.IsInfinity(value);

    private static bool Expired(
        long deadline) =>
        Environment.TickCount64 >=
        deadline;

}
