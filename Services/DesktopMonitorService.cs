using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LuKnight.Services;

public enum MonitorDirection
{
    Left,
    Right,
    Up,
    Down
}

public readonly record struct DesktopMonitorInfo(
    nint Handle,
    Rect Bounds,
    Rect WorkArea,
    bool IsPrimary)
{
    public Point Center =>
        new(
            Bounds.Left +
            (Bounds.Width / 2.0),
            Bounds.Top +
            (Bounds.Height / 2.0));

    public bool HasTaskbarLeft =>
        WorkArea.Left >
        Bounds.Left + 1;

    public bool HasTaskbarRight =>
        WorkArea.Right <
        Bounds.Right - 1;

    public bool HasTaskbarTop =>
        WorkArea.Top >
        Bounds.Top + 1;

    public bool HasTaskbarBottom =>
        WorkArea.Bottom <
        Bounds.Bottom - 1;
}

public static class DesktopMonitorService
{
    private const uint MonitorDefaultToNearest =
        0x00000002;

    private const uint MonitorInfoPrimary =
        0x00000001;

    private const uint SwpNoSize =
        0x0001;

    private const uint SwpNoZOrder =
        0x0004;

    private const uint SwpNoActivate =
        0x0010;


    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;

        public NativeRect Monitor;
        public NativeRect Work;

        public uint Flags;
    }


    private delegate bool MonitorEnumProc(
        nint monitor,
        nint hdc,
        ref NativeRect monitorRect,
        nint data);


    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        nint hdc,
        nint clipRect,
        MonitorEnumProc callback,
        nint data);


    [DllImport(
        "user32.dll",
        CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(
        nint monitor,
        ref MonitorInfo info);


    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(
        nint hwnd,
        uint flags);


    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(
        nint hwnd,
        out NativeRect rect);


    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        nint hwnd,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);


    public static IReadOnlyList<DesktopMonitorInfo>
        GetAllMonitors()
    {
        var result =
            new List<DesktopMonitorInfo>();

        MonitorEnumProc callback =
            (
                nint monitor,
                nint hdc,
                ref NativeRect rect,
                nint data) =>
            {
                if (TryReadMonitor(
                        monitor,
                        out DesktopMonitorInfo info))
                {
                    result.Add(info);
                }

                return true;
            };

        EnumDisplayMonitors(
            nint.Zero,
            nint.Zero,
            callback,
            nint.Zero);

        return result;
    }


    public static DesktopMonitorInfo
        GetMonitorForWindow(
            Window window)
    {
        nint hwnd =
            new WindowInteropHelper(
                window).Handle;

        if (hwnd == nint.Zero)
        {
            throw new InvalidOperationException(
                "Window handle belum tersedia.");
        }

        nint monitor =
            MonitorFromWindow(
                hwnd,
                MonitorDefaultToNearest);

        if (!TryReadMonitor(
                monitor,
                out DesktopMonitorInfo info))
        {
            throw new InvalidOperationException(
                "Monitor untuk window tidak ditemukan.");
        }

        return info;
    }


    public static Rect GetWindowBounds(
        Window window)
    {
        nint hwnd =
            new WindowInteropHelper(
                window).Handle;

        if (hwnd == nint.Zero ||
            !GetWindowRect(
                hwnd,
                out NativeRect rect))
        {
            return new Rect(
                window.Left,
                window.Top,
                window.ActualWidth,
                window.ActualHeight);
        }

        return ToRect(rect);
    }


    public static void SetWindowPosition(
        Window window,
        double left,
        double top)
    {
        nint hwnd =
            new WindowInteropHelper(
                window).Handle;

        if (hwnd == nint.Zero)
            return;

        SetWindowPos(
            hwnd,
            nint.Zero,
            (int)Math.Round(left),
            (int)Math.Round(top),
            0,
            0,
            SwpNoSize |
            SwpNoZOrder |
            SwpNoActivate);
    }


    public static bool TryGetNeighbor(
        DesktopMonitorInfo current,
        MonitorDirection direction,
        out DesktopMonitorInfo neighbor)
    {
        IReadOnlyList<DesktopMonitorInfo> monitors =
            GetAllMonitors();

        DesktopMonitorInfo best =
            default;

        double bestScore =
            double.MaxValue;

        bool found = false;

        foreach (DesktopMonitorInfo candidate
                 in monitors)
        {
            if (candidate.Handle ==
                current.Handle)
            {
                continue;
            }

            if (!IsCandidateInDirection(
                    current,
                    candidate,
                    direction,
                    out double score))
            {
                continue;
            }

            if (score >= bestScore)
                continue;

            best = candidate;
            bestScore = score;
            found = true;
        }

        neighbor = best;

        return found;
    }


    private static bool IsCandidateInDirection(
        DesktopMonitorInfo current,
        DesktopMonitorInfo candidate,
        MonitorDirection direction,
        out double score)
    {
        score =
            double.MaxValue;

        bool verticalOverlap =
            OverlapLength(
                current.Bounds.Top,
                current.Bounds.Bottom,
                candidate.Bounds.Top,
                candidate.Bounds.Bottom) > 1;

        bool horizontalOverlap =
            OverlapLength(
                current.Bounds.Left,
                current.Bounds.Right,
                candidate.Bounds.Left,
                candidate.Bounds.Right) > 1;


        switch (direction)
        {
            case MonitorDirection.Left:

                if (!verticalOverlap ||
                    candidate.Center.X >=
                    current.Center.X)
                {
                    return false;
                }

                score =
                    Math.Max(
                        0,
                        current.Bounds.Left -
                        candidate.Bounds.Right)
                    +
                    Math.Abs(
                        current.Center.Y -
                        candidate.Center.Y)
                    * 0.10;

                return true;


            case MonitorDirection.Right:

                if (!verticalOverlap ||
                    candidate.Center.X <=
                    current.Center.X)
                {
                    return false;
                }

                score =
                    Math.Max(
                        0,
                        candidate.Bounds.Left -
                        current.Bounds.Right)
                    +
                    Math.Abs(
                        current.Center.Y -
                        candidate.Center.Y)
                    * 0.10;

                return true;


            case MonitorDirection.Up:

                if (!horizontalOverlap ||
                    candidate.Center.Y >=
                    current.Center.Y)
                {
                    return false;
                }

                score =
                    Math.Max(
                        0,
                        current.Bounds.Top -
                        candidate.Bounds.Bottom)
                    +
                    Math.Abs(
                        current.Center.X -
                        candidate.Center.X)
                    * 0.10;

                return true;


            case MonitorDirection.Down:

                if (!horizontalOverlap ||
                    candidate.Center.Y <=
                    current.Center.Y)
                {
                    return false;
                }

                score =
                    Math.Max(
                        0,
                        candidate.Bounds.Top -
                        current.Bounds.Bottom)
                    +
                    Math.Abs(
                        current.Center.X -
                        candidate.Center.X)
                    * 0.10;

                return true;


            default:

                return false;
        }
    }


    private static double OverlapLength(
        double startA,
        double endA,
        double startB,
        double endB)
    {
        return Math.Max(
            0,
            Math.Min(endA, endB) -
            Math.Max(startA, startB));
    }


    private static bool TryReadMonitor(
        nint monitor,
        out DesktopMonitorInfo info)
    {
        var nativeInfo =
            new MonitorInfo
            {
                Size =
                    (uint)Marshal.SizeOf<
                        MonitorInfo>()
            };

        if (monitor == nint.Zero ||
            !GetMonitorInfo(
                monitor,
                ref nativeInfo))
        {
            info = default;
            return false;
        }

        info =
            new DesktopMonitorInfo(
                monitor,
                ToRect(
                    nativeInfo.Monitor),
                ToRect(
                    nativeInfo.Work),
                (nativeInfo.Flags &
                 MonitorInfoPrimary) != 0);

        return true;
    }


    private static Rect ToRect(
        NativeRect rect)
    {
        return new Rect(
            rect.Left,
            rect.Top,
            rect.Right -
            rect.Left,
            rect.Bottom -
            rect.Top);
    }
}