using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace LuKnight.Services;

public interface IDesktopMouseActionExecutor
{
    Task<DesktopActionResult> ClickAsync(
        DesktopWindowTarget window,
        string controlPath,
        string expectedFingerprint,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsDesktopMouseActionExecutor : IDesktopMouseActionExecutor
{
    private static readonly TimeSpan ClickTimeout = TimeSpan.FromSeconds(4);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private const uint InputMouse = 0;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint GetAncestorRoot = 2;
    private const int VkLeftButton = 0x01;
    private const int VkRightButton = 0x02;
    private const int VkMiddleButton = 0x04;

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    public async Task<DesktopActionResult> ClickAsync(
        DesktopWindowTarget window,
        string controlPath,
        string expectedFingerprint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool entered = await _gate.WaitAsync(0, cancellationToken);
        if (!entered)
            return new(false, "Mouse automation sedang sibuk.");

        long deadline = Environment.TickCount64 + (long)ClickTimeout.TotalMilliseconds;
        Task<DesktopActionResult> worker;
        try
        {
            worker = Task.Run(
                () =>
                {
                    try
                    {
                        return ClickCore(
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
            return await worker.WaitAsync(ClickTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return new(false, "Mouse automation melewati batas waktu. Tidak ada retry otomatis.");
        }
    }

    private static DesktopActionResult ClickCore(
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
                return new(false, "Mouse automation melewati batas waktu.");
            if (!ValidateWindow(window))
                return new(false, "Window target berubah atau sudah tidak tersedia.");

            AutomationElement root = AutomationElement.FromHandle(window.Handle);
            if (root.Current.ProcessId != window.ProcessId)
                return new(false, "Window target berubah.");

            AutomationElement? element = DesktopUiAutomationLocator.ResolvePath(root, path);
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

            if (!string.Equals(
                    DesktopUiNodeIdentity.Fingerprint(snapshot),
                    expectedFingerprint,
                    StringComparison.Ordinal))
            {
                return new(false, "Control berubah sejak konfirmasi.");
            }
            if (DesktopUiActionPolicy.IsTemporarilyBlocked(snapshot, out string policyReason))
                return new(false, policyReason);
            if (!DesktopMouseGeometry.TryGetCenter(snapshot.Bounds, out System.Windows.Point center))
                return new(false, "Control tidak memiliki area klik yang aman.");

            var point = new NativePoint
            {
                X = checked((int)center.X),
                Y = checked((int)center.Y)
            };
            if (AnyMouseButtonPressed())
                return new(false, "Mouse sedang digunakan. Aksi dibatalkan.");
            if (!ValidatePoint(window, element, point))
                return new(false, "Area tombol tertutup atau target pointer berubah.");
            if (!GetCursorPos(out NativePoint original))
                return new(false, "Posisi pointer tidak dapat dibaca.");

            bool pointerMoved = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Expired(deadline) || !ValidateWindow(window))
                    return new(false, "Target berubah sebelum klik.");
                if (!SetCursorPos(point.X, point.Y))
                    return new(false, "Pointer tidak dapat dipindahkan.");
                pointerMoved = true;

                if (Expired(deadline) ||
                    AnyMouseButtonPressed() ||
                    !ValidateWindow(window) ||
                    !ValidatePoint(window, element, point))
                {
                    return new(false, "Mouse target berubah sebelum klik.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                Input[] inputs = [MouseInput(MouseLeftDown), MouseInput(MouseLeftUp)];
                uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
                if (sent != inputs.Length)
                    return new(false, "Windows menolak mouse input.");
                return new(true, $"Tombol {snapshot.DisplayName} diklik.");
            }
            finally
            {
                if (pointerMoved &&
                    GetCursorPos(out NativePoint current) &&
                    Math.Abs(current.X - point.X) <= 2 &&
                    Math.Abs(current.Y - point.Y) <= 2)
                {
                    SetCursorPos(original.X, original.Y);
                }
            }
        }
        catch (ElementNotAvailableException)
        {
            return new(false, "Control target sudah tidak tersedia.");
        }
        catch (COMException)
        {
            return new(false, "UI Automation gagal memvalidasi mouse target.");
        }
        catch (InvalidOperationException)
        {
            return new(false, "Mouse target tidak lagi valid.");
        }
        catch (OverflowException)
        {
            return new(false, "Koordinat control tidak valid.");
        }
    }

    private static bool ValidateWindow(DesktopWindowTarget window)
    {
        if (window.Handle == nint.Zero || !IsWindow(window.Handle))
            return false;
        uint thread = GetWindowThreadProcessId(window.Handle, out uint pid);
        return thread != 0 && pid == (uint)window.ProcessId;
    }

    private static bool ValidatePoint(
        DesktopWindowTarget window,
        AutomationElement target,
        NativePoint point)
    {
        nint hwnd = WindowFromPoint(point);
        if (hwnd == nint.Zero || GetAncestor(hwnd, GetAncestorRoot) != window.Handle)
            return false;

        AutomationElement hit = AutomationElement.FromPoint(
            new System.Windows.Point(point.X, point.Y));
        return IsTargetOrDescendant(hit, target);
    }

    private static bool IsTargetOrDescendant(
        AutomationElement hit,
        AutomationElement target)
    {
        AutomationElement? current = hit;
        TreeWalker walker = TreeWalker.RawViewWalker;
        for (int depth = 0; depth <= 12 && current is not null; depth++)
        {
            if (Automation.Compare(current, target))
                return true;
            current = walker.GetParent(current);
        }
        return false;
    }

    private static bool AnyMouseButtonPressed() =>
        IsPressed(VkLeftButton) || IsPressed(VkRightButton) || IsPressed(VkMiddleButton);

    private static bool IsPressed(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static Input MouseInput(uint flags) => new()
    {
        Type = InputMouse,
        Data = new InputUnion { Mouse = new MouseInputData { Flags = flags } }
    };

    private static bool Expired(long deadline) => Environment.TickCount64 >= deadline;
    private static int DepthFromPath(string path) => Math.Max(0, path.Count(x => x == '/'));

    private static string NormalizeControlType(ControlType? type)
    {
        string value = type?.ProgrammaticName ?? "ControlType.Unknown";
        const string prefix = "ControlType.";
        return value.StartsWith(prefix, StringComparison.Ordinal)
            ? value[prefix.Length..]
            : value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInputData Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
}
