using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LuKnight.Services;

public enum DesktopApplicationKind
{
    Unknown,

    Browser,
    CodeEditor,
    Creative,
    Office,
    FileManager,
    Communication
}


public readonly record struct
    DesktopApplicationContext(
        nint WindowHandle,
        int ProcessId,
        string ProcessName,
        DesktopApplicationKind Kind);


public static class DesktopApplicationService
{
    [DllImport("user32.dll")]
    private static extern uint
        GetWindowThreadProcessId(
            nint hwnd,
            out uint processId);


    public static bool TryGetApplication(
        nint windowHandle,
        out DesktopApplicationContext context)
    {
        context = default;


        if (windowHandle ==
            nint.Zero)
        {
            return false;
        }


        GetWindowThreadProcessId(
            windowHandle,
            out uint processId);


        if (processId == 0)
        {
            return false;
        }


        try
        {
            using Process process =
                Process.GetProcessById(
                    (int)processId);


            string processName =
                process.ProcessName;


            context =
                new DesktopApplicationContext(
                    windowHandle,
                    (int)processId,
                    processName,
                    Classify(
                        processName));


            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }


    private static DesktopApplicationKind
        Classify(
            string processName)
    {
        string name =
            processName
                .Trim()
                .ToLowerInvariant();


        return name switch
        {
            // =========================
            // BROWSER
            // =========================

            "chrome"
                or "msedge"
                or "firefox"
                or "brave"
                or "opera"
                or "vivaldi"
                => DesktopApplicationKind.Browser,


            // =========================
            // CODE / IDE
            // =========================

            "code"
                or "cursor"
                or "devenv"
                or "rider64"
                or "zed"
                => DesktopApplicationKind.CodeEditor,


            // =========================
            // CREATIVE
            // =========================

            "photoshop"
                or "illustrator"
                or "lightroom"
                or "afterfx"
                or "premiere"
                => DesktopApplicationKind.Creative,


            // =========================
            // OFFICE
            // =========================

            "winword"
                or "excel"
                or "powerpnt"
                or "onenote"
                => DesktopApplicationKind.Office,


            // =========================
            // FILE MANAGER
            // =========================

            "explorer"
                => DesktopApplicationKind.FileManager,


            // =========================
            // COMMUNICATION
            // =========================

            "discord"
                or "slack"
                or "teams"
                or "ms-teams"
                or "zoom"
                => DesktopApplicationKind.Communication,


            _ =>
                DesktopApplicationKind.Unknown
        };
    }
}