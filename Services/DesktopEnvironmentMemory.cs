using System;
using System.Collections.Generic;

namespace LuKnight.Services;

public sealed class DesktopEnvironmentMemory
{
    private readonly Dictionary<string, DateTime>
        _applicationVisits =
            new(StringComparer.OrdinalIgnoreCase);


    private static readonly TimeSpan
        ArrivalRepeatDelay =
            TimeSpan.FromSeconds(90);


    public bool ShouldReactToArrival(
        DesktopApplicationContext application,
        DateTime now)
    {
        string key =
            application.ProcessName.Trim();


        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }


        if (_applicationVisits.TryGetValue(
                key,
                out DateTime lastVisit) &&
            now - lastVisit <
                ArrivalRepeatDelay)
        {
            return false;
        }


        _applicationVisits[key] =
            now;


        CleanupOldEntries(now);

        return true;
    }


    private void CleanupOldEntries(
        DateTime now)
    {
        if (_applicationVisits.Count < 20)
        {
            return;
        }


        var expired =
            new List<string>();


        foreach (var entry
                 in _applicationVisits)
        {
            if (now - entry.Value >
                TimeSpan.FromMinutes(15))
            {
                expired.Add(
                    entry.Key);
            }
        }


        foreach (string key
                 in expired)
        {
            _applicationVisits.Remove(
                key);
        }
    }
}