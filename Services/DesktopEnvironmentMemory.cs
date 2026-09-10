using System;
using System.Collections.Generic;
using System.Windows;

namespace LuKnight.Services;


public readonly record struct
    DesktopWindowMemory(
        nint WindowHandle,
        int ProcessId,
        string ProcessName,
        DesktopApplicationKind ApplicationKind,
        Rect LastBounds,
        DateTime LastVisitedAt,
        int VisitCount);


public sealed class DesktopEnvironmentMemory
{
    // ===================================
    // ARRIVAL REACTION MEMORY
    // ===================================

    private readonly Dictionary<string, DateTime>
        _applicationVisits =
            new(
                StringComparer
                    .OrdinalIgnoreCase);


    // ===================================
    // PHYSICAL WINDOW MEMORY
    // ===================================

    private readonly Dictionary<
        nint,
        DesktopWindowMemory>
        _windowVisits =
            new();


    private static readonly TimeSpan
        ArrivalRepeatDelay =
            TimeSpan.FromSeconds(
                90);


    private static readonly TimeSpan
        WindowMemoryLifetime =
            TimeSpan.FromMinutes(
                15);


    // ===================================
    // ARRIVAL REACTION
    // ===================================

    public bool CanReactToArrival(
        DesktopApplicationContext application,
        DateTime now)
    {
        string key =
            GetApplicationKey(
                application);


        if (string.IsNullOrWhiteSpace(
                key))
        {
            return false;
        }


        if (_applicationVisits
            .TryGetValue(
                key,
                out DateTime lastVisit) &&
            now - lastVisit <
                ArrivalRepeatDelay)
        {
            return false;
        }


        return true;
    }


    public void MarkArrivalReactionPlayed(
        DesktopApplicationContext application,
        DateTime now)
    {
        string key =
            GetApplicationKey(
                application);


        if (string.IsNullOrWhiteSpace(
                key))
        {
            return;
        }


        _applicationVisits[key] =
            now;


        CleanupOldEntries(
            now);
    }


    // ===================================
    // WINDOW WORLD MEMORY
    // ===================================

    public void RememberWindow(
        nint windowHandle,
        DateTime now)
    {
        if (windowHandle ==
            nint.Zero)
        {
            return;
        }


        if (!DesktopWindowService
            .TryGetWindow(
                windowHandle,
                out DesktopWindowInfo window))
        {
            return;
        }


        bool hasApplication =
            DesktopApplicationService
                .TryGetApplication(
                    windowHandle,
                    out DesktopApplicationContext
                        application);


        int processId =
            hasApplication
                ? application.ProcessId
                : 0;


        string processName =
            hasApplication
                ? application.ProcessName
                : "unknown";


        DesktopApplicationKind kind =
            hasApplication
                ? application.Kind
                : DesktopApplicationKind.Unknown;


        int visitCount =
            1;


        if (_windowVisits
            .TryGetValue(
                windowHandle,
                out DesktopWindowMemory
                    previous))
        {
            bool sameProcess =
                processId == 0 ||
                previous.ProcessId == 0 ||
                previous.ProcessId ==
                    processId;


            if (sameProcess)
            {
                visitCount =
                    Math.Min(
                        12,
                        previous.VisitCount +
                        1);
            }
        }


        _windowVisits[
            windowHandle] =
                new DesktopWindowMemory(
                    windowHandle,
                    processId,
                    processName,
                    kind,
                    window.Bounds,
                    now,
                    visitCount);


        CleanupOldEntries(
            now);
    }


    // ===================================
    // NAVIGATION BIAS
    // ===================================

    public double
        GetNavigationScoreAdjustment(
            DesktopWindowInfo candidate,
            DateTime now)
    {
        if (!_windowVisits
            .TryGetValue(
                candidate.Handle,
                out DesktopWindowMemory
                    memory))
        {
            return 0;
        }


        TimeSpan age =
            now -
            memory.LastVisitedAt;


        if (age >
            WindowMemoryLifetime)
        {
            return 0;
        }


        // Baru saja meninggalkan window ini.
        // Hindari ping-pong langsung.
        if (age <
            TimeSpan.FromSeconds(
                8))
        {
            return 180;
        }


        // Pastikan HWND belum dipakai ulang
        // oleh process yang berbeda.
        if (memory.ProcessId != 0)
        {
            if (!DesktopApplicationService
                .TryGetApplication(
                    candidate.Handle,
                    out DesktopApplicationContext
                        currentApplication))
            {
                return 0;
            }


            if (currentApplication.ProcessId !=
                memory.ProcessId)
            {
                return 0;
            }
        }


        double recencyBonus;


        if (age <
            TimeSpan.FromMinutes(
                2))
        {
            recencyBonus =
                -160;
        }
        else if (age <
                 TimeSpan.FromMinutes(
                     5))
        {
            recencyBonus =
                -110;
        }
        else
        {
            recencyBonus =
                -60;
        }


        // Window yang sering dikunjungi
        // makin terasa familiar.
        double visitBonus =
            -Math.Min(
                60,
                Math.Max(
                    0,
                    memory.VisitCount - 1) *
                12);


        // Kita juga mengingat posisi terakhir.
        // Kalau window berpindah jauh,
        // familiarity bonus dikurangi sedikit.
        double oldCenterX =
            memory.LastBounds.Left +
            (memory.LastBounds.Width / 2);


        double oldCenterY =
            memory.LastBounds.Top +
            (memory.LastBounds.Height / 2);


        double currentCenterX =
            candidate.Bounds.Left +
            (candidate.Bounds.Width / 2);


        double currentCenterY =
            candidate.Bounds.Top +
            (candidate.Bounds.Height / 2);


        double movedX =
            currentCenterX -
            oldCenterX;


        double movedY =
            currentCenterY -
            oldCenterY;


        double movedDistance =
            Math.Sqrt(
                (movedX * movedX) +
                (movedY * movedY));


        double movementPenalty =
            Math.Min(
                90,
                movedDistance *
                0.08);


        return
            recencyBonus +
            visitBonus +
            movementPenalty;
    }


    private static string
        GetApplicationKey(
            DesktopApplicationContext
                application)
    {
        return application
            .ProcessName
            .Trim();
    }


    // ===================================
    // CLEANUP
    // ===================================

    private void CleanupOldEntries(
        DateTime now)
    {
        var expiredApplications =
            new List<string>();


        foreach (var entry
                 in _applicationVisits)
        {
            if (now - entry.Value >
                TimeSpan.FromMinutes(
                    15))
            {
                expiredApplications.Add(
                    entry.Key);
            }
        }


        foreach (string key
                 in expiredApplications)
        {
            _applicationVisits.Remove(
                key);
        }


        var expiredWindows =
            new List<nint>();


        foreach (var entry
                 in _windowVisits)
        {
            if (now -
                entry.Value.LastVisitedAt >
                WindowMemoryLifetime)
            {
                expiredWindows.Add(
                    entry.Key);
            }
        }


        foreach (nint handle
                 in expiredWindows)
        {
            _windowVisits.Remove(
                handle);
        }
    }
}