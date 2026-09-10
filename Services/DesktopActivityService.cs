using System;
using System.Runtime.InteropServices;

namespace LuKnight.Services;

public static class DesktopActivityService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }


    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(
        ref LastInputInfo info);


    public static TimeSpan GetIdleTime()
    {
        var info =
            new LastInputInfo
            {
                Size =
                    (uint)Marshal.SizeOf<
                        LastInputInfo>()
            };


        if (!GetLastInputInfo(
                ref info))
        {
            return TimeSpan.Zero;
        }


        uint now =
            unchecked(
                (uint)Environment.TickCount);


        uint elapsed =
            unchecked(
                now - info.Time);


        return TimeSpan.FromMilliseconds(
            elapsed);
    }


    public static bool IsUserActive(
        double thresholdSeconds = 8)
    {
        return GetIdleTime() <
            TimeSpan.FromSeconds(
                thresholdSeconds);
    }
}