using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace LuKnight.Services;

public interface IDesktopKeyboardTextActionExecutor
{
    Task<DesktopActionResult> ReplaceTextAsync(
        DesktopWindowTarget window,
        string controlPath,
        string expectedFingerprint,
        string value,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsDesktopKeyboardTextActionExecutor
    : IDesktopKeyboardTextActionExecutor
{
    private static readonly TimeSpan Timeout =
        TimeSpan.FromSeconds(4);

    private readonly SemaphoreSlim _gate =
        new(1, 1);

    private const uint InputKeyboard = 1;

    private const ushort VkControl = 0x11;
    private const ushort VkA = 0x41;

    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

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
        GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool
        SetForegroundWindow(
            nint hwnd);

    [DllImport("user32.dll")]
    private static extern short
        GetAsyncKeyState(
            int virtualKey);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern uint
        SendInput(
            uint count,
            Input[] inputs,
            int size);

    public async Task<DesktopActionResult>
        ReplaceTextAsync(
            DesktopWindowTarget window,
            string controlPath,
            string expectedFingerprint,
            string value,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!DesktopUiTextInputPolicy.ValidateValue(
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
                "Keyboard automation sedang sibuk.");
        }

        long deadline =
            Environment.TickCount64 +
            (long)Timeout.TotalMilliseconds;

        Task<DesktopActionResult> worker;

        try
        {
            worker =
                Task.Run(
                    () =>
                    {
                        try
                        {
                            return ReplaceCore(
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
                "Keyboard automation melewati batas waktu. Status input tidak diketahui; jangan retry otomatis.");
        }
    }

    private static DesktopActionResult ReplaceCore(
        DesktopWindowTarget window,
        string path,
        string expectedFingerprint,
        string value,
        long deadline,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Expired(deadline) ||
                !ValidateWindow(window))
            {
                return new(
                    false,
                    "Window target berubah atau tidak tersedia.");
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
                DesktopUiAutomationLocator.ResolvePath(
                    root,
                    path);

            if (element is null)
            {
                return new(
                    false,
                    "Text field tidak tersedia.");
            }

            DesktopUiNodeSnapshot snapshot =
                Snapshot(
                    element,
                    path);

            if (!string.Equals(
                    DesktopUiNodeIdentity.Fingerprint(
                        snapshot),
                    expectedFingerprint,
                    StringComparison.Ordinal))
            {
                return new(
                    false,
                    "Text field berubah sejak konfirmasi.");
            }

            if (!DesktopUiTextInputPolicy.ValidateTarget(
                    snapshot,
                    out string policyReason))
            {
                return new(
                    false,
                    policyReason);
            }

            // Independently recheck the fallback boundary before changing focus.
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out _))
                return new(false, "ValuePattern tersedia; keyboard fallback dibatalkan.");

            if (!element.Current.IsKeyboardFocusable)
            {
                return new(
                    false,
                    "Text field tidak dapat menerima keyboard focus.");
            }

            // Jangan ambil alih input saat user sedang
            // menekan tombol keyboard/mouse.
            if (AnyPhysicalKeyPressed())
            {
                return new(
                    false,
                    "Keyboard atau mouse sedang digunakan. Aksi dibatalkan.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (Expired(deadline) ||
                !ValidateWindow(window))
            {
                return new(
                    false,
                    "Target berubah sebelum keyboard focus.");
            }

            if (GetForegroundWindow() !=
                window.Handle)
            {
                if (!SetForegroundWindow(
                        window.Handle))
                {
                    return new(
                        false,
                        "Window target tidak dapat dijadikan foreground.");
                }
            }

            if (GetForegroundWindow() !=
                window.Handle)
            {
                return new(
                    false,
                    "Window target bukan foreground window.");
            }

            element.SetFocus();

            if (!element.Current.HasKeyboardFocus)
            {
                return new(
                    false,
                    "Text field tidak memperoleh keyboard focus.");
            }

            DesktopUiNodeSnapshot final =
                Snapshot(
                    element,
                    path);

            if (!string.Equals(
                    DesktopUiNodeIdentity.Fingerprint(
                        final),
                    expectedFingerprint,
                    StringComparison.Ordinal))
            {
                return new(
                    false,
                    "Text field berubah setelah memperoleh focus.");
            }

            if (!DesktopUiTextInputPolicy.ValidateTarget(
                    final,
                    out policyReason))
            {
                return new(
                    false,
                    policyReason);
            }

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out _))
                return new(false, "ValuePattern berubah setelah focus; keyboard fallback dibatalkan.");

            Input[] inputs = BuildReplaceTextInput(value);

            cancellationToken.ThrowIfCancellationRequested();

            if (Expired(deadline) ||
                !ValidateWindow(window) ||
                GetForegroundWindow() != window.Handle ||
                !element.Current.HasKeyboardFocus ||
                AnyPhysicalKeyPressed())
            {
                return new(
                    false,
                    "Kondisi input berubah sebelum keyboard injection.");
            }

            uint sent =
                SendInput(
                    (uint)inputs.Length,
                    inputs,
                    Marshal.SizeOf<Input>());

            if (sent != inputs.Length)
            {
                return new(
                    false,
                    "Windows hanya menerima sebagian keyboard input. Status teks tidak diketahui; jangan retry otomatis.");
            }

            return new(
                true,
                $"Teks dimasukkan ke {final.DisplayName} melalui keyboard fallback tervalidasi.");
        }
        catch (ElementNotAvailableException)
        {
            return new(
                false,
                "Text field sudah tidak tersedia.");
        }
        catch (Exception ex)
            when (
                ex is COMException
                or InvalidOperationException)
        {
            return new(
                false,
                "Keyboard fallback gagal memvalidasi text field.");
        }
    }

    private static Input[] BuildReplaceTextInput(
        string value)
    {
        var inputs =
            new List<Input>(
                4 +
                value.Length * 2);

        // Select-all hanya hardcoded di executor ini.
        // Tidak ada arbitrary hotkey API.
        inputs.Add(
            VirtualKey(
                VkControl,
                keyUp: false));

        inputs.Add(
            VirtualKey(
                VkA,
                keyUp: false));

        inputs.Add(
            VirtualKey(
                VkA,
                keyUp: true));

        inputs.Add(
            VirtualKey(
                VkControl,
                keyUp: true));

        foreach (char character in value)
        {
            inputs.Add(
                UnicodeKey(
                    character,
                    keyUp: false));

            inputs.Add(
                UnicodeKey(
                    character,
                    keyUp: true));
        }

        return inputs.ToArray();
    }

    private static Input VirtualKey(
        ushort virtualKey,
        bool keyUp) =>
        new()
        {
            Type =
                InputKeyboard,

            Data =
                new InputUnion
                {
                    Keyboard =
                        new KeyboardInputData
                        {
                            VirtualKey =
                                virtualKey,

                            Flags =
                                keyUp
                                    ? KeyEventKeyUp
                                    : 0
                        }
                }
        };

    private static Input UnicodeKey(
        char character,
        bool keyUp) =>
        new()
        {
            Type =
                InputKeyboard,

            Data =
                new InputUnion
                {
                    Keyboard =
                        new KeyboardInputData
                        {
                            VirtualKey =
                                0,

                            ScanCode =
                                character,

                            Flags =
                                KeyEventUnicode |
                                (keyUp
                                    ? KeyEventKeyUp
                                    : 0)
                        }
                }
        };

    [StructLayout(
        LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(
        LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInputData Mouse;

        [FieldOffset(0)]
        public KeyboardInputData Keyboard;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    private static bool AnyPhysicalKeyPressed()
    {
        for (int virtualKey = 1;
             virtualKey <= 254;
             virtualKey++)
        {
            if ((GetAsyncKeyState(
                     virtualKey) &
                 0x8000) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ValidateWindow(
        DesktopWindowTarget window)
    {
        if (window.Handle == nint.Zero ||
            !IsWindow(window.Handle))
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
