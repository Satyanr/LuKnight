using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace LuKnight.Services;

public readonly record struct DesktopWindowInfo(
    nint Handle,
    Rect Bounds,
    int ZOrder);

public static class DesktopWindowService
{
    private const int DwmwaExtendedFrameBounds =
        9;

    private const int DwmwaCloaked =
        14;

    private const int GwlExStyle =
        -20;

    private const long WsExToolWindow =
        0x00000080L;

    private const long WsExNoActivate =
        0x08000000L;

    private static readonly TimeSpan CacheDuration =
        TimeSpan.FromMilliseconds(100);

    private static readonly object CacheLock =
        new();

    private static DateTime _cacheAt =
        DateTime.MinValue;

    private static IReadOnlyList<DesktopWindowInfo>
        _cachedWindows =
            Array.Empty<DesktopWindowInfo>();


    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }


    private delegate bool EnumWindowsProc(
        nint hwnd,
        nint parameter);


    [DllImport("user32.dll")]
    private static extern bool EnumWindows(
        EnumWindowsProc callback,
        nint parameter);


    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(
        nint hwnd);


    [DllImport("user32.dll")]
    private static extern bool IsIconic(
        nint hwnd);


    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(
        nint hwnd,
        out NativeRect rect);


    [DllImport("user32.dll")]
    private static extern uint
        GetWindowThreadProcessId(
            nint hwnd,
            out uint processId);


    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        nint hwnd,
        StringBuilder className,
        int maximumCount);


    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongPtrW")]
    private static extern nint
        GetWindowLongPtr64(
            nint hwnd,
            int index);


    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongW")]
    private static extern int
        GetWindowLong32(
            nint hwnd,
            int index);


    [DllImport("dwmapi.dll")]
    private static extern int
        DwmGetWindowAttribute(
            nint hwnd,
            int attribute,
            out NativeRect value,
            int size);


    [DllImport("dwmapi.dll")]
    private static extern int
        DwmGetWindowAttribute(
            nint hwnd,
            int attribute,
            out int value,
            int size);


    public static IReadOnlyList<DesktopWindowInfo>
        GetVisibleWindows()
    {
        lock (CacheLock)
        {
            DateTime now =
                DateTime.UtcNow;

            if (now - _cacheAt <
                CacheDuration)
            {
                return _cachedWindows;
            }

            var result =
                new List<DesktopWindowInfo>();

            int zOrder = 0;

            EnumWindowsProc callback =
                (hwnd, parameter) =>
                {
                    int currentZ =
                        zOrder++;

                    if (TryReadWindow(
                            hwnd,
                            currentZ,
                            out DesktopWindowInfo info))
                    {
                        result.Add(info);
                    }

                    return true;
                };

            EnumWindows(
                callback,
                nint.Zero);

            _cachedWindows =
                result;

            _cacheAt = now;

            return _cachedWindows;
        }
    }


    public static bool TryGetWindow(
        nint hwnd,
        out DesktopWindowInfo info)
    {
        return TryReadWindow(
            hwnd,
            0,
            out info);
    }


    public static bool TryFindLandingSurface(
        Rect previousCharacterBounds,
        double nextLeft,
        double nextTop,
        out DesktopWindowInfo surface)
    {
        surface = default;

        double width =
            previousCharacterBounds.Width;

        double height =
            previousCharacterBounds.Height;

        double previousBottom =
            previousCharacterBounds.Bottom;

        double nextBottom =
            nextTop + height;


        if (nextBottom <
            previousBottom)
        {
            return false;
        }


        // Sedikit lebih sempit dari seluruh
        // character window supaya telinga yang
        // menyentuh sisi window tidak dianggap
        // sebagai landing.
        double characterLeft =
            nextLeft + 14;

        double characterRight =
            nextLeft + width - 14;


        double bestY =
            double.MaxValue;

        int bestZ =
            int.MaxValue;

        bool found =
            false;


        foreach (DesktopWindowInfo candidate
                 in GetVisibleWindows())
        {
            double surfaceY =
                candidate.Bounds.Top;


            // Surface harus benar-benar dilewati
            // dari atas ke bawah.
            if (surfaceY <
                    previousBottom - 1 ||
                surfaceY >
                    nextBottom + 1)
            {
                continue;
            }


            double overlap =
                Math.Min(
                    characterRight,
                    candidate.Bounds.Right)
                -
                Math.Max(
                    characterLeft,
                    candidate.Bounds.Left);


            if (overlap < 28)
                continue;


            bool betterSurface =
                surfaceY < bestY - 1;

            bool sameHeightHigherZ =
                Math.Abs(
                    surfaceY - bestY) <= 1
                &&
                candidate.ZOrder < bestZ;


            if (!betterSurface &&
                !sameHeightHigherZ)
            {
                continue;
            }


            surface =
                candidate;

            bestY =
                surfaceY;

            bestZ =
                candidate.ZOrder;

            found =
                true;
        }

        return found;
    }


    private static bool TryReadWindow(
        nint hwnd,
        int zOrder,
        out DesktopWindowInfo info)
    {
        info = default;


        if (hwnd == nint.Zero ||
            !IsWindowVisible(hwnd) ||
            IsIconic(hwnd))
        {
            return false;
        }


        GetWindowThreadProcessId(
            hwnd,
            out uint processId);


        // Jangan jadikan window Lu-Knight sendiri
        // sebagai terrain.
        if (processId ==
            (uint)Environment.ProcessId)
        {
            return false;
        }


        string className =
            ReadClassName(hwnd);


        if (IsShellWindowClass(
                className))
        {
            return false;
        }


        long exStyle =
            GetExtendedStyle(
                hwnd);


        // Abaikan tooltip, popup utility,
        // overlay dan window non-activating.
        if ((exStyle &
             WsExToolWindow) != 0 ||
            (exStyle &
             WsExNoActivate) != 0)
        {
            return false;
        }


        if (IsCloaked(hwnd))
            return false;


        if (!TryGetBounds(
                hwnd,
                out Rect bounds))
        {
            return false;
        }


        // Hindari invisible tiny helper windows.
        if (bounds.Width < 120 ||
            bounds.Height < 60)
        {
            return false;
        }


        info =
            new DesktopWindowInfo(
                hwnd,
                bounds,
                zOrder);

        return true;
    }


    private static bool TryGetBounds(
        nint hwnd,
        out Rect bounds)
    {
        NativeRect nativeRect;

        int result =
            DwmGetWindowAttribute(
                hwnd,
                DwmwaExtendedFrameBounds,
                out nativeRect,
                Marshal.SizeOf<NativeRect>());


        if (result != 0)
        {
            if (!GetWindowRect(
                    hwnd,
                    out nativeRect))
            {
                bounds = default;
                return false;
            }
        }


        if (nativeRect.Right <=
                nativeRect.Left ||
            nativeRect.Bottom <=
                nativeRect.Top)
        {
            bounds = default;
            return false;
        }


        bounds =
            new Rect(
                nativeRect.Left,
                nativeRect.Top,
                nativeRect.Right -
                nativeRect.Left,
                nativeRect.Bottom -
                nativeRect.Top);

        return true;
    }


    private static bool IsCloaked(
        nint hwnd)
    {
        int result =
            DwmGetWindowAttribute(
                hwnd,
                DwmwaCloaked,
                out int cloaked,
                sizeof(int));

        return result == 0 &&
               cloaked != 0;
    }


    private static long GetExtendedStyle(
        nint hwnd)
    {
        if (IntPtr.Size == 8)
        {
            return GetWindowLongPtr64(
                    hwnd,
                    GwlExStyle)
                .ToInt64();
        }

        return GetWindowLong32(
            hwnd,
            GwlExStyle);
    }


    private static string ReadClassName(
        nint hwnd)
    {
        var builder =
            new StringBuilder(256);

        GetClassName(
            hwnd,
            builder,
            builder.Capacity);

        return builder.ToString();
    }


    private static bool IsShellWindowClass(
        string className)
    {
        return className is
            "Progman"
            or "WorkerW"
            or "Shell_TrayWnd"
            or "Shell_SecondaryTrayWnd";
    }
}